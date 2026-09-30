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
    ///   - 命令-应答：上位机发帧 → 产品回 ACK（写命令）或数据帧（读命令）；应答功能码可能与请求不同（如 05 请求 07 应答），按控制码匹配
    ///   - 主动推送：产品测量期间周期推 0xCA（协议未定义，按非目标帧跳过）；0x7B 结果实测不推送，靠主动轮询
    /// 多字节整数一律小端（实机电池电压证实；若联调发现数值字节反转改此处）。
    /// 命令的功能码/控制码均来自 Config\protocol_commands.json（见 ProductProtocol），
    /// 配置页保存后热重载，串口收发严格按配置执行。
    /// </summary>
    public class ProductController : IProductController
    {
        private readonly SerialPort _port;
        private readonly ILogService _log;
        private readonly int _commandTimeoutMs = 3000;

        // 串口收发互斥门：并发调用（BP-003 多点稳压窗内并发回读、BP-006/007 后台压力轮询与启停命令重叠）时，
        // 若两个线程同时 Read 同一串口，一帧字节可能被劈进两个接收缓冲区，导致双方都解析失败双双超时误判；
        // 把“清缓冲→发送→收帧”整段串行化消除竞争（正常单次往返 ≈几十 ms，串行后吞吐足够）。
        private readonly SemaphoreSlim _portGate = new(1, 1);

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
                SerialPortOpener.Open(_port);   // 限时打开：原生 Open() 无超时，端口异常时会无限期挂起卡死整个方案
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
        /// 发送协议帧并等待响应帧（日志中标注命令名称便于追踪）。
        /// 整段“清缓冲→发送→收帧”持 <see cref="_portGate"/> 互斥，并发调用方自动排队。
        /// Write 前先 DiscardInBuffer 清空接收缓冲，避免上一次超时残留的旧字节
        /// 被误当成本次命令的应答；等待期间到达的非目标帧（如 0xCA 周期推送）按控制码过滤跳过。
        /// </summary>
        private async Task<ProductProtocol.ParsedFrame?> SendCommandAsync(
            ProductCmd cmd, string commandName = "", byte[]? data = null,
            int timeoutMs = 0, CancellationToken ct = default)
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

                    // 清空接收缓冲：丢弃上一命令超时未读的残留字节与期间到达的主动推送帧，
                    // 确保接下来 ReceiveFrameAsync 读到的是本命令的应答。
                    try { _port.DiscardInBuffer(); }
                    catch (Exception ex) { _log.Debug($"DiscardInBuffer 失败（忽略）：{ex.Message}"); }

                    _port.Write(packet, 0, packet.Length);
                    _log.Info($"产品 TX [{tag}]: {BitConverter.ToString(packet)}");
                }, ct).ConfigureAwait(false);

                var resp = await ReceiveFrameAsync(timeout, cmd.Code, ct).ConfigureAwait(false);
                if (resp != null)
                    _log.Info($"产品 RX [{tag}]: Func=0x{resp.FunctionCode:X2} Ctrl=0x{resp.ControlCode:X2} Data=[{BitConverter.ToString(resp.Payload)}]");
                else
                    _log.Warn($"产品 RX [{tag}]: 超时无响应");
                return resp;
            }
            finally
            {
                _portGate.Release();
            }
        }

        /// <summary>
        /// 通用命令发送：按协议命令配置发送任意命令并返回响应 payload（无响应返回 null）。
        /// 供扩展命令/实机联调使用，命令的 Func/Code 完全取自 protocol_commands.json。
        /// </summary>
        public async Task<byte[]?> SendRawCommandAsync(ProductCmd cmd, byte[]? data = null, int timeoutMs = 0, CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(cmd, "RawCmd", data, timeoutMs, ct).ConfigureAwait(false);
            return resp?.Payload;
        }

        /// <summary>
        /// 从串口接收一帧完整数据（按 Head/Len/Tail 定位帧边界，CRC 校验）。
        /// 帧解析统一委托 <see cref="TryExtractFrame"/>，避免自行维护 scanStart 造成的跳帧/漏帧。
        /// 按控制码匹配应答（功能码不限：实测 05 请求可能 07 应答）；
        /// 非目标帧（0xCA 周期推送等）跳过继续等；Ctrl=0xFF 为产品错误帧，立即结束等待。
        /// </summary>
        private async Task<ProductProtocol.ParsedFrame?> ReceiveFrameAsync(
            int timeoutMs, byte expectedCtrl, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                var buffer = new List<byte>();
                var deadline = Environment.TickCount64 + timeoutMs;
                var pollCount = 0;

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

                        // 持续提取缓冲区中的完整帧，直到取到目标控制码或暂无完整帧
                        while (true)
                        {
                            var parsed = TryExtractFrame(buffer);
                            if (parsed == null) break;   // 帧不完整，回去继续读串口

                            if (parsed.ControlCode == 0xFF)
                            {
                                _log.Warn($"产品回错误帧 Ctrl=0xFF（命令不支持/被拒绝），结束本次等待");
                                return null;
                            }
                            if (parsed.ControlCode != expectedCtrl)
                            {
                                _log.Debug($"收到非目标帧 Func=0x{parsed.FunctionCode:X2} Ctrl=0x{parsed.ControlCode:X2}（期望 0x{expectedCtrl:X2}，如 0xCA 周期推送），跳过继续等");
                                continue;
                            }

                            _log.Info($"产品 RX: Func=0x{parsed.FunctionCode:X2} Ctrl=0x{parsed.ControlCode:X2} Data=[{BitConverter.ToString(parsed.Payload)}]");
                            return parsed;
                        }
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
        /// 从缓冲区提取一个完整且 CRC 通过的帧，并就地消费已处理的字节。
        /// 设计要点（修复原 scanStart 跳帧 bug）：
        ///   - 帧头前的垃圾字节直接丢弃；
        ///   - 找到帧头但帧不完整时，<b>保留 buffer 原样</b>返回 null（帧头仍在 position 0），
        ///     下次收到更多字节后从同一帧头继续判定，绝不会把未读完的帧头跳过；
        ///   - 帧尾不匹配说明该 0xEF 是伪帧头，丢弃 1 字节后继续找；
        ///   - 整帧（含 CRC 失败帧）一次性消费，避免残留帧体被误扫成新帧头。
        /// </summary>
        /// <param name="buffer">接收缓冲区（会被就地修改）</param>
        /// <returns>解析成功的帧；暂无完整帧时返回 null</returns>
        private ProductProtocol.ParsedFrame? TryExtractFrame(List<byte> buffer)
        {
            while (true)
            {
                // 1. 定位帧头，丢弃帧头前的垃圾字节
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

                // 2. 至少要有 Head + Len 两字节才能判断帧长
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

                // 3. 帧尾下标 = Head(0) + Len(1) + Payload(len) + CRC(2) = len + 4；整帧长 = len + 5
                var frameEnd = len + 4;
                if (frameEnd >= buffer.Count)
                    return null;   // 帧不完整，保留 buffer（帧头在 0）等更多数据 —— 关键：不前进、不跳帧

                if (buffer[frameEnd] != ProductProtocol.Tail)
                {
                    // 帧尾不匹配 → 当前 0xEF 是伪帧头，丢弃 1 字节继续扫描
                    _log.Debug($"TryExtractFrame: 帧头处 Len={len} 但帧尾非 0xFE，丢弃伪帧头");
                    buffer.RemoveRange(0, 1);
                    continue;
                }

                // 4. 取出整帧解析（CRC 校验在 ParseFrame 内）
                var frame = buffer.Take(frameEnd + 1).ToArray();
                var parsed = ProductProtocol.ParseFrame(frame);

                // 无论 CRC 是否通过，整帧都消费掉，避免残留帧体被误扫
                buffer.RemoveRange(0, frameEnd + 1);

                if (parsed != null)
                    return parsed;

                _log.Warn($"TryExtractFrame: 帧尾匹配但 CRC 校验失败，丢弃整帧：{BitConverter.ToString(frame)}");
                // CRC 失败，继续找下一帧
            }
        }

        /// <summary>
        /// 等待产品主动推送的血压测量结果（控制码 0x7B，功能码不限）。
        /// 预留：实测当前固件不主动推 0x7B（测量期间只周期推 0xCA，协议未定义），BP-004/005 已改主动轮询。
        /// 等待期间持 <see cref="_portGate"/> 独占串口（避免与命令收发劈帧），调用方不得并发发其他命令。
        /// 帧提取复用 <see cref="TryExtractFrame"/>，修复原实现 RemoveRange 只删帧头不删整帧、
        /// 非目标帧 break 后残留帧体被重复误扫导致的缓冲区错位问题。
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
                var targetCode = ProductProtocol.GetBpResult.Code;     // 0x7B（功能码不限：实测应答可能 05 也可能 07）

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

        /// <summary>
        /// 从 0x7B 响应 payload 解析血压读数（协议 bp_measure_result_t，uint16 LE 三联）：
        /// 收缩压 + 舒张压 + 平均压（mmHg）；心率不在本帧，由 0x7C 单独读回。
        /// </summary>
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

            _log.Info($"血压解析：收缩压={systolic} 舒张压={diastolic} 平均压={meanPress}");
            return new ProductBpReading(systolic, diastolic, meanPress);
        }

        // ==================== 具体命令方法 ====================

        /// <summary>
        /// 进入产测模式（0x07/0x01）：打开串口后立即发送，防止产品进入休眠模式（约10秒无通讯即休眠）。
        /// 协议要求：必须在 0x72/0x74 查询之前发送，每个测试模块开始时必须先调用此方法。
        /// </summary>
        public async Task EnterProductionTestModeAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送进入产测模式命令（{ProductProtocol.EnterProductionTestMode}，防产品休眠）");
            await SendCommandAsync(ProductProtocol.EnterProductionTestMode, "EnterProdTest", ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 产品信息获取（0x72）：回读 MAC 地址与嵌软版本（payload = 6 字节 MAC + ASCII 版本）。
        /// MAC 格式化为空格分隔 hex（0C 5A 7A 4E D2 8F）；无响应时两者均回空串。
        /// </summary>
        public async Task<(string MacText, string Version)> QueryProductInfoAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送产品信息获取（0x72）");
            var resp = await SendCommandAsync(ProductProtocol.GetVersion, "QueryProductInfo", ct: ct).ConfigureAwait(false);
            if (resp == null) return (string.Empty, string.Empty);

            var mac = resp.Payload.Length >= 6
                ? BitConverter.ToString(resp.Payload, 0, 6).Replace('-', ' ')
                : string.Empty;
            var version = resp.Payload.Length > 6
                ? System.Text.Encoding.ASCII.GetString(resp.Payload, 6, resp.Payload.Length - 6).Trim()
                : string.Empty;
            _log.Info($"产品信息获取返回：MAC {mac} 嵌软版本 {version}（原始 {BitConverter.ToString(resp.Payload)}）");
            return (mac, version);
        }

        /// <summary>
        /// 产品当前状态查询（0x74）：读取产品当前测试模式和状态。
        /// </summary>
        public async Task<string> QueryProductStatusAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送产品状态查询（0x74）");
            var resp = await SendCommandAsync(ProductProtocol.GetState, "QueryProductStatus", ct: ct).ConfigureAwait(false);
            if (resp == null) return string.Empty;

            // 回包 payload = 测试模式 + 测试状态各 1 字节：只显示原始字节（如 00-00），不做语义解读
            var status = BitConverter.ToString(resp.Payload);
            _log.Info($"产品状态返回：{status}");
            return status;
        }

        /// <summary>
        /// 设置测试模式（0x73）：每次测量前必须先设置对应模式。
        /// 协议参数 2 字节：test_mode + status（0=进入测量 1=退出测量）；早期只发 1 字节产品不应答，按文档补齐。
        /// </summary>
        /// <param name="mode">测试模式（0=空闲 1=袖带充放气 2=血压测试 3=静态压力 4=血氧 5=心电 6=加速度）</param>
        /// <param name="status">0=进入测量 1=退出测量（默认 0）</param>
        public async Task SetTestModeAsync(byte mode, byte status = 0, CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 设置测试模式 mode={mode} status={status}（0x73）");
            await SendCommandAsync(ProductProtocol.SetMode, "SetTestMode", new byte[] { mode, status }, ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 启动压力传感器（0x07 0x65）：产品进入压力静态测试态，静态压力校准流程的前置命令。
        /// </summary>
        public async Task StartPressureSensorAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送启动压力传感器（0x65）");
            await SendCommandAsync(ProductProtocol.StartPressureSensor, "StartPressureSensor", ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 校准压力传感器（0x07 0x66 + uint16 LE 校准压力）：仪器稳压到位后逐点发送，
        /// 产品以当前压力为该校准点基准（帧样例 EF 04 07 66 3C 00 xx xx FE = 60mmHg）。
        /// </summary>
        public async Task CalibratePressureSensorAsync(ushort pressureMmHg, CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送压力传感器校准（0x66）校准点={pressureMmHg}mmHg");
            await SendCommandAsync(ProductProtocol.CalibratePressureSensor, "CalibratePressureSensor",
                new byte[] { (byte)(pressureMmHg & 0xFF), (byte)(pressureMmHg >> 8) }, ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 关闭压力传感器（0x07 0x67）：校准模块全部测试项结束后发送，归还产品常态。
        /// </summary>
        public async Task ClosePressureSensorAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送关闭压力传感器（0x67）");
            await SendCommandAsync(ProductProtocol.ClosePressureSensor, "ClosePressureSensor", ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 读取产品当前静态压力值（0x76 读产品压力值，实测帧 EF 02 05 76 17 86 FE）。
        /// BP-003 静态压力精度测试用：产品传感器检测到的模拟仪施加的稳压值。
        /// 协议定义 uint32（4 字节），实测当前固件回 2 字节：按长度自适应（≥4 字节 uint32 LE，2 字节 uint16 LE），单位 mmHg。
        /// </summary>
        public async Task<double> ReadProductPressureAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetPressure, "ReadProductPressure", ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 2)
            {
                _log.Warn("产品静态压力读取失败或 payload 不足 2 字节");
                return double.NaN;
            }

            var raw = resp.Payload.Length >= 4
                ? ProductProtocol.ReadUInt32LE(resp.Payload, 0)
                : ProductProtocol.ReadUInt16LE(resp.Payload, 0);
            var pressure = (double)raw;
            _log.Info($"产品上报静态压力：{pressure:0.0} mmHg（raw={raw}，{resp.Payload.Length} 字节）");
            return pressure;
        }

        /// <summary>
        /// 读取产品血压测量最终结果（0x7B）。
        /// BP-004/BP-005 用：主动轮询读取（或等待推送后调用）。
        /// </summary>
        public async Task<ProductBpReading?> ReadBpResultAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetBpResult, "ReadBpResult", ct: ct).ConfigureAwait(false);
            if (resp == null) return null;
            return ParseBpReading(resp.Payload);
        }

        /// <summary>
        /// 开始/关闭一次完整血压测量（0x79，参数 uint8 status：0=开始 1=关闭）。
        /// 协议要求：测量完成后需下发一次关闭，才能再次开始测量。
        /// </summary>
        /// <param name="start">true=开始一次测量，false=关闭一次完整测量</param>
        public async Task StartBpMeasureAsync(bool start = true, CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送{(start ? "开始" : "关闭")}完整血压测量指令（0x79 status={(start ? 0 : 1)}）");
            await SendCommandAsync(ProductProtocol.StartBpMeasure, "StartBpMeasure",
                new byte[] { (byte)(start ? 0 : 1) }, ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 停止血压测量（0x7D）。
        /// </summary>
        public async Task StopBpMeasureAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} 发送停止血压测量指令（0x7D）");
            await SendCommandAsync(ProductProtocol.StopBpMeasure, "StopBpMeasure", ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 读取 ECG 导联状态（0x88）。
        /// ECG-002 导联脱落检测用。
        /// </summary>
        /// <returns>导联状态原始字节（具体位定义待实机联调确认）</returns>
        public async Task<byte[]> ReadLeadStateAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetLeadState, "ReadLeadState", ct: ct).ConfigureAwait(false);
            return resp?.Payload ?? Array.Empty<byte>();
        }

        /// <summary>
        /// 读取心率测量值（0x7C）。
        /// ECG-004 心率检测精度用。
        /// </summary>
        public async Task<double> ReadHeartRateAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetHrValue, "ReadHeartRate", ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 2)
            {
                _log.Warn("产品心率读取失败或 payload 不足 2 字节");
                return double.NaN;
            }

            var hr = ProductProtocol.ReadUInt16LE(resp.Payload, 0);
            _log.Info($"产品上报心率：{hr} BPM");
            return hr;
        }

        /// <summary>
        /// 开始/停止血氧测量（0x81，Func=0x05）。
        /// </summary>
        /// <param name="start">true=开始，false=停止</param>
        public async Task StartSpo2MeasureAsync(bool start = true, CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} {(start ? "开始" : "停止")}血氧测量（0x81）");
            await SendCommandAsync(ProductProtocol.StartSpo2, "StartSpo2Measure",
                new byte[] { (byte)(start ? 0x00 : 0x01) }, ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 读取血氧测量值（0x82，Func=0x05）。
        /// SPO2-001 血氧饱和度精度测试用。
        /// </summary>
        /// <returns>SpO2（%）；读取失败返回 NaN</returns>
        public async Task<double> ReadSpo2ValueAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetSpo2Value, "ReadSpo2Value", ct: ct).ConfigureAwait(false);
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
        /// 读取电池信息（0x6C，Func=0x07）：响应 payload 共 8 字节（协议 fac_bat_req）。
        ///   [0..3] 电压 uint32 LE（mV）  [4] 百分比（0-100%）  [5..7] 保留字节 rese[3]（不解析）
        /// </summary>
        public async Task<BatteryReading?> ReadBatteryInfoAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetBatteryInfo, "ReadBatteryInfo", ct: ct).ConfigureAwait(false);
            if (resp == null || resp.Payload.Length < 5)
            {
                _log.Warn($"产品电池信息读取失败或 payload 不足 5 字节（实际 {resp?.Payload.Length ?? 0}）");
                return null;
            }

            var payload = resp.Payload;
            var voltageMv = ProductProtocol.ReadUInt32LE(payload, 0);
            var percentage = payload[4];

            _log.Info($"产品上报电池信息：电压={voltageMv}mV 百分比={percentage}% Raw=[{BitConverter.ToString(payload)}]");
            return new BatteryReading(voltageMv, percentage, payload);
        }

        /// <summary>
        /// 读取充电状态（0x6D，Func=0x07）：返回充电状态字节（1=充电中，0=未充电）。
        /// </summary>
        public async Task<byte> ReadChargingStatusAsync(CancellationToken ct = default)
        {
            var resp = await SendCommandAsync(ProductProtocol.GetChargingStatus, "ReadChargingStatus", ct: ct).ConfigureAwait(false);
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
            _portGate.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
