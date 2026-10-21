using System.IO.Ports;
using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{

    /// <summary>
    /// 被测产品（DUT）串口控制器。
    /// 应答功能码可能与请求不同（如 05 请求 07 应答），收帧只按控制码匹配，不要加功能码校验。
    /// 多字节一律小端（实机电池电压证实）。0x7B 实测不推送、靠主动轮询；测量期间周期推的 0xCA 协议未定义，按非目标帧跳过。
    /// </summary>
    public class ProductController : IProductController
    {
        private readonly SerialPort _port;
        private readonly ILogService _log;
        private readonly int _commandTimeoutMs = 3000;

        // 串口收发互斥门：两个线程同时 Read 同一串口时，一帧字节可能被劈进两个接收缓冲区，双方都解析失败、双双超时误判。
        // 并发现场：BP-003 多点稳压窗内并发回读、BP-006/007 后台压力轮询与启停命令重叠；故把“清缓冲→发送→收帧”整段串行化（单次往返 ≈几十 ms）。
        private readonly SemaphoreSlim _portGate = new(1, 1);

        /// <summary>创建产品控制器：串口参数从 SerialLinkSettings 读取（波特率/数据位/校验位/停止位）</summary>
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
                SerialPortOpener.Open(_port);   // 限时打开： Open() 无超时，端口异常时会无限期挂起卡死整个方案
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

        /// <summary>
        /// 同步请求-应答：占住串口门 → 清接收缓冲 → 发一帧 → 等控制码相同的应答帧（超时无响应记 WARN 并返回 null）。
        /// </summary>
        /// <param name="commandName">命令语义名（写日志用，取与协议文档一致的函数名）</param>
        /// <param name="timeoutMs">超时（0 = 用默认）</param>
        /// <param name="decode">响应 payload → 可读物理量文本（如 <c>p => $"{...} mmHg"</c>），非 null 时解析值拼在 RX 行末尾</param>
        private async Task<ProductProtocol.ParsedFrame?> SendCommandAsync(
            ProductCmd cmd, string commandName = "", byte[]? data = null,
            int timeoutMs = 0, Func<byte[], string>? decode = null, CancellationToken ct = default)
        {
            var timeout = timeoutMs > 0 ? timeoutMs : _commandTimeoutMs;
            var packet = ProductProtocol.BuildPacket(cmd, data);
            var tag = string.IsNullOrEmpty(commandName) ? cmd.ToString() : $"{commandName}({cmd})";

            await _portGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    if (!_port.IsOpen)
                        throw new InvalidOperationException($"产品串口 {_port.PortName} 未打开");

                    // 清空接收缓冲：丢弃上一命令超时未读的残留字节与期间到达的主动推送帧，确保 ReceiveFrameAsync 读到的是本命令的应答
                    try { _port.DiscardInBuffer(); }
                    catch (Exception ex) { _log.Debug($"DiscardInBuffer 失败（忽略）：{ex.Message}"); }

                    _port.Write(packet, 0, packet.Length);
                    _log.Info($"产品 TX [{tag}]: {BitConverter.ToString(packet)}");
                }, ct).ConfigureAwait(false);

                var resp = await ReceiveFrameAsync(timeout, cmd.Code, ct).ConfigureAwait(false);
                if (resp != null)
                    _log.Info($"产品 RX [{tag}]: Func=0x{resp.FunctionCode:X2} Ctrl=0x{resp.ControlCode:X2} Data=[{BitConverter.ToString(resp.Payload)}]{DecodeSuffix(decode, resp.Payload)}");
                else
                    _log.Warn($"产品 RX [{tag}]: 超时无响应");
                return resp;
            }
            finally
            {
                _portGate.Release();
            }
        }

        /// <summary>把 decode 解析结果拼成 RX 行尾的“ → 值”后缀（解析异常不影响收发，只记录解析失败）</summary>
        private string DecodeSuffix(Func<byte[], string>? decode, byte[] payload)
        {
            if (decode == null) return string.Empty;
            try
            {
                var text = decode(payload);
                return string.IsNullOrEmpty(text) ? string.Empty : $" → {text}";
            }
            catch (Exception ex)
            {
                return $" → 解析失败({ex.Message})";
            }
        }

        /// <summary>通用命令发送：命令的 Func/Code 完全取自 protocol_commands.json，返回响应 payload（无响应返回 null），供扩展命令/实机联调使用</summary>
        public async Task<byte[]?> SendRawCommandAsync(ProductCmd cmd, byte[]? data = null, int timeoutMs = 0, CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(cmd, "RawCmd", data, timeoutMs, ct: ct).ConfigureAwait(false);
            return resp?.Payload;
        }

        /// <summary>
        /// 从串口接收一帧完整数据（按 Head/Len/Tail 定位帧边界，CRC 校验），帧解析委托 <see cref="TryExtractFrame"/>。
        /// 按控制码匹配应答（功能码不限：实测 05 请求可能 07 应答）；非目标帧（0xCA 周期推送等）跳过继续等；Ctrl=0xFF 为产品错误帧，立即结束等待。
        /// </summary>
        private async Task<ProductProtocol.ParsedFrame?> ReceiveFrameAsync(
            int timeoutMs, byte expectedCtrl, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                var buffer = new List<byte>();
                var deadline = Environment.TickCount64 + timeoutMs;
                var pollCount = 0;
                // 非目标帧按控制码计数，等完只汇总一条（心电波形 0x8D 连续推送时逐帧打日志会刷屏）
                Dictionary<byte, int>? skipped = null;

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

                        // 持续提取缓冲区中的完整帧，直到取到目标控制码或暂无完整帧
                        while (true)
                        {
                            var parsed = TryExtractFrame(buffer);
                            if (parsed == null) break;   // 帧不完整，回去继续读串口

                            if (parsed.ControlCode == 0xFF)
                            {
                                LogSkipped(skipped, expectedCtrl);
                                _log.Warn($"产品回错误帧 Ctrl=0xFF（命令不支持/被拒绝），结束本次等待");
                                return null;
                            }
                            if (parsed.ControlCode != expectedCtrl)
                            {
                                skipped ??= new Dictionary<byte, int>();
                                skipped[parsed.ControlCode] =
                                    skipped.TryGetValue(parsed.ControlCode, out var n) ? n + 1 : 1;
                                continue;
                            }

                            LogSkipped(skipped, expectedCtrl);
                            return parsed;   // RX 原始字节与解析值由 SendCommandAsync 带命令名统一打一行
                        }
                    }
                    else
                    {
                        Thread.Sleep(2);
                    }
                }

                LogSkipped(skipped, expectedCtrl);
                _log.Warn($"产品响应超时（{timeoutMs}ms，期望 Ctrl=0x{expectedCtrl:X2}，已收 {buffer.Count} 字节，轮询 {pollCount} 次）");
                return null;
            }, ct).ConfigureAwait(false);
        }

        /// <summary>等待期间收到的非目标帧汇总日志（一条说清“跳过了哪些控制码各几帧”）</summary>
        private void LogSkipped(Dictionary<byte, int>? skipped, byte expectedCtrl)
        {
            if (skipped == null || skipped.Count == 0) return;
            var detail = string.Join("，", skipped.Select(kv => $"0x{kv.Key:X2}×{kv.Value}"));
            _log.Info($"等待 Ctrl=0x{expectedCtrl:X2} 期间跳过非目标帧：{detail}");
        }

        /// <summary>
        /// 从缓冲区提取一个完整且 CRC 通过的帧并就地消费字节。
        /// 帧不完整时原样保留 buffer（帧头留在 0 位）以便续读；伪帧头只丢 1 字节；整帧含 CRC 失败帧一次性消费，否则残体会被当成新帧头。
        /// </summary>
        /// <param name="buffer">接收缓冲区（会被就地修改）</param>
        /// <returns>解析成功的帧；暂无完整帧时返回 null</returns>
        private ProductProtocol.ParsedFrame? TryExtractFrame(List<byte> buffer)
        {
            while (true)
            {
                var headPos = -1;
                for (var i = 0; i < buffer.Count; i++)
                {
                    if (buffer[i] == ProductProtocol.Head) { headPos = i; break; }
                }
                if (headPos < 0)
                {
                    // 整段无帧头，全部丢弃（帧头是单字节 0xEF，无需保留尾巴）
                    if (buffer.Count > 0)
                    {
                        _log.Debug($"TryExtractFrame: {buffer.Count} 字节内无帧头 0xEF，丢弃");
                        buffer.Clear();
                    }
                    return null;
                }
                if (headPos > 0)
                {
                    _log.Debug($"TryExtractFrame: 丢弃帧头前 {headPos} 字节垃圾");
                    buffer.RemoveRange(0, headPos);
                }

                if (buffer.Count < 2)
                    return null;   // 等更多数据（帧头保留在 position 0）

                var len = buffer[1];
                // Payload 至少含 Func + Ctrl = 2 字节；len < 2 为非法帧头，丢弃后继续找
                if (len < 2)
                {
                    _log.Debug($"TryExtractFrame: Len={len} 非法（<2），丢弃伪帧头");
                    buffer.RemoveRange(0, 1);
                    continue;
                }

                // 帧尾下标 = Head(0) + Len(1) + Payload(len) + CRC(2) = len + 4；整帧长 = len + 5
                var frameEnd = len + 4;
                if (frameEnd >= buffer.Count)
                    return null;   // 关键：不前进、不跳帧

                if (buffer[frameEnd] != ProductProtocol.Tail)
                {
                    _log.Debug($"TryExtractFrame: 帧头处 Len={len} 但帧尾非 0xFE，丢弃伪帧头");
                    buffer.RemoveRange(0, 1);
                    continue;
                }

                // 整帧交给 ParseFrame 解析（CRC 校验在其中）
                var frame = buffer.Take(frameEnd + 1).ToArray();
                var parsed = ProductProtocol.ParseFrame(frame);

                buffer.RemoveRange(0, frameEnd + 1);

                if (parsed != null)
                    return parsed;

                _log.Warn($"TryExtractFrame: 帧尾匹配但 CRC 校验失败，丢弃整帧：{BitConverter.ToString(frame)}");
                // CRC 失败，继续找下一帧
            }
        }

        /// <summary>
        /// 等待产品主动推送的血压结果（Ctrl=0x7B，功能码不限）：期间持 <see cref="_portGate"/> 独占串口，调用方不得并发发其他命令。
        /// </summary>
        public async Task<ProductBpReading?> WaitForBpPushResultAsync(
            int timeoutMs = 60000, CancellationToken ct = default)
        {
            _log.Info($"等待产品推送血压结果（超时 {timeoutMs}ms）...");

            await _portGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
            {
                var buffer = new List<byte>();
                var deadline = Environment.TickCount64 + timeoutMs;
                var targetCode = ProductProtocol.GetBpResult.Code;     // 0x7B

                while (Environment.TickCount64 < deadline)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_port.BytesToRead > 0)
                    {
                        var chunk = new byte[_port.BytesToRead];
                        var count = _port.Read(chunk, 0, chunk.Length);
                        for (var i = 0; i < count; i++)
                            buffer.Add(chunk[i]);
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }

                    // 持续提取缓冲区中的完整帧，直到取到目标 0x7B 或暂无完整帧
                    while (true)
                    {
                        var parsed = TryExtractFrame(buffer);
                        if (parsed == null)
                            break;   // 暂无更多完整帧，回去继续读串口

                        if (parsed.ControlCode == targetCode)
                        {
                            _log.Debug($"产品推送血压结果: [{BitConverter.ToString(parsed.Payload)}]");
                            return ParseBpReading(parsed.Payload);
                        }

                        // 非目标帧（如 0xCA 周期推送、心率/血氧推送），消费后继续等
                        _log.Debug($"产品推送非目标帧 Func=0x{parsed.FunctionCode:X2} Ctrl=0x{parsed.ControlCode:X2}，继续等待");
                    }
                }

                _log.Warn($"产品血压结果推送超时（{timeoutMs}ms）");
                return null;
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                _portGate.Release();
            }
        }

        /// <summary>从 0x7B 响应 payload 解析血压读数（协议 bp_measure_result_t，uint16 LE 三联：收缩压/舒张压/平均压 mmHg）；心率不在本帧，由 0x7C 单独读回</summary>
        private ProductBpReading? ParseBpReading(byte[] payload)
        {
            if (payload.Length < 6)
            {
                _log.Warn($"血压结果 payload 长度不足（{payload.Length} 字节，需 ≥6）");
                return null;
            }

            var systolic  = ProductProtocol.ReadUInt16LE(payload, 0);
            var diastolic = ProductProtocol.ReadUInt16LE(payload, 2);
            var meanPress = ProductProtocol.ReadUInt16LE(payload, 4);

            // 解析值已拼在 RX 行末（见 DescribeBpResult），这里只留调试级明细
            _log.Debug($"血压解析：收缩压={systolic} 舒张压={diastolic} 平均压={meanPress}");
            return new ProductBpReading(systolic, diastolic, meanPress);
        }

        // ==================== 响应解析文本（拼在“产品 RX”行末，一行看全收到什么 = 多少）====================

        /// <summary>0x76 压力值（mmHg）：协议定义 uint32（4 字节），实测固件回 2 字节，按长度自适应</summary>
        private static double ParsePressureMmHg(byte[] payload)
            => payload.Length >= 4
                ? ProductProtocol.ReadUInt32LE(payload, 0)
                : ProductProtocol.ReadUInt16LE(payload, 0);

        private static string DescribePressure(byte[] payload) => $"{ParsePressureMmHg(payload):0.##} mmHg";

        private static string DescribeHeartRate(byte[] payload) => $"{ProductProtocol.ReadUInt16LE(payload, 0)} BPM";

        private static string DescribeSpo2(byte[] payload) => $"{ProductProtocol.ReadUInt16BE(payload, 0)} %";

        private static string DescribeBpResult(byte[] payload)
            => payload.Length < 6 ? $"长度不足 {payload.Length}"
                : $"收{ProductProtocol.ReadUInt16LE(payload, 0)}/舒{ProductProtocol.ReadUInt16LE(payload, 2)}/平{ProductProtocol.ReadUInt16LE(payload, 4)} mmHg";

        private static string DescribeBattery(byte[] payload)
            => $"电压{ProductProtocol.ReadUInt32LE(payload, 0)}mV 电量{payload[4]}%";

        private static string DescribeCharging(byte[] payload) => payload[0] == 1 ? "充电中" : "未充电";

        /// <summary>0x80 过压状态（uint16：0=无过压 1=过压，实时）</summary>
        private static string DescribeOverPressure(byte[] payload) =>
            ProductProtocol.ReadUInt16LE(payload, 0) == 0 ? "无过压" : "过压";

        /// <summary>0x72 产品信息：6 字节 MAC + ASCII 嵌软版本</summary>
        private static string DescribeProductInfo(byte[] payload)
        {
            var mac = payload.Length >= 6 ? BitConverter.ToString(payload, 0, 6).Replace('-', ' ') : string.Empty;
            var ver = payload.Length > 6 ? System.Text.Encoding.ASCII.GetString(payload, 6, payload.Length - 6).Trim() : string.Empty;
            return $"MAC {mac} 版本 {ver}";
        }

        // ==================== 具体命令方法 ====================

        /// <summary>
        /// 进入产测模式（0x07/0x01）：打开串口后立即发送（约 10 秒无通讯产品即休眠）；协议要求必须在 0x72/0x74 查询之前、每个测试模块开始时先调用。
        /// </summary>
        public async Task EnterProductionTestModeAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送进入产测模式命令（{ProductProtocol.EnterProductionTestMode}，防产品休眠）");
            await SendCommandAsync(ProductProtocol.EnterProductionTestMode, "EnterProdTest", ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 产品信息获取（0x72）：payload = 6 字节 MAC + ASCII 嵌软版本，MAC 输出空格分隔 hex（如 0C 5A 7A 4E D2 8F）；无响应时两者均回空串。
        /// </summary>
        public async Task<(string MacText, string Version)> QueryProductInfoAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送产品信息获取（0x72）");
            var resp = await SendCommandAsync(ProductProtocol.GetVersion, "QueryProductInfo",
                decode: DescribeProductInfo, ct: ct).ConfigureAwait(false);
            if (resp == null) return (string.Empty, string.Empty);

            var mac = resp.Payload.Length >= 6
                ? BitConverter.ToString(resp.Payload, 0, 6).Replace('-', ' ')
                : string.Empty;
            var version = resp.Payload.Length > 6
                ? System.Text.Encoding.ASCII.GetString(resp.Payload, 6, resp.Payload.Length - 6).Trim()
                : string.Empty;
            return (mac, version);
        }

        public async Task<string> QueryProductStatusAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送产品状态查询（0x74）");
            var resp = await SendCommandAsync(ProductProtocol.GetState, "QueryProductStatus",
                decode: p => $"模式/状态 = {BitConverter.ToString(p)}", ct: ct).ConfigureAwait(false);
            if (resp == null) return string.Empty;

            // 回包 payload = 测试模式 + 测试状态各 1 字节：只显示原始字节（如 00-00），不做语义解读
            return BitConverter.ToString(resp.Payload);
        }

        /// <summary>读设备端状态（0x74）原始两字节 payload = test_mode + test_state（各 1 字节），供需要按字节判定回读的测试项使用</summary>
        public async Task<(byte TestMode, byte TestState)?> ReadDeviceStateAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 读设备端状态（0x74）");
            var resp = await SendCommandAsync(ProductProtocol.GetState, "ReadDeviceState",
                decode: p => $"模式/状态 = {BitConverter.ToString(p)}", ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 2) return null;

            return (resp.Payload[0], resp.Payload[1]);
        }

        /// <summary>
        /// 设置测试模式（0x73）：每次测量前必须先设置对应模式；协议参数固定 2 字节 test_mode + status（只发 1 字节产品不应答）。
        /// </summary>
        /// <param name="mode">0=空闲 1=袖带充放气 2=血压测试 3=静态压力 4=血氧 5=心电 6=加速度</param>
        /// <param name="status">0=进入测量 1=退出测量（默认 0）</param>
        public async Task SetTestModeAsync(byte mode, byte status = 0, CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 设置测试模式 mode={mode} status={status}（0x73）");
            await SendCommandAsync(ProductProtocol.SetMode, "SetTestMode", new byte[] { mode, status }, ct: ct).ConfigureAwait(false);
        }

        /// <summary>启动压力传感器（0x07 0x65）：产品进入压力静态测试态，静态压力校准流程的前置命令</summary>
        public async Task StartPressureSensorAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送启动压力传感器（0x65）");
            await SendCommandAsync(ProductProtocol.StartPressureSensor, "StartPressureSensor", ct: ct).ConfigureAwait(false);
        }

        /// <summary>校准压力传感器（0x07 0x66 + uint16 LE 校准压力）：仪器稳压到位后逐点发送，产品以当前压力为该校准点基准（帧样例 EF 04 07 66 3C 00 xx xx FE = 60mmHg）</summary>
        public async Task CalibratePressureSensorAsync(ushort pressureMmHg, CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送压力传感器校准（0x66）校准点={pressureMmHg}mmHg");
            await SendCommandAsync(ProductProtocol.CalibratePressureSensor, "CalibratePressureSensor",
                new byte[] { (byte)(pressureMmHg & 0xFF), (byte)(pressureMmHg >> 8) }, ct: ct).ConfigureAwait(false);
        }

        /// <summary>关闭压力传感器（0x07 0x67）：校准模块全部测试项结束后发送，归还产品常态</summary>
        public async Task ClosePressureSensorAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送关闭压力传感器（0x67）");
            await SendCommandAsync(ProductProtocol.ClosePressureSensor, "ClosePressureSensor", ct: ct).ConfigureAwait(false);
        }

        /// <summary>读取产品当前静态压力值（0x76 读产品压力值，实测帧 EF 02 05 76 17 86 FE）：BP-003 静态压力精度测试取产品传感器检测到的模拟仪施加稳压值（mmHg）；字节数自适应见 <see cref="ParsePressureMmHg"/></summary>
        public async Task<double> ReadProductPressureAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetPressure, "ReadProductPressure",
                decode: DescribePressure, ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 2)
            {
                _log.Warn("产品静态压力读取失败或 payload 不足 2 字节");
                return double.NaN;
            }

            return ParsePressureMmHg(resp.Payload);
        }

        /// <summary>读取产品血压测量最终结果（0x7B）：BP-004/BP-005 用，主动轮询读取（或等待推送后调用）</summary>
        public async Task<ProductBpReading?> ReadBpResultAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetBpResult, "ReadBpResult",
                decode: DescribeBpResult, ct: ct).ConfigureAwait(false);
            if (resp == null) return null;
            return ParseBpReading(resp.Payload);
        }

        /// <summary>开始/关闭一次完整血压测量（0x79，参数 uint8 status：0=开始 1=关闭）；协议要求测量完成后须下发一次关闭，才能再次开始测量</summary>
        public async Task StartBpMeasureAsync(bool start = true, CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送{(start ? "开始" : "关闭")}完整血压测量指令（0x79 status={(start ? 0 : 1)}）");
            await SendCommandAsync(ProductProtocol.StartBpMeasure, "StartBpMeasure",
                new byte[] { (byte)(start ? 0 : 1) }, ct: ct).ConfigureAwait(false);
        }

        /// <summary>开始充气（0x75 + 4 字节参数：uint16 LE 目标压力mmHg 在前、uint16 LE 最长充气时间秒在后）：BP-009 过压保护用，由产品气泵充到指定压、模拟仪不参与加压，超时后产品底层自行放气</summary>
        public async Task StartInflateAsync(ushort targetMmHg, ushort timeoutSeconds, CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送开始充气（0x75）目标压力={targetMmHg}mmHg 最长充气={timeoutSeconds}s");
            await SendCommandAsync(ProductProtocol.StartInflate, "StartInflate",
                new byte[]
                {
                    (byte)(targetMmHg & 0xFF), (byte)(targetMmHg >> 8),
                    (byte)(timeoutSeconds & 0xFF), (byte)(timeoutSeconds >> 8),
                }, ct: ct).ConfigureAwait(false);
        }

        /// <summary>读过压状态（0x80，无参数）：payload = uint16 LE，0=无过压 1=过压，实时状态，无响应回 null；BP-009 取 0→1 跳变时刻作为产品保护动作时刻</summary>
        public async Task<bool?> ReadOverPressureStateAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetOverPressureStatus, "ReadOverPressureState",
                decode: DescribeOverPressure, ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 2) return null;

            return ProductProtocol.ReadUInt16LE(resp.Payload, 0) != 0;
        }

        /// <summary>读取 ECG 导联状态（0x88）</summary>
        /// <returns>导联状态原始字节（具体位定义待实机联调确认）</returns>
        public async Task<byte[]> ReadLeadStateAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetLeadState, "ReadLeadState", ct: ct).ConfigureAwait(false);
            return resp?.Payload ?? Array.Empty<byte>();
        }

        /// <summary>开始ECG测试（协议命令 CtrlStartEcgTest：Func=0x05/Ctrl=0x73，参数默认 05 00，完整帧 EF 04 05 73 05 00 15 03 FE）：产品开始持续推送心电波形（0x8D）直到收到关闭命令</summary>
        public Task StartEcgTestAsync(CancellationToken ct = default)
            => SendEcgTestToggleAsync(ProductProtocol.StartEcgTest, ProductProtocol.StartEcgTestKey,
                "StartEcgTest", "开始ECG测试", new byte[] { ProductProtocol.ModeEcg, 0 }, ct);

        /// <summary>关闭ECG测试（协议命令 CtrlStopEcgTest：Func=0x05/Ctrl=0x73，参数默认 05 01，完整帧 EF 04 05 73 05 01 D4 C3 FE）：产品停止推送心电波形</summary>
        public Task StopEcgTestAsync(CancellationToken ct = default)
            => SendEcgTestToggleAsync(ProductProtocol.StopEcgTest, ProductProtocol.StopEcgTestKey,
                "StopEcgTest", "关闭ECG测试", new byte[] { ProductProtocol.ModeEcg, 1 }, ct);

        /// <summary>开始/关闭ECG测试共用发送：参数优先取协议命令配置页的 Data 列，未配置时用 <paramref name="fallbackData"/></summary>
        private async Task SendEcgTestToggleAsync(ProductCmd cmd, string cmdKey, string commandName,
            string label, byte[] fallbackData, CancellationToken ct)
        {
            var data = ProductProtocol.ResolveCmdData(cmdKey, fallbackData);
            _log.Info($"{DateTime.Now} {label}（{cmd} 参数 {BitConverter.ToString(data).Replace('-', ' ')}）");
            await SendCommandAsync(cmd, commandName, data, ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 采集产品主动推送的心电波形（Func=0x05 / Ctrl=0x8D，每帧 75 点 = 300ms @ 250SPS）。
        /// 整段窗口（skipMs + durationMs）持 <see cref="_portGate"/> 独占串口：命令收发写前会 DiscardInBuffer，采集期间并发发命令会丢弃波形帧甚至拆成半帧，调用方不得并发读其他值。
        /// 前 skipMs 只排空串口不记录（避开产品刚进入心电测量的起始瞬态），数据段帧时刻以 skipMs 为原点、与仪器输出曲线共用时间轴；跳档以整帧为单位（最多丢 300ms 边界数据）。
        /// 逐帧不记日志（60s ≈ 200 帧），只在结束时汇总一条。
        /// </summary>
        public async Task<int> CaptureEcgWaveAsync(
            int durationMs, int skipMs = 0, Action<double, double[]>? onFrame = null, CancellationToken ct = default)
        {
            var waveCtrl = ProductProtocol.PushEcgWave.Code;      // 0x8D
            var skipSeconds = skipMs / 1000.0;
            _log.Info($"开始采集产品心电波形推送（丢弃前 {skipSeconds:0.#}s，记录 {durationMs / 1000.0:0.#}s，"
                + $"{ProductProtocol.EcgSampleRate}SPS，目标帧 Ctrl=0x{waveCtrl:X2}）...");

            await _portGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    var buffer = new List<byte>();
                    var deadline = Environment.TickCount64 + skipMs + durationMs;
                    var recordFrom = Environment.TickCount64 + skipMs;
                    var frames = 0;
                    var samples = 0;
                    var discarded = 0;
                    var otherFrames = 0;

                    // 丢弃进入心电模式前已到达的残留字节，从帧边界开始采
                    try { _port.DiscardInBuffer(); }
                    catch (Exception ex) { _log.Debug($"DiscardInBuffer 失败（忽略）：{ex.Message}"); }

                    while (Environment.TickCount64 < deadline)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (_port.BytesToRead > 0)
                        {
                            var chunk = new byte[_port.BytesToRead];
                            var count = _port.Read(chunk, 0, chunk.Length);
                            for (var i = 0; i < count; i++)
                                buffer.Add(chunk[i]);

                            while (true)
                            {
                                var parsed = TryExtractFrame(buffer);
                                if (parsed == null) break;      // 暂无完整帧，继续读串口

                                if (parsed.ControlCode != waveCtrl)
                                {
                                    otherFrames++;              // 0xCA 周期推送等，静默计数
                                    continue;
                                }

                                var millivolts = ProductProtocol.ParseEcgWave(parsed.Payload);
                                if (millivolts == null)
                                {
                                    _log.Warn($"心电波形帧 payload 不足 2 字节：[{BitConverter.ToString(parsed.Payload)}]");
                                    continue;
                                }

                                if (Environment.TickCount64 < recordFrom)
                                {
                                    discarded += millivolts.Length;   // 丢弃段：只排串口不记录
                                    continue;
                                }

                                onFrame?.Invoke(skipSeconds + (double)samples / ProductProtocol.EcgSampleRate, millivolts);
                                frames++;
                                samples += millivolts.Length;
                            }
                        }
                        else
                        {
                            Thread.Sleep(2);
                        }
                    }

                    _log.Info($"产品心电波形采集结束：丢弃 {discarded} 点（前 {skipSeconds:0.#}s），有效 {frames} 帧 / {samples} 点"
                        + $"（≈{samples / (double)ProductProtocol.EcgSampleRate:0.0}s，窗口 {durationMs / 1000.0:0.#}s"
                        + (otherFrames > 0 ? $"，期间跳过非波形帧 {otherFrames} 个" : string.Empty) + ")");
                    return samples;
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                _portGate.Release();
            }
        }

        /// <summary>读取心率测量值（0x7C）</summary>
        public async Task<double> ReadHeartRateAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetHrValue, "ReadHeartRate",
                decode: DescribeHeartRate, ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 2)
            {
                _log.Warn("产品心率读取失败或 payload 不足 2 字节");
                return double.NaN;
            }

            return ProductProtocol.ReadUInt16LE(resp.Payload, 0);
        }

        /// <summary>开始/停止血氧测量（0x81，Func=0x05）</summary>
        public async Task StartSpo2MeasureAsync(bool start = true, CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} {(start ? "开始" : "停止")}血氧测量（0x81）");
            await SendCommandAsync(ProductProtocol.StartSpo2, "StartSpo2Measure",
                new byte[] { (byte)(start ? 0x00 : 0x01) }, ct: ct).ConfigureAwait(false);
        }

        /// <summary>读取血氧测量值（0x82，Func=0x05）</summary>
        /// <returns>SpO2（%）；读取失败返回 NaN</returns>
        public async Task<double> ReadSpo2ValueAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetSpo2Value, "ReadSpo2Value",
                decode: DescribeSpo2, ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 2)
            {
                _log.Warn("产品血氧读取失败或 payload 不足 2 字节");
                return double.NaN;
            }

            // TODO: 实机联调时确认格式（当前预留：uint16 BE = SpO2%）
            return ProductProtocol.ReadUInt16BE(resp.Payload, 0);
        }

        /// <summary>读取电池信息（0x6C，Func=0x07）：响应 payload 共 8 字节（协议 fac_bat_req）— [0..3] 电压 uint32 LE（mV）、[4] 百分比（0-100%）、[5..7] 保留字节 rese[3]（不解析）</summary>
        public async Task<BatteryReading?> ReadBatteryInfoAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetBatteryInfo, "ReadBatteryInfo",
                decode: DescribeBattery, ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 5)
            {
                _log.Warn($"产品电池信息读取失败或 payload 不足 5 字节（实际 {resp?.Payload.Length ?? 0}）");
                return null;
            }

            var payload = resp.Payload;
            var voltageMv = ProductProtocol.ReadUInt32LE(payload, 0);
            var percentage = payload[4];

            return new BatteryReading(voltageMv, percentage, payload);
        }

        /// <summary>读取充电状态（0x6D，Func=0x07）：返回充电状态字节（1=充电中，0=未充电）</summary>
        public async Task<byte> ReadChargingStatusAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetChargingStatus, "ReadChargingStatus",
                decode: DescribeCharging, ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 1)
            {
                _log.Warn("产品充电状态读取失败或 payload 为空");
                return 0;
            }

            return resp.Payload[0];
        }

        public void Dispose()
        {
            if (_port.IsOpen)
                _port.Close();
            _port.Dispose();
            // 不释放 _portGate：SemaphoreSlim 只在用到 AvailableWaitHandle 时持内核句柄，本类全程只用 WaitAsync/Release，交 GC 回收；显式 Dispose 会让仍在等待的调用方抛 ObjectDisposedException。
            GC.SuppressFinalize(this);
        }
    }
}
