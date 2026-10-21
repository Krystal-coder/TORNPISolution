using System.IO.Ports;
using TORNPISolution.Interface;

namespace TORNPISolution.Service.Communication
{
    /// <summary>
    /// 通用串口客户端：文本（AT 指令）、二进制（固定帧）、自行分帧（设备主动连发）三种通讯模式。
    /// 自行分帧不持 _lock：ReadRaw 轮询期间还要 WriteRaw，加锁会必然等满超时。
    /// 接第二个用户前先确认是否会并发写同一端口：一端口一实例或补锁。
    /// </summary>
    public class SerialClient : IDisposable
    {
        private readonly SerialPort _port;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly ILogService _log;
        private readonly int _defaultTimeoutMs;

        public SerialClient(
            string portName,
            int baudRate = 9600,
            Parity parity = Parity.None,
            int dataBits = 8,
            StopBits stopBits = StopBits.One,
            int responseTimeoutMs = 2000,
            ILogService? log = null)
        {
            _defaultTimeoutMs = responseTimeoutMs;
            _log = log ?? new LogService(nameof(SerialClient));
            _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = responseTimeoutMs,
                WriteTimeout = responseTimeoutMs,
                NewLine = "\r\n"
            };
        }

        /// <summary>串口是否已打开</summary>
        public bool IsOpen => _port.IsOpen;

        /// <summary>串口号</summary>
        public string PortName => _port.PortName;

        /// <summary>打开串口</summary>
        public Task OpenAsync(CancellationToken ct = default) => Task.Run(() =>
        {
            if (!_port.IsOpen)
            {
                SerialPortOpener.Open(_port);   // 限时打开： Open() 无超时，端口异常时会无限期挂起
                _log.Info($"串口 {_port.PortName} 已打开（{_port.BaudRate},{_port.Parity},{_port.DataBits},{_port.StopBits}）");
            }
        }, ct);

        /// <summary>关闭串口</summary>
        public Task CloseAsync() => Task.Run(() =>
        {
            if (_port.IsOpen)
            {
                _port.Close();
                _log.Info($"串口 {_port.PortName} 已关闭");
            }
        });

        // ==================== 文本协议（AT 指令等） ====================

        /// <summary>发送文本指令（自动追加 \r\n），等待多行响应直到超时。</summary>
        /// <param name="command">指令文本（不含 \r\n）</param>
        public async Task<string> SendTextAsync(string command, int timeoutMs = 0, CancellationToken ct = default)
        {
            var timeout = timeoutMs > 0 ? timeoutMs : _defaultTimeoutMs;
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    if (!_port.IsOpen)
                        throw new InvalidOperationException($"串口 {_port.PortName} 未打开");

                    _port.DiscardInBuffer();
                    _port.WriteLine(command);
                    _log.Debug($"TX: {command}");

                    var sb = new System.Text.StringBuilder();
                    var deadline = Environment.TickCount64 + timeout;

                    while (Environment.TickCount64 < deadline)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (_port.BytesToRead > 0)
                        {
                            var line = _port.ReadLine();
                            sb.AppendLine(line);
                            _log.Debug($"RX: {line}");
                            // 收到空行或超短行视为响应结束
                            if (string.IsNullOrWhiteSpace(line) && sb.Length > 0)
                                break;
                        }
                        else
                        {
                            Thread.Sleep(5);
                        }
                    }

                    return sb.ToString().TrimEnd();
                }, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _log.Warn($"串口 {_port.PortName} 文本响应超时（{timeout}ms）：{command}");
                throw;
            }
            finally
            {
                _lock.Release();
            }
        }

        // ==================== 二进制协议（固定帧） ====================

        /// <summary>发送固定长度字节帧并接收指定长度的响应帧（帧格式由调用方决定，本方法不解析内容）。</summary>
        /// <param name="expectedLength">期望响应字节数（0 表示不等待响应）</param>
        public async Task<byte[]> SendFrameAsync(byte[] frame, int expectedLength = 0, int timeoutMs = 0, CancellationToken ct = default)
        {
            var timeout = timeoutMs > 0 ? timeoutMs : _defaultTimeoutMs;
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    if (!_port.IsOpen)
                        throw new InvalidOperationException($"串口 {_port.PortName} 未打开");

                    _port.DiscardInBuffer();
                    _port.Write(frame, 0, frame.Length);
                    _log.Debug($"TX Frame [{frame.Length}]: {BitConverter.ToString(frame)}");

                    if (expectedLength <= 0)
                        return Array.Empty<byte>();

                    var buffer = new byte[expectedLength];
                    var received = 0;
                    var deadline = Environment.TickCount64 + timeout;

                    while (received < expectedLength)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (Environment.TickCount64 > deadline)
                            throw new TimeoutException($"二进制帧响应超时（已收 {received}/{expectedLength} 字节）");

                        if (_port.BytesToRead > 0)
                        {
                            received += _port.Read(buffer, received, expectedLength - received);
                        }
                        else
                        {
                            Thread.Sleep(2);
                        }
                    }

                    _log.Debug($"RX Frame [{received}]: {BitConverter.ToString(buffer)}");
                    return buffer;
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>持续监听串口，等待设备主动发送的数据（如心跳询问帧）。</summary>
        /// <returns>收到的字节，超时返回 null</returns>
        public async Task<byte[]?> ListenAsync(int timeoutMs = 5000, CancellationToken ct = default)
        {
            try
            {
                return await Task.Run(() =>
                {
                    if (!_port.IsOpen)
                        return null;

                    var deadline = Environment.TickCount64 + timeoutMs;
                    var buffer = new List<byte>();

                    while (Environment.TickCount64 < deadline)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (_port.BytesToRead > 0)
                        {
                            var chunk = new byte[_port.BytesToRead];
                            var count = _port.Read(chunk, 0, chunk.Length);
                            buffer.AddRange(chunk.Take(count));
                            // 收到至少 1 字节后稍等让帧完整到达
                            Thread.Sleep(50);
                            break;
                        }
                        Thread.Sleep(10);
                    }

                    return buffer.Count > 0 ? buffer.ToArray() : null;
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        // ==================== 自行分帧的协议（设备主动连发、帧长不固定） ====================

        /// <summary>
        /// 直接写字节、不等应答，用于设备连续主动上报的协议。
        /// 不持锁也不清接收缓冲，调用方保证串行；残留需显式 <see cref="DiscardInBuffer"/> 丢弃。
        /// </summary>
        public void WriteRaw(byte[] data, CancellationToken ct = default)
        {
            if (!_port.IsOpen)
                throw new InvalidOperationException($"串口 {_port.PortName} 未打开");

            ct.ThrowIfCancellationRequested();
            _port.Write(data, 0, data.Length);
            _log.Debug($"TX Raw [{data.Length}]: {BitConverter.ToString(data)}");
        }

        /// <summary>读一次可用字节：有数据立即返回（不保证一整帧，由调用方拼帧），窗口内无字节返回空数组。</summary>
        public byte[] ReadRaw(int waitMs, CancellationToken ct = default)
        {
            if (!_port.IsOpen)
                throw new InvalidOperationException($"串口 {_port.PortName} 未打开");

            var deadline = Environment.TickCount64 + waitMs;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var available = _port.BytesToRead;
                if (available > 0)
                {
                    var buffer = new byte[available];
                    var read = _port.Read(buffer, 0, available);
                    if (read == available) return buffer;

                    var trimmed = new byte[read];
                    Array.Copy(buffer, trimmed, read);
                    return trimmed;
                }

                if (Environment.TickCount64 >= deadline) return Array.Empty<byte>();
                Thread.Sleep(5);
            }
        }

        /// <summary>丢掉接收缓冲里的残留（上一轮没读完的状态帧，不清会被当成本轮的开头）</summary>
        public void DiscardInBuffer()
        {
            if (_port.IsOpen) _port.DiscardInBuffer();
        }

        public void Dispose()
        {
            _lock.Dispose();
            if (_port.IsOpen)
            {
                _port.Close();
            }
            _port.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
