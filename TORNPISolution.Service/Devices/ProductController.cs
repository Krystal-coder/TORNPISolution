using System.IO.Ports;
using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{

    /// <summary>
    /// 被测产品（DUT）控制器：通过串口二进制协议与产品通讯。
    /// 串口参数从仪器链接配置（SerialLinkSettings）读取，支持配置页调整。
    /// 协议帧结构：[Head 0xEF] [Len] [Func] [Ctrl] [Data...] [CRC16-MODBUS 2B] [Tail 0xFE]
    /// Len = Payload 长度（Func + Ctrl + Data），不含 CRC。总帧长 = Payload + 5。
    /// 
    /// 通讯模式：
    ///   - 命令-应答：上位机发帧 → 产品回 ACK（写命令）或数据帧（读命令）
    ///   - 主动推送：产品测完后主动推帧（如血压结果 0x7B），上位机限时等待
    /// </summary>
    public class ProductController : IProductController
    {
        private readonly SerialPort _port;
        private readonly ILogService _log;
        private readonly int _commandTimeoutMs = 3000;

        /// <summary>
        /// 创建产品控制器。串口参数从 SerialLinkSettings 读取（波特率/数据位/校验位/停止位）。
        /// </summary>
        /// <param name="settings">仪器链接配置中的产品串口参数（config.Product）</param>
        /// <param name="log">日志服务（可选）</param>
        public ProductController(SerialLinkSettings settings, ILogService? log = null)
        {
            _log = log ?? new LogService(nameof(ProductController));
            var parity = settings.Parity switch
            {
                "Odd"  => Parity.Odd,
                "Even" => Parity.Even,
                _      => Parity.None
            };
            var stopBits = settings.StopBits switch
            {
                2 => StopBits.Two,
                _ => StopBits.One
            };
            _port = new SerialPort(settings.PortName, settings.BaudRate, parity, settings.DataBits, stopBits)
            {
                ReadTimeout = 2000,
                WriteTimeout = 2000,
                DtrEnable = true,
                RtsEnable = true
            };
        }

        public bool IsOpen => _port.IsOpen;

        public Task OpenAsync(CancellationToken ct = default) => Task.Run(() =>
        {
            if (!_port.IsOpen)
            {
                _port.Open();
                _log.Info($"产品串口 {_port.PortName} 已打开（{_port.BaudRate},{_port.DataBits}{_port.Parity.ToString()[0]}{_port.StopBits}）");
            }
        }, ct);

        public Task CloseAsync() => Task.Run(() =>
        {
            if (_port.IsOpen)
            {
                _port.Close();
                _log.Info($"产品串口 {_port.PortName} 已关闭");
            }
        });

        // ==================== 底层收发 ====================

        /// <summary>发送协议帧并等待响应帧（日志中标注命令名称便于追踪）</summary>
        private async Task<ProductProtocol.ParsedFrame?> SendCommandAsync(
            byte ctrlCode, string commandName = "", byte[]? data = null,
            int timeoutMs = 0, CancellationToken ct = default)
        {
            var timeout = timeoutMs > 0 ? timeoutMs : _commandTimeoutMs;
            var packet = ProductProtocol.BuildPacket(ctrlCode, data);
            var tag = string.IsNullOrEmpty(commandName) ? $"0x{ctrlCode:X2}" : commandName;

            await Task.Run(() =>
            {
                _port.Write(packet, 0, packet.Length);
                _log.Info($"产品 TX [{tag}]: {BitConverter.ToString(packet)}");
            }, ct).ConfigureAwait(false);

            var resp = await ReceiveFrameAsync(timeout, ct).ConfigureAwait(false);
            if (resp != null)
                _log.Info($"产品 RX [{tag}]: Func=0x{resp.FunctionCode:X2} Ctrl=0x{resp.ControlCode:X2} Data=[{BitConverter.ToString(resp.Payload)}]");
            else
                _log.Warn($"产品 RX [{tag}]: 超时无响应");
            return resp;
        }

        /// <summary>从串口接收一帧完整数据（按 Head/Len/Tail 定位帧边界，CRC 校验）</summary>
        private async Task<ProductProtocol.ParsedFrame?> ReceiveFrameAsync(
            int timeoutMs, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                var buffer = new List<byte>();
                var deadline = Environment.TickCount64 + timeoutMs;
                var pollCount = 0;
                var scanStart = 0;  // 已扫描到的位置，避免重复扫描

                while (Environment.TickCount64 < deadline)
                {
                    ct.ThrowIfCancellationRequested();
                    pollCount++;
                    var bytesAvailable = _port.BytesToRead;
                    
                    if (bytesAvailable > 0)
                    {
                        var chunk = new byte[bytesAvailable];
                        var count = _port.Read(chunk, 0, bytesAvailable);
                        for (var i = 0; i < count; i++)
                            buffer.Add(chunk[i]);

                        _log.Info($"ReceiveFrameAsync: 收到 {count} 字节，buffer 累计 {buffer.Count} 字节");

                        // 尝试从 buffer 中找到完整帧（从 scanStart 开始扫描）
                        for (var start = scanStart; start <= buffer.Count - 6; start++)
                        {
                            if (buffer[start] != ProductProtocol.Head) continue;
                            if (start + 1 >= buffer.Count) break;

                            var len = buffer[start + 1];
                            var frameEnd = start + len + 4;   // Tail 位置 = Head + Len + Payload(len) + CRC(2)

                            if (frameEnd >= buffer.Count) break;  // 帧不完整，等更多数据
                            if (buffer[frameEnd] != ProductProtocol.Tail)
                            {
                                // 帧尾不匹配，跳过这个帧头
                                _log.Debug($"ReceiveFrameAsync: 帧头位置 {start} 但帧尾不匹配，跳过");
                                continue;
                            }

                            var frame = buffer.Skip(start).Take(frameEnd - start + 1).ToArray();
                            var parsed = ProductProtocol.ParseFrame(frame);
                            if (parsed != null)
                            {
                                _log.Info($"产品 RX: Func=0x{parsed.FunctionCode:X2} Ctrl=0x{parsed.ControlCode:X2} Data=[{BitConverter.ToString(parsed.Payload)}]");
                                return parsed;
                            }
                            else
                            {
                                _log.Warn($"ReceiveFrameAsync: 找到帧头但 CRC 校验失败，帧内容: {BitConverter.ToString(frame)}");
                                // CRC 失败，跳过这个帧头，继续扫描下一个
                            }
                        }
                        
                        // 更新 scanStart，避免重复扫描已检查过的字节
                        scanStart = Math.Max(0, buffer.Count - 5);  // 保留最后5字节以防帧跨批次到达
                    }
                    else
                    {
                        Thread.Sleep(2);
                    }
                }

                _log.Warn($"产品响应超时（{timeoutMs}ms，已收 {buffer.Count} 字节，轮询 {pollCount} 次）");
                return null;
            }, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 等待产品主动推送的血压测量结果（功能码 0x07，控制码 0x7B）。
        /// 默认超时 60 秒（协议规定产品应在 1 分钟内推送结果）。
        /// </summary>
        public async Task<ProductBpReading?> WaitForBpPushResultAsync(
            int timeoutMs = 60000, CancellationToken ct = default)
        {
            _log.Info($"等待产品推送血压结果（超时 {timeoutMs}ms）...");

            return await Task.Run(() =>
            {
                var buffer = new List<byte>();
                var deadline = Environment.TickCount64 + timeoutMs;

                while (Environment.TickCount64 < deadline)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_port.BytesToRead > 0)
                    {
                        var chunk = new byte[_port.BytesToRead];
                        var count = _port.Read(chunk, 0, chunk.Length);
                        for (var i = 0; i < count; i++)
                            buffer.Add(chunk[i]);

                        // 扫描完整帧
                        for (var start = 0; start <= buffer.Count - 6; start++)
                        {
                            if (buffer[start] != ProductProtocol.Head) continue;
                            if (start + 1 >= buffer.Count) break;

                            var len = buffer[start + 1];
                            var frameEnd = start + len + 4;
                            if (frameEnd >= buffer.Count) break;
                            if (buffer[frameEnd] != ProductProtocol.Tail) continue;

                            var frame = buffer.Skip(start).Take(frameEnd - start + 1).ToArray();
                            var parsed = ProductProtocol.ParseFrame(frame);

                            // 移除已扫描的字节（含无效帧头）
                            buffer.RemoveRange(0, start + 1);

                            if (parsed != null)
                            {
                                if (parsed.ControlCode == ProductProtocol.CtrlGetBpResult)
                                {
                                    _log.Debug($"产品推送血压结果: [{BitConverter.ToString(parsed.Payload)}]");
                                    return ParseBpReading(parsed.Payload);
                                }
                                // 非目标帧，继续等
                                _log.Debug($"产品推送非目标帧 Ctrl=0x{parsed.ControlCode:X2}，继续等待");
                                break;
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }

                _log.Warn($"产品血压结果推送超时（{timeoutMs}ms）");
                return null;
            }, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 从 0x7B 响应 payload 解析血压读数。
        /// 当前预留格式：收缩压(uint16 BE) + 舒张压(uint16 BE) + 心率(uint16 BE) = 6 字节。
        /// TODO: 实机联调时按实际格式调整。
        /// </summary>
        private ProductBpReading? ParseBpReading(byte[] payload)
        {
            if (payload.Length < 6)
            {
                _log.Warn($"血压结果 payload 长度不足（{payload.Length} 字节，需 ≥6）");
                return null;
            }

            var systolic  = ProductProtocol.ReadUInt16BE(payload, 0);
            var diastolic = ProductProtocol.ReadUInt16BE(payload, 2);
            var heartRate = ProductProtocol.ReadUInt16BE(payload, 4);

            _log.Info($"血压解析：收缩压={systolic} 舒张压={diastolic} 心率={heartRate}");
            return new ProductBpReading(systolic, diastolic, heartRate);
        }

        // ==================== 具体命令方法 ====================

        /// <summary>
        /// 心跳保活（0x01）：防止产品进入休眠模式。
        /// 产品约10秒无通讯即休眠，每个测试模块开始时必须先调用此方法。
        /// </summary>
        public async Task KeepAliveAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送心跳保活（0x01）");
            await SendCommandAsync(ProductProtocol.CtrlKeepAlive, "KeepAlive", ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 产品问询（0x72）：读取产品 MAC 地址和嵌软版本。
        /// </summary>
        public async Task<string> QueryProductInfoAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送产品问询（0x72）");
            var resp = await SendCommandAsync(ProductProtocol.CtrlGetVersion, "QueryProductInfo", ct: ct).ConfigureAwait(false);
            if (resp == null) return string.Empty;

            var info = resp.Payload.Length > 0
                ? System.Text.Encoding.ASCII.GetString(resp.Payload).Trim()
                : BitConverter.ToString(resp.Payload);
            _log.Info($"产品问询返回：{info}");
            return info;
        }

        /// <summary>
        /// 产品当前状态查询（0x74）：读取产品当前测试模式和状态。
        /// </summary>
        public async Task<string> QueryProductStatusAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送产品状态查询（0x74）");
            var resp = await SendCommandAsync(ProductProtocol.CtrlGetState, "QueryProductStatus", ct: ct).ConfigureAwait(false);
            if (resp == null) return string.Empty;

            var status = resp.Payload.Length > 0
                ? BitConverter.ToString(resp.Payload)
                : "empty";
            _log.Info($"产品状态返回：{status}");
            return status;
        }

        /// <summary>
        /// 设置测试模式（0x73）：每次测量前必须先设置对应模式。
        /// </summary>
        /// <param name="mode">测试模式（0=空闲 1=袖带充放气 2=血压测试 3=静态压力 4=血氧 5=心电 6=加速度）</param>
        public async Task SetTestModeAsync(byte mode, CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 设置测试模式 mode={mode}（0x73）");
            await SendCommandAsync(ProductProtocol.CtrlSetMode, "SetTestMode", new byte[] { mode }, ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 读取产品当前静态压力值（0x7E）。
        /// BP-003 静态压力精度测试用：产品传感器检测到的 BPA700 施加的稳压值。
        /// 返回格式：uint32 BE（4 字节），单位 mmHg。
        /// </summary>
        public async Task<double> ReadProductPressureAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.CtrlGetStaticPress, "ReadProductPressure", ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 4)
            {
                _log.Warn("产品静态压力读取失败或 payload 不足 4 字节");
                return double.NaN;
            }

            var raw = ProductProtocol.ReadUInt32BE(resp.Payload, 0);
            var pressure = (double)raw;
            _log.Info($"产品上报静态压力：{pressure:0.0} mmHg（raw={raw}）");
            return pressure;
        }

        /// <summary>
        /// 读取产品血压测量最终结果（0x7B）。
        /// BP-004/BP-005 用：主动轮询读取（或等待推送后调用）。
        /// </summary>
        public async Task<ProductBpReading?> ReadBpResultAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.CtrlGetBpResult, "ReadBpResult", ct: ct).ConfigureAwait(false);
            if (resp == null) return null;
            return ParseBpReading(resp.Payload);
        }

        /// <summary>
        /// 开始血压测量（0x79）：发送后产品在一定时间内自动开始完整血压测量。
        /// 测完后产品主动推送结果（0x7B），也可通过 ReadBpResultAsync 主动读取。
        /// </summary>
        public async Task StartBpMeasureAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送开始血压测量指令（0x79）");
            await SendCommandAsync(ProductProtocol.CtrlStartBpMeasure, "StartBpMeasure", ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 停止血压测量（0x7D）。
        /// </summary>
        public async Task StopBpMeasureAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送停止血压测量指令（0x7D）");
            await SendCommandAsync(ProductProtocol.CtrlStopBpMeasure, "StopBpMeasure", ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 读取 ECG 导联状态（0x88）。
        /// ECG-002 导联脱落检测用。
        /// </summary>
        /// <returns>导联状态原始字节（具体位定义待实机联调确认）</returns>
        public async Task<byte[]> ReadLeadStateAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.CtrlGetLeadState, "ReadLeadState", ct: ct).ConfigureAwait(false);
            return resp?.Payload ?? Array.Empty<byte>();
        }

        /// <summary>
        /// 读取心率测量值（0x7C）。
        /// ECG-004 心率检测精度用。
        /// </summary>
        public async Task<double> ReadHeartRateAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.CtrlGetHrValue, "ReadHeartRate", ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 2)
            {
                _log.Warn("产品心率读取失败或 payload 不足 2 字节");
                return double.NaN;
            }

            var hr = ProductProtocol.ReadUInt16BE(resp.Payload, 0);
            _log.Info($"产品上报心率：{hr} BPM");
            return hr;
        }

        /// <summary>
        /// 开始/停止血氧测量（0x80）。
        /// </summary>
        /// <param name="start">true=开始，false=停止</param>
        public async Task StartSpo2MeasureAsync(bool start = true, CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} {(start ? "开始" : "停止")}血氧测量（0x80）");
            await SendCommandAsync(ProductProtocol.CtrlStartSpo2, "StartSpo2Measure",
                new byte[] { (byte)(start ? 0x01 : 0x00) }, ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 读取血氧测量值（0x81）。
        /// SPO2-001 血氧饱和度精度测试用。
        /// </summary>
        /// <returns>SpO2（%）；读取失败返回 NaN</returns>
        public async Task<double> ReadSpo2ValueAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.CtrlGetSpo2Value, "ReadSpo2Value", ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 2)
            {
                _log.Warn("产品血氧读取失败或 payload 不足 2 字节");
                return double.NaN;
            }

            // TODO: 实机联调时确认格式（当前预留：uint16 BE = SpO2%）
            var spo2 = ProductProtocol.ReadUInt16BE(resp.Payload, 0);
            _log.Info($"产品上报血氧：{spo2}%");
            return spo2;
        }

        /// <summary>
        /// 读取电池信息（0x6C）：返回电池电压（mV，uint32 LE 4字节）和百分比（%）。
        /// 响应帧 payload：[Voltage_0 Voltage_1 Voltage_2 Voltage_3 Percentage]
        /// </summary>
        public async Task<BatteryReading?> ReadBatteryInfoAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.CtrlGetBatteryInfo, "ReadBatteryInfo", ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 5)
            {
                _log.Warn($"产品电池信息读取失败或 payload 不足 5 字节（实际 {resp?.Payload.Length ?? 0}）");
                return null;
            }

            var voltageMv = ProductProtocol.ReadUInt32LE(resp.Payload, 0);
            var percentage = resp.Payload[4];
            _log.Info($"产品上报电池信息：电压={voltageMv}mV 百分比={percentage}%");
            return new BatteryReading(voltageMv, percentage);
        }

        /// <summary>
        /// 读取充电状态（0x6D）：返回充电状态字节（1=充电中，0=未充电）。
        /// </summary>
        public async Task<byte> ReadChargingStatusAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.CtrlGetChargingStatus, "ReadChargingStatus", ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 1)
            {
                _log.Warn("产品充电状态读取失败或 payload 为空");
                return 0;
            }

            var status = resp.Payload[0];
            _log.Info($"产品上报充电状态：{status}（1=充电中，0=未充电）");
            return status;
        }

        public void Dispose()
        {
            if (_port.IsOpen)
                _port.Close();
            _port.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
