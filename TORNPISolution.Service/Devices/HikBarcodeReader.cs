using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 海康 MV-ID 读码器（智能读码相机）控制类。
    /// 相机侧开「TCP服务器端协议」当服务端，这里作客户端连两条连接：
    /// 触发端口（默认 2001）只发触发文本，结果端口（默认 2002）只收条码。
    /// 结果按 &lt;回车&gt;&lt;换行&gt;&lt;条码内容&gt;; 推送，帧边界是分号，所以读帧时把帧尾换成 ';'。
    /// 相机开了交互时，触发口会先要一轮请求/应答（hello / world），不应答它不会推码。
    /// 一次触发可能推来多条（视野里不止一个码），要几条就读几次；没读到码返回 null，不抛。
    /// </summary>
    public class HikBarcodeReader : IBarcodeReader
    {
        /// <summary>结果帧尾：读码格式里的结束符是分号，不是换行</summary>
        private const string ResultTerminator = ";";

        /// <summary>没读到时相机推的标志文本（相机侧「无码输出」默认写这个）</summary>
        private const string NoReadText = "NoRead";

        /// <summary>探一次交互请求时等多久：相机不玩交互就是这个等待时长</summary>
        private const int InteractionProbeMs = 300;

        /// <summary>一轮读取里最多让交互插几次队，防相机反复要应答把读取卡死</summary>
        private const int MaxInteractionRounds = 3;

        private readonly HikBarcodeReaderSettings _settings;
        private readonly ILogService _log;
        private readonly SemaphoreSlim _scanLock = new(1, 1);

        private TcpLineClient? _trigger;
        private TcpLineClient? _result;
        private bool _disposed;

        /// <summary>
        /// 这台相机到底要不要交互握手：建链时探一次定下来，不要就每次触发省一个探测等待；
        /// 真读到了请求会重新置回 true。
        /// </summary>
        private bool _interactionRequired = true;

        public HikBarcodeReader(HikBarcodeReaderSettings settings, ILogService? log = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _log = log ?? new LogService($"{nameof(HikBarcodeReader)}:{settings.Name}");
        }

        public string Name => _settings.Name;

        public bool IsOpen => _trigger is { IsOpen: true } && _result is { IsOpen: true };

        /// <summary>
        /// 先连结果端口再连触发端口：结果端口是收码的唯一出口，
        /// 它连不上时只连通触发端口会造成"触发得动、码一条都回不来"的假象。
        /// </summary>
        public async Task OpenAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsOpen)
            {
                return;
            }

            var result = new TcpLineClient(_settings.IpAddress, _settings.Port, _settings.ConnectTimeoutMs, _log);
            var trigger = new TcpLineClient(_settings.IpAddress, _settings.TriggerPort, _settings.ConnectTimeoutMs, _log);
            try
            {
                await result.ConnectAsync(ct).ConfigureAwait(false);
                await trigger.ConnectAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                await CloseClientsAsync(result, trigger).ConfigureAwait(false);
                throw;
            }

            _result = result;
            _trigger = trigger;
            // 建链时就探一次：交互请求可能是连接级握手，不等它就只能等满一个触发周期
            _interactionRequired = await HandshakeIfRequestedAsync(trigger, ct).ConfigureAwait(false);
            _log.Info($"{_settings.Name} 已连接：结果 {result.Remote}，触发 {trigger.Remote}");
        }

        public async Task CloseAsync()
        {
            var trigger = _trigger;
            var result = _result;
            _trigger = null;
            _result = null;
            await CloseClientsAsync(trigger, result).ConfigureAwait(false);
            _log.Info($"{_settings.Name} 两条连接已关闭");
        }

        /// <summary>
        /// 软触发一次并等结果：清掉结果端口上积压的旧码 → 发触发文本 → 应答交互请求 → 读一条结果。
        /// 整个动作串行，两个线程同时触发会导致收到的码对不上是哪一次触发。
        /// </summary>
        public async Task<string?> ScanAsync(int? timeoutMs = null, CancellationToken ct = default)
        {
            var trigger = _trigger ?? throw new InvalidOperationException($"{_settings.Name} 未连接，请先调用 OpenAsync");
            var budget = timeoutMs ?? _settings.ReadTimeoutMs;

            await _scanLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                DiscardPendingResults();
                await trigger.SendAsync(_settings.TriggerText, _settings.TriggerAppendNewLine, ct).ConfigureAwait(false);
                if (_interactionRequired)
                {
                    await HandshakeIfRequestedAsync(trigger, ct).ConfigureAwait(false);
                }
                var code = await ReadResultAsync(budget, ct).ConfigureAwait(false);
                if (code == null)
                {
                    _log.Warn($"{_settings.Name} 触发后 {budget}ms 内没读到码");
                }
                return code;
            }
            finally
            {
                _scanLock.Release();
            }
        }

        /// <summary>只等相机推来的下一条结果，不发触发文本（自由运行、IO 触发或取同一次触发的第二条码时用）</summary>
        public Task<string?> ReadAsync(int? timeoutMs = null, CancellationToken ct = default)
            => ReadResultAsync(timeoutMs ?? _settings.ReadTimeoutMs, ct);

        public void DiscardPendingResults() => _result?.ClearReceived();

        // ==================== 内部实现 ====================

        /// <summary>
        /// 从结果端口读一帧：帧尾按分号切，内容里混进的回车换行由 TcpLineClient 的 Trim 去掉。
        /// 等不到返回 null；相机推来说明没读到的标志文本也返回 null（没读到和没扫上是同一回事）。
        /// 交互请求也可能从结果口冒出来，就地应答完接着等下一条。
        /// </summary>
        private async Task<string?> ReadResultAsync(int timeoutMs, CancellationToken ct)
        {
            var result = _result ?? throw new InvalidOperationException($"{_settings.Name} 未连接，请先调用 OpenAsync");
            var deadline = Environment.TickCount64 + timeoutMs;

            for (var round = 0; ; round++)
            {
                var remainingMs = (int)(deadline - Environment.TickCount64);
                if (remainingMs <= 0)
                {
                    return null;
                }

                var frame = await result.ReadFrameAsync(ResultTerminator, remainingMs, ct).ConfigureAwait(false);
                if (frame == null)
                {
                    return null;
                }

                if (!IsInteractionRequest(frame))
                {
                    if (frame.Contains(NoReadText, StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Debug($"{_settings.Name} 相机报无读：{frame}");
                        return null;
                    }

                    _log.Info($"{_settings.Name} 读到条码：{frame}");
                    return frame;
                }

                if (round >= MaxInteractionRounds)
                {
                    _log.Warn($"{_settings.Name} 交互应答 {MaxInteractionRounds} 次还在要应答，停止本轮读取");
                    return null;
                }

                _interactionRequired = true;
                await result.SendAsync(_settings.InteractionReplyText, ct: ct).ConfigureAwait(false);
                _log.Debug($"{_settings.Name} 结果口收到交互请求，已应答 {_settings.InteractionReplyText}");
            }
        }

        /// <summary>
        /// 相机要交互应答时处理一轮：触发口会先冒出「交互请求文本」，回了「交互回复文本」它才抓拍。
        /// 请求可能带帧尾也可能不带，所以先按行读，读不到再从缓冲区取半截。
        /// 没配请求文本就当没交互（直接返回 false），不阻断正常触发流程。
        /// </summary>
        /// <returns>这一轮确实应答过交互则为 true</returns>
        private async Task<bool> HandshakeIfRequestedAsync(TcpLineClient trigger, CancellationToken ct)
        {
            var request = _settings.InteractionRequestText;
            if (string.IsNullOrEmpty(request))
            {
                return false;
            }

            var line = await trigger.ReadFrameAsync(TcpLineClient.NewLine, InteractionProbeMs, ct).ConfigureAwait(false)
                       ?? trigger.TakePartial();
            if (line == null)
            {
                return false;
            }

            if (!IsInteractionRequest(line))
            {
                _log.Debug($"{_settings.Name} 触发口收到预期外的文本：{line}");
                return false;
            }

            await trigger.SendAsync(_settings.InteractionReplyText, _settings.TriggerAppendNewLine, ct).ConfigureAwait(false);
            _log.Debug($"{_settings.Name} 已应答相机交互请求");
            return true;
        }

        /// <summary>整帧就是交互请求文本才算（去掉尾部标点与空白后比），免得把含这几个字的条码误判成握手</summary>
        private bool IsInteractionRequest(string frame)
        {
            var request = _settings.InteractionRequestText;
            return !string.IsNullOrEmpty(request)
                   && !string.IsNullOrEmpty(_settings.InteractionReplyText)
                   && frame.TrimEnd('!', '.', ' ', '\r', '\n')
                          .Equals(request, StringComparison.OrdinalIgnoreCase);
        }

        private static async Task CloseClientsAsync(params TcpLineClient?[] clients)
        {
            foreach (var client in clients)
            {
                if (client == null)
                {
                    continue;
                }
                await client.CloseAsync().ConfigureAwait(false);
                client.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            try
            {
                // 释放阶段只能同步等，给两条连接一点时间体面断开
                CloseClientsAsync(_trigger, _result).Wait(TimeSpan.FromSeconds(2));
            }
            catch { /* 释放阶段忽略 */ }

            _trigger = null;
            _result = null;
            _scanLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
