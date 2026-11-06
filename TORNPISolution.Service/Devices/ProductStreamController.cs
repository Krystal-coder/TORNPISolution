using System.IO.Ports;
using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 产品（DUT）串口的常驻收发通道：一条读线程持续拆帧，应答帧按控制码喂给等待中的命令，0x60 采样帧另发事件。
    /// 与整机测试用的 ProductController 各持一个串口句柄——同一个 COM 口只能有一个持有者，
    /// 故整机方案与这一路不得同时连接（互斥在界面上挡，不改已调好的收发链路）。
    /// </summary>
    public sealed class ProductStreamController : IDisposable
    {
        private const int ReadChunkBytes = 4096;

        /// <summary>帧间空闲的轮询步长（ms）：取消标志靠它周期性回到循环顶部</summary>
        private const int IdlePollMs = 20;

        /// <summary>读线程退场的宽限：轮询循环最多睡一个 IdlePollMs 就能看见取消标志</summary>
        private const int ReadExitGraceMs = 300;

        /// <summary>关串口的限时：超时就交给后台线程慢慢收，不能把退出链钉在这一下</summary>
        private const int CloseTimeoutMs = 1200;

        private readonly SerialPort _port;
        private readonly ILogService _log;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly object _waiterLock = new();

        /// <summary>控制码 → 等待该应答的挂起点（同一控制码可能排着多个等待者）</summary>
        private readonly Dictionary<byte, List<TaskCompletionSource<ProductProtocol.ParsedFrame>>> _waiters = new();

        private CancellationTokenSource? _readCts;
        private Task? _readTask;

        /// <summary>无人等待的帧数（应答迟到、未订阅期间的推送）：逐帧只记 Debug，总量在关闭时给一行</summary>
        private long _unclaimedFrames;

        public ProductStreamController(SerialLinkSettings settings, ILogService? log = null)
        {
            _log = log ?? new LogService(nameof(ProductStreamController));
            var parity = settings.Parity switch
            {
                "Odd" => Parity.Odd,
                "Even" => Parity.Even,
                _ => Parity.None
            };
            var stopBits = settings.StopBits switch
            {
                2 => StopBits.Two,
                _ => StopBits.One
            };

            // 读超时只作兜底：读线程按 BytesToRead 轮询、不靠它回到循环顶部，凑不齐字节时才会碰到
            _port = new SerialPort(settings.PortName, settings.BaudRate, parity, settings.DataBits, stopBits)
            {
                ReadTimeout = 200,
                WriteTimeout = 2000,
                ReadBufferSize = 65536,
                DtrEnable = true,
                RtsEnable = true
            };
        }

        /// <summary>0x60 采样帧解析结果（在读线程上触发，订阅方只许入队，不得直接碰界面）</summary>
        public event Action<AccelSample>? AccelSampleReceived;

        /// <summary>0x8D 心电波形推送帧原帧（在读线程上触发，订阅方只许入队，不得直接碰界面）；解析成点由订阅方按自己的帧式做</summary>
        public event Action<ProductProtocol.ParsedFrame>? EcgWaveFrameReceived;

        public bool IsOpen { get; private set; }

        // ==================== 开关 ====================

        public void Open()
        {
            if (IsOpen) return;

            SerialPortOpener.Open(_port);   // 限时打开：Open() 无超时，端口异常时会无限期挂起
            _port.DiscardInBuffer();
            IsOpen = true;

            var cts = new CancellationTokenSource();
            _readCts = cts;
            _readTask = Task.Run(() => ReadLoop(cts.Token));
            _log.Info($"产品串口 {_port.PortName} 已打开（常驻收流，{_port.BaudRate},{_port.DataBits}{_port.Parity.ToString()[0]}{_port.StopBits}）");
        }

        public void Close()
        {
            if (!IsOpen && _readTask == null) return;
            IsOpen = false;

            try { _readCts?.Cancel(); } catch { /* 已在释放 */ }
            try { _readTask?.Wait(ReadExitGraceMs); } catch { /* 读线程的异常已在循环里记过账 */ }
            _readTask = null;

            ClosePortBounded();

            CancelAllWaiters();
            _log.Info($"产品串口 {_port.PortName} 已关闭（无人等待的帧 {_unclaimedFrames}）");
        }

        /// <summary>
        /// 限时关串口：SerialPort.Close() 要等驱动侧事件循环收尾，本身没有超时可言，
        /// 只能照 SerialPortOpener 打开那次的办法另开后台线程去关，超时就把句柄留给它慢慢回收。
        /// </summary>
        private void ClosePortBounded()
        {
            var port = _port;
            var done = new ManualResetEventSlim(false);
            new Thread(() =>
            {
                try
                {
                    if (port.IsOpen) port.Close();
                }
                catch (Exception ex)
                {
                    _log.Warn($"产品串口 {port.PortName} 关闭异常（忽略）：{ex.Message}");
                }
                finally { done.Set(); }
            })
            { IsBackground = true, Name = $"SerialClose-{port.PortName}" }.Start();

            if (!done.Wait(CloseTimeoutMs))
                _log.Warn($"产品串口 {port.PortName} 关闭超时（{CloseTimeoutMs}ms），句柄留在后台线程回收");
        }

        public void Dispose() => Close();

        // ==================== 收发 ====================

        /// <summary>发一帧并等同一控制码的应答；超时返回 null（不抛，调用方按"无应答"处理）</summary>
        public async Task<ProductProtocol.ParsedFrame?> RequestAsync(
            ProductCmd cmd, string commandName, byte[]? data = null,
            int timeoutMs = 3000, CancellationToken ct = default)
        {
            var packet = ProductProtocol.BuildPacket(cmd, data);
            var tcs = new TaskCompletionSource<ProductProtocol.ParsedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_waiterLock)
            {
                if (!_waiters.TryGetValue(cmd.Code, out var queue))
                    _waiters[cmd.Code] = queue = new List<TaskCompletionSource<ProductProtocol.ParsedFrame>>();
                queue.Add(tcs);
            }

            try
            {
                await WriteAsync(packet, $"TX [{commandName}({cmd})]", ct).ConfigureAwait(false);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
                if (completed != tcs.Task)
                {
                    _log.Warn($"产品 TX [{commandName}({cmd})]：已发出，{timeoutMs}ms 内没等到应答");
                    return null;
                }

                var frame = await tcs.Task.ConfigureAwait(false);
                _log.Info($"产品 RX [{commandName}({cmd})]: Func=0x{frame.FunctionCode:X2} Ctrl=0x{frame.ControlCode:X2} Data=[{BitConverter.ToString(frame.Payload)}]");
                return frame;
            }
            catch (OperationCanceledException)
            {
                _log.Warn($"产品 TX [{commandName}({cmd})]：等待应答被取消");
                return null;
            }
            finally
            {
                lock (_waiterLock)
                {
                    if (_waiters.TryGetValue(cmd.Code, out var queue))
                    {
                        queue.Remove(tcs);
                        if (queue.Count == 0) _waiters.Remove(cmd.Code);
                    }
                }
            }
        }

        /// <summary>只发不等（建立时的清场这类）：产品回不回、有没有人等都不影响发送</summary>
        public Task SendAsync(ProductCmd cmd, string commandName, byte[]? data = null)
            => WriteAsync(ProductProtocol.BuildPacket(cmd, data), $"TX [{commandName}({cmd})]");

        private async Task WriteAsync(byte[] packet, string logTag, CancellationToken ct = default)
        {
            if (!IsOpen)
                throw new InvalidOperationException($"产品串口 {_port.PortName} 未打开");

            // 单次写有 WriteTimeout 兜底，但前面有人占着闸门时不接取消就永远排不到：停止键要能打断排队
            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _port.Write(packet, 0, packet.Length);
                _log.Debug($"产品 {logTag}: {BitConverter.ToString(packet)}");
            }
            finally
            {
                _writeGate.Release();
            }
        }

        // ==================== 读线程 ====================

        private void ReadLoop(CancellationToken ct)
        {
            var chunk = new byte[ReadChunkBytes];
            var buffer = new List<byte>(ReadChunkBytes);

            while (!ct.IsCancellationRequested)
            {
                int read;
                try
                {
                    // 有字节才读：产品静默时阻塞 Read 会每 200ms 抛一次 TimeoutException，
                    // 挂调试器时每条 first-chance 异常都要全进程挂起通知，界面就是被这个拖死的（与康伟达读循环同一口径）
                    var available = _port.BytesToRead;
                    if (available == 0)
                    {
                        Thread.Sleep(IdlePollMs);
                        continue;
                    }

                    read = _port.Read(chunk, 0, Math.Min(chunk.Length, available));
                }
                catch (TimeoutException) { continue; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        _log.Warn($"产品串口读取中断：{ex.GetType().Name} {ex.Message}");
                    break;
                }

                if (read <= 0) continue;

                for (var i = 0; i < read; i++) buffer.Add(chunk[i]);

                // 拆帧与订阅方回调都在 try 内：漏出去一条异常会把读线程静默带走（Task 的异常没人观察就不响），
                // 表现是"报文不刷新了"且日志里一行痕迹都没有。出错时丢掉本批未凑齐的字节，下一帧从 0xEF 重新对齐。
                try
                {
                    var frame = TryExtractFrame(buffer);
                    while (frame != null)
                    {
                        HandleFrame(frame);
                        frame = TryExtractFrame(buffer);
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"产品帧处理异常，本批 {read} 字节丢弃：{ex.Message}", ex);
                    buffer.Clear();
                }

                if (buffer.Count > 32 * 1024)
                {
                    _log.Warn($"接收缓冲 {buffer.Count} 字节仍凑不出完整帧，丢弃");
                    buffer.Clear();
                }
            }
        }

        private void HandleFrame(ProductProtocol.ParsedFrame frame)
        {
            var handedToSubscriber = false;
            if (frame.ControlCode == ProductProtocol.StartAccelSensor.Code
                && frame.Payload.Length >= ProductProtocol.AccelSamplePayloadBytes)
            {
                var sample = ProductProtocol.ParseAccelSample(frame.Payload, DateTime.Now);
                if (sample != null)
                {
                    AccelSampleReceived?.Invoke(sample);
                    handedToSubscriber = true;
                }
            }
            else if (frame.ControlCode == ProductProtocol.PushEcgWave.Code
                     && frame.FunctionCode == ProductProtocol.PushEcgWave.Func)
            {
                EcgWaveFrameReceived?.Invoke(frame);
                handedToSubscriber = true;
            }

            // 采样帧也照常喂等待者：点"启动传感器"后先到的可能直接是采样帧，那正是传感器起来了的证据
            // 已经上抛给采集侧的帧不能算丢：它照样进了表和 CSV，报成"丢弃"会把排障指向反方向
            if (!CompleteWaiter(frame) && !handedToSubscriber)
            {
                Interlocked.Increment(ref _unclaimedFrames);
                _log.Debug($"产品 RX：Ctrl=0x{frame.ControlCode:X2} 无人等待，丢弃（Func=0x{frame.FunctionCode:X2}，参数 [{BitConverter.ToString(frame.Payload)}]）");
            }
        }

        private bool CompleteWaiter(ProductProtocol.ParsedFrame frame)
        {
            List<TaskCompletionSource<ProductProtocol.ParsedFrame>>? targets = null;
            lock (_waiterLock)
            {
                if (_waiters.TryGetValue(frame.ControlCode, out var queue) && queue.Count > 0)
                {
                    targets = new List<TaskCompletionSource<ProductProtocol.ParsedFrame>>(queue);
                    queue.Clear();
                }
            }

            if (targets == null) return false;

            foreach (var tcs in targets) tcs.TrySetResult(frame);
            return true;
        }

        private void CancelAllWaiters()
        {
            List<TaskCompletionSource<ProductProtocol.ParsedFrame>>? pending = null;
            lock (_waiterLock)
            {
                if (_waiters.Count > 0)
                {
                    pending = _waiters.Values.SelectMany(q => q).ToList();
                    _waiters.Clear();
                }
            }

            if (pending == null) return;
            foreach (var tcs in pending) tcs.TrySetCanceled();
        }

        /// <summary>
        /// 从缓冲区取出下一个完整且 CRC 通过的帧并就地消费字节：帧头 0xEF 定位、Len 定长、帧尾 0xFE 与 CRC 复核。
        /// 帧还不完整时不前进字节（等下一批）；伪帧头只丢 1 字节；CRC 失败帧整帧一次性消费后继续找下一帧。
        /// </summary>
        /// <returns>解析成功的帧；暂无完整帧返回 null</returns>
        private ProductProtocol.ParsedFrame? TryExtractFrame(List<byte> buffer)
        {
            while (true)
            {
                var headPos = buffer.IndexOf(ProductProtocol.Head);
                if (headPos < 0)
                {
                    buffer.Clear();
                    return null;
                }
                if (headPos > 0) buffer.RemoveRange(0, headPos);

                if (buffer.Count < 2) return null;

                var len = buffer[1];
                // Len 至少含 Func + Ctrl；小于 2 是伪帧头，丢 1 字节继续找
                if (len < 2)
                {
                    buffer.RemoveAt(0);
                    continue;
                }

                var frameEnd = len + 4;   // 帧尾下标 = Head(0) + Len(1) + Payload(len) + CRC(2)
                if (frameEnd >= buffer.Count) return null;

                if (buffer[frameEnd] != ProductProtocol.Tail)
                {
                    buffer.RemoveAt(0);
                    continue;
                }

                var frameBytes = buffer.Take(frameEnd + 1).ToArray();
                buffer.RemoveRange(0, frameEnd + 1);

                var parsed = ProductProtocol.ParseFrame(frameBytes);
                if (parsed != null) return parsed;

                _log.Warn($"产品 RX：帧尾匹配但 CRC 校验失败，丢弃整帧 {BitConverter.ToString(frameBytes)}");
            }
        }
    }
}
