using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using TORNPISolution.Interface;

namespace TORNPISolution.Service.Communication
{
    /// <summary>
    /// 文本协议 TCP 客户端（Raw Socket 上的 ASCII 帧），供 SCPI / AT / 读码器一类一问一答或主动上报的仪器复用。
    /// 帧边界由调用方指定：SCPI 与 AT 指令按 \r\n 成帧，读码器结果按 ';' 结束。
    /// 同一时刻只放一个调用方进去读（_readLock 串行化），一次网络读切出的多帧先入队留给下次调用，
    /// 粘包与半包都在 _partial 里消化，不会把上一条命令的残留串到下一条。
    /// 等数据只做一次非阻塞的 socket 探测再读，绝不取消在途的 ReadAsync：NetworkStream 的读被取消后流状态不可靠，
    /// 后续再读就会错位（表现为偶尔丢半行或读到上一条的回执）。
    /// </summary>
    public class TcpLineClient : IDisposable
    {
        /// <summary>帧尾（发送时自动补，接收时按它切帧）</summary>
        public const string NewLine = "\r\n";

        private const int ReadBufferSize = 1024;

        /// <summary>空等数据时的探测间隔：用 socket 非阻塞探一下，没数据就让出线程接着等</summary>
        private const int PollSliceMs = 20;

        private readonly string _ipAddress;
        private readonly int _port;
        private readonly int _connectTimeoutMs;
        private readonly ILogService _log;
        private readonly SemaphoreSlim _readLock = new(1, 1);
        private readonly ConcurrentQueue<string> _frames = new();
        private readonly StringBuilder _partial = new();
        private readonly object _partialLock = new();

        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private bool _disposed;

        public TcpLineClient(string ipAddress, int port, int connectTimeoutMs = 5000, ILogService? log = null)
        {
            _ipAddress = ipAddress;
            _port = port;
            _connectTimeoutMs = connectTimeoutMs;
            _log = log ?? new LogService(nameof(TcpLineClient));
        }

        /// <summary>远端标识（日志前缀用）</summary>
        public string Remote => $"{_ipAddress}:{_port}";

        /// <summary>TCP 连接是否已建立</summary>
        public bool IsOpen => _tcp is { Connected: true } && _stream != null;

        /// <summary>建立 TCP 连接（已连接直接返回）；连不上按超时抛 TimeoutException</summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            if (IsOpen)
            {
                return;
            }

            var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_connectTimeoutMs);
            try
            {
                await tcp.ConnectAsync(_ipAddress, _port, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                tcp.Dispose();
                throw new TimeoutException($"连接 {Remote} 超时（{_connectTimeoutMs}ms）");
            }
            catch
            {
                tcp.Dispose();
                throw;
            }

            _tcp = tcp;
            _stream = tcp.GetStream();
            ClearReceived();
            _log.Info($"已连接 {Remote}");
        }

        /// <summary>关闭连接（不抛：现场断开本来就可能已经断了）</summary>
        public Task CloseAsync() => Task.Run(() =>
        {
            var stream = _stream;
            var tcp = _tcp;
            _stream = null;
            _tcp = null;
            try { stream?.Dispose(); } catch { /* 断开时抛异常属正常 */ }
            try { tcp?.Dispose(); } catch { /* 同上 */ }
            ClearReceived();
            _log.Info($"已断开 {Remote}");
        });

        /// <summary>发送一段文本（默认补 \r\n 帧尾，文本自带就不重复补）</summary>
        public async Task SendAsync(string text, bool appendNewLine = true, CancellationToken ct = default)
        {
            var stream = _stream ?? throw new InvalidOperationException($"未连接 {Remote}，请先调用 ConnectAsync");
            var payload = appendNewLine && !text.EndsWith(NewLine, StringComparison.Ordinal) ? text + NewLine : text;
            await stream.WriteAsync(Encoding.ASCII.GetBytes(payload), ct).ConfigureAwait(false);
            _log.Debug($"→ {Remote} {text}");
        }

        /// <summary>读一帧：读到 terminator 为止，去帧尾并 Trim；等不到返回 null（不抛，由调用方判超时），半截内容留在缓冲区等下一段。</summary>
        public async Task<string?> ReadFrameAsync(string terminator = NewLine, int timeoutMs = 3000, CancellationToken ct = default)
        {
            var stream = _stream ?? throw new InvalidOperationException($"未连接 {Remote}，请先调用 ConnectAsync");

            await _readLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var deadline = Environment.TickCount64 + timeoutMs;
                while (true)
                {
                    // 先取上次多切出来的整帧，再考虑去网络上等
                    if (TryDequeueFrame(out var cached))
                    {
                        return cached;
                    }

                    var remainingMs = (int)(deadline - Environment.TickCount64);
                    if (remainingMs <= 0)
                    {
                        return null;
                    }

                    // 空等这段时间只探可读性，不伤在途的读；没等到就返回 null，半截内容留在缓冲区
                    if (!await WaitForDataAsync(remainingMs, ct).ConfigureAwait(false))
                    {
                        return null;
                    }

                    var buffer = new byte[ReadBufferSize];
                    var count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (count <= 0)
                    {
                        throw new IOException($"{Remote} 已被对端断开");
                    }

                    AppendAndSplit(Encoding.ASCII.GetString(buffer, 0, count), terminator);
                }
            }
            finally
            {
                _readLock.Release();
            }
        }

        /// <summary>
        /// 空等到有数据可读（或到点）：每轮用 Socket.Poll(0) 非阻塞探一下，没数据就 Task.Delay 让出线程。
        /// 对端断开时 Poll 也报可读，交后面的 ReadAsync 读出 0 字节来识别；连接已释放同样归成“已被对端断开”。
        /// </summary>
        private async Task<bool> WaitForDataAsync(int timeoutMs, CancellationToken ct)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (true)
            {
                try
                {
                    if (_tcp?.Client.Poll(0, SelectMode.SelectRead) == true)
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    throw new IOException($"{Remote} 已被对端断开", ex);
                }

                var remainingMs = (int)(deadline - Environment.TickCount64);
                if (remainingMs <= 0)
                {
                    return false;
                }

                await Task.Delay(Math.Min(PollSliceMs, remainingMs), ct).ConfigureAwait(false);
            }
        }

        /// <summary>丢掉收到的全部内容（含半截帧）：发触发命令前清一次，免得把上一次的残留当成本次结果</summary>
        public void ClearReceived()
        {
            while (_frames.TryDequeue(out _)) { }
            lock (_partialLock)
            {
                _partial.Clear();
            }
        }

        /// <summary>取走缓冲区里未成帧的半截文本（取完即清），用于对端发不带帧尾的短文本；调用方须确认本轮已发完，否则拿到断句。</summary>
        public string? TakePartial()
        {
            lock (_partialLock)
            {
                if (_partial.Length == 0)
                {
                    return null;
                }
                var text = _partial.ToString().Trim();
                _partial.Clear();
                return text.Length == 0 ? null : text;
            }
        }

        /// <summary>把新到的一段接进缓冲区，按帧尾切出所有完整帧入队，半截帧留在缓冲区等下一段</summary>
        private void AppendAndSplit(string received, string terminator)
        {
            lock (_partialLock)
            {
                _partial.Append(received);
                var text = _partial.ToString();
                var consumed = 0;

                while (true)
                {
                    var index = text.IndexOf(terminator, consumed, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        break;
                    }

                    var frame = text.Substring(consumed, index - consumed).Trim();
                    if (frame.Length > 0)
                    {
                        _frames.Enqueue(frame);
                    }
                    consumed = index + terminator.Length;
                }

                _partial.Clear();
                if (consumed < text.Length)
                {
                    _partial.Append(text, consumed, text.Length - consumed);
                }
            }
        }

        private bool TryDequeueFrame(out string frame)
        {
            var got = _frames.TryDequeue(out var cached);
            frame = cached ?? string.Empty;
            return got;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            try { _stream?.Dispose(); } catch { /* 释放阶段忽略 */ }
            try { _tcp?.Dispose(); } catch { /* 释放阶段忽略 */ }
            _stream = null;
            _tcp = null;
            _readLock.Dispose();
            ClearReceived();
            GC.SuppressFinalize(this);
        }
    }
}
