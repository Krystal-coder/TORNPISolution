using System.Net.Sockets;
using System.Text;
using TORNPISolution.Interface;

namespace TORNPISolution.Service.Communication
{
    /// <summary>
    /// SCPI Raw Socket 客户端（LAN 网口，默认端口 5025）。
    /// 命令以 \n 结尾，响应以 \n 终止。
    /// 所有请求经 SemaphoreSlim 串行化（SCPI 一问一答）
    /// </summary>
    public class ScpiTcpClient : IDisposable
    {
        private readonly string _ipAddress;
        private readonly int _port;
        private readonly ILogService _log;
        private readonly int _timeoutMs;
        private readonly SemaphoreSlim _busLock = new(1, 1);

        private TcpClient? _tcp;
        private NetworkStream? _stream;

        public ScpiTcpClient(
            string ipAddress,
            int port = 5025,
            int timeoutMs = 3000,
            ILogService? log = null)
        {
            _ipAddress = ipAddress;
            _port = port;
            _timeoutMs = timeoutMs;
            _log = log ?? new LogService(nameof(ScpiTcpClient));
        }

        /// <summary>TCP 连接是否已建立</summary>
        public bool IsOpen => _tcp is { Connected: true } && _stream != null;

        /// <summary>建立 TCP 连接</summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            if (IsOpen)
            {
                return;
            }

            _tcp = new TcpClient();
            await _tcp.ConnectAsync(_ipAddress, _port, ct).ConfigureAwait(false);
            _stream = _tcp.GetStream();
            _log.Info($"已连接 {_ipAddress}:{_port}");
        }

        /// <summary>关闭 TCP 连接</summary>
        public Task CloseAsync() => Task.Run(() =>
        {
            _stream?.Dispose();
            _tcp?.Dispose();
            _stream = null;
            _tcp = null;
            _log.Info($"已断开 {_ipAddress}:{_port}");
        });

        /// <summary>发送命令（无响应，自动追加 \n 结尾）</summary>
        public async Task WriteAsync(string command, CancellationToken ct = default)
        {
            await _busLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteCoreAsync(command, ct).ConfigureAwait(false);
            }
            finally
            {
                _busLock.Release();
            }
        }

        /// <summary>发送查询命令并读取一行响应（去掉结尾换行）</summary>
        public async Task<string> QueryAsync(string command, CancellationToken ct = default)
        {
            await _busLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteCoreAsync(command, ct).ConfigureAwait(false);
                var response = await ReadLineAsync(ct).ConfigureAwait(false);
                _log.Debug($"SCPI {command} -> {response}");
                return response;
            }
            finally
            {
                _busLock.Release();
            }
        }

        private async Task WriteCoreAsync(string command, CancellationToken ct)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException($"未连接 {_ipAddress}:{_port}，请先调用 ConnectAsync");
            }

            var data = Encoding.ASCII.GetBytes(command.TrimEnd('\n', '\r') + "\n");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeoutMs);

            try
            {
                await _stream.WriteAsync(data, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"SCPI 发送超时（{_timeoutMs}ms）：{command}");
            }

            _log.Debug($"SCPI 发送：{command}");
        }

        /// <summary>读取一行响应（遇 \n 结束，超时抛 TimeoutException）</summary>
        private async Task<string> ReadLineAsync(CancellationToken ct)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException($"未连接 {_ipAddress}:{_port}，请先调用 ConnectAsync");
            }

            var buffer = new byte[256];
            var sb = new StringBuilder();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeoutMs);

            try
            {
                while (true)
                {
                    var count = await _stream.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
                    if (count <= 0)
                    {
                        throw new IOException($"仪器 {_ipAddress}:{_port} 已断开连接");
                    }

                    for (var i = 0; i < count; i++)
                    {
                        var c = (char)buffer[i];
                        if (c == '\n')
                        {
                            return sb.ToString().TrimEnd('\r');
                        }
                        sb.Append(c);
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"SCPI 响应超时（{_timeoutMs}ms）：{sb}");
            }
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _tcp?.Dispose();
            _busLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
