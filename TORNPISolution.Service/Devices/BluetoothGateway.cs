using System.Collections.Concurrent;
using System.Globalization;
using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 研为 EW-DTU09 蓝牙网关控制类（网口 TCP 上的 AT 文本指令）。
    /// 命令以 \r\n 结束，响应若干行数据后以 OK / ERROR 收尾；扫描结果不等查询，
    /// 由网关以 +SC_NTF: 主动冒出来，所以建链后固定有一条后台读取泵在读。
    /// 上报本身也按 AT 响应格式打包：每条 +SC_NTF 后面都还跟一行 OK。
    /// 读取泵独占接收，收到的每一行按前缀分流：上报前缀进扫描收集器，其余行喂给当前挂起的那条命令。
    /// 命令串行下发（_commandLock），保证一行 OK/ERROR 只对应它自己那条命令。
    /// </summary>
    public class BluetoothGateway : IBluetoothGateway
    {
        /// <summary>扫描上报前缀（mac,mac_id,mac_type,data_type,rssi,tx_power,ch_index,scan_response,data_len,data）</summary>
        private const string ScanPrefix = "+SC_NTF:";

        /// <summary>
        /// 主动上报的前缀：这些行不属于任何命令的回执，绝不能当成 OK/ERROR 之外的响应喂给挂起命令。
        /// </summary>
        private static readonly string[] UnsolicitedPrefixes =
        {
            ScanPrefix,     // 扫描通知
            "+NOTIFY:",     // 从机通知
            "+INDICATE:",   // 从机指示
            "+DATA:",       // 从机数据
            "+DISCON:",     // 断开
            "+SERV:",       // 服务发现
            "+CHAR:",       // 特征发现
        };

        /// <summary>工作模式问句的回执前缀：后面跟的字段数不固定，只取逗号前的第一个才是模式值</summary>
        private const string ModePrefix = "+MODE:";

        /// <summary>开扫描：间隔 6、窗口 4（单位都是 625us）、超时 0 表示持续扫描、要回应包、数据段按 ASCII 解</summary>
        private const string StartScanCommand = "AT+SCAN=1,6,4,0,1,0";

        /// <summary>停扫描</summary>
        private const string StopScanCommand = "AT+SCAN=0";

        /// <summary>
        /// MAC 过滤占用哪个过滤槽：手册给的 MAC 过滤写法是 AT+FLT=14,3,9,{mac}，
        /// 类型位 3 表示按地址过滤（支持只填地址前几个字节），标识符位按手册填 9。
        /// </summary>
        private const string MacFilterFormat = "AT+FLT=14,3,9,{0}";

        /// <summary>关过滤：类型位填 255（不过滤），数据段是一个占位地址的 HEX 串（厂商给的固定写法）</summary>
        private const string DisableFilterCommand = "AT+FLT=14,255,9,393445433332304543353743";

        /// <summary>过滤参数下发到扫描启动之间留一点生效时间</summary>
        private const int FilterSettleDelayMs = 500;

        /// <summary>
        /// 停扫描后的静置时间：上报每一行后面都自带一行 OK，停的那一下里队列可能还压着几条，
        /// 先等它们落完再发下一条指令，免得把上报带的 OK 当成下一条指令的回执消费掉。
        /// </summary>
        private const int StopSettleDelayMs = 300;

        /// <summary>复位后等上电信息的时长：模块要重启，比寻常指令慢得多</summary>
        private const int RebootWaitMs = 8000;

        /// <summary>等上电信息时的探测间隔</summary>
        private const int BootProbeIntervalMs = 100;

        /// <summary>上电信息块的第一行就是这三个字母（型号那行各批不一样，不拿它做判据）</summary>
        private const string BootBannerMarker = "ATI";

        /// <summary>复位用的指令（AT+REST=1）</summary>
        private const string RestartCommand = "AT+REST=1";

        /// <summary>
        /// 操作指令里的复位：AT+OP=0（1 是恢复出厂、2 是测试模式）。模块重启后重播上电信息，
        /// 清的是运行状态不动配置，所以先前下发的过滤参数重启后仍然有效。
        /// </summary>
        private const string ResetOperationCommand = "AT+OP=0";

        /// <summary>复位后重连的尝试间隔</summary>
        private const int ReconnectProbeIntervalMs = 200;

        /// <summary>读取泵每轮等一行的时间；没数据就空转一轮，顺便看有没有让停</summary>
        private const int PumpIdleSliceMs = 500;

        private readonly BluetoothGatewaySettings _settings;
        private readonly ILogService _log;
        private readonly SemaphoreSlim _commandLock = new(1, 1);

        private TcpLineClient? _client;
        private ReplyBox? _pending;
        private ScanCollector? _collector;
        private CancellationTokenSource? _pumpCts;
        private Task? _pumpTask;
        private bool _disposed;

        public BluetoothGateway(BluetoothGatewaySettings settings, ILogService? log = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _log = log ?? new LogService($"{nameof(BluetoothGateway)}:{settings.Name}");
        }

        public string Name => _settings.Name;

        public bool IsOpen => _client is { IsOpen: true };

        /// <summary>
        /// 建链 → 起读取泵 → 发 AT 确认模块在 AT 模式下应答。
        /// AT 不应答后面每条指令都只会等超时，所以这里直接失败，现场看日志就知道去切模式。
        /// </summary>
        public async Task OpenAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsOpen)
            {
                return;
            }

            var client = new TcpLineClient(_settings.IpAddress, _settings.Port, _settings.ConnectTimeoutMs, _log);
            try
            {
                await client.ConnectAsync(ct).ConfigureAwait(false);
                _client = client;
                StartPump();

                var reply = await TransceiveAsync("AT", ct).ConfigureAwait(false);
                if (!reply.Success)
                {
                    throw new InvalidOperationException($"{_settings.Name}（{client.Remote}）不响应 AT：网关侧要把「数据协议」选成 AT 指令，工作模式也不能停在数据透传");
                }
                _log.Info($"{_settings.Name} 已连接 {client.Remote}");
            }
            catch
            {
                await CloseInternalAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task CloseAsync()
        {
            await CloseInternalAsync().ConfigureAwait(false);
            _log.Info($"{_settings.Name} 连接已关闭");
        }

        /// <summary>
        /// 握手测试。已建链就直接发 AT 走现有会话；没建链则单独探一条连接（探完就断），
        /// 免得占用网关上唯一的 AT 通道去影响正在跑的会话。
        /// </summary>
        public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            if (IsOpen)
            {
                try
                {
                    return (await TransceiveAsync("AT", ct).ConfigureAwait(false)).Success;
                }
                catch (Exception ex)
                {
                    _log.Warn($"{_settings.Name} 握手失败：{ex.Message}");
                    return false;
                }
            }

            var probe = new TcpLineClient(_settings.IpAddress, _settings.Port, _settings.ConnectTimeoutMs, _log);
            try
            {
                await probe.ConnectAsync(ct).ConfigureAwait(false);
                await probe.SendAsync("AT", ct: ct).ConfigureAwait(false);
                var reply = await probe.ReadFrameAsync(TcpLineClient.NewLine, _settings.ResponseTimeoutMs, ct)
                              .ConfigureAwait(false);
                var ok = reply != null && reply.Contains("OK", StringComparison.OrdinalIgnoreCase);
                _log.Info($"{_settings.Name} 握手{(ok ? "成功" : $"失败：{reply ?? "无响应"}")}");
                return ok;
            }
            catch (Exception ex)
            {
                _log.Warn($"{_settings.Name} 握手异常：{ex.Message}");
                return false;
            }
            finally
            {
                await probe.CloseAsync().ConfigureAwait(false);
                probe.Dispose();
            }
        }

        /// <summary>读网关自身信息（ATI）：版本、型号、MAC 等若干行，原样拼成一段文本返回</summary>
        public async Task<string> GetIdentityAsync(CancellationToken ct = default)
        {
            var reply = await TransceiveAsync("ATI", ct).ConfigureAwait(false);
            return string.Join(" | ", reply.Lines);
        }

        /// <summary>
        /// 读当前工作模式（AT+MODE?）：0=AT 指令模式，1=数据透传模式。
        /// 回执字段数不固定：简格式只给一个模式值，透传设置那页会把角色、透传 UUID 等一并回上来，
        /// 所以只取第一个逗号前的那一段。
        /// </summary>
        public async Task<int> GetModeAsync(CancellationToken ct = default)
        {
            var reply = await TransceiveAsync("AT+MODE?", ct).ConfigureAwait(false);
            var line = reply.Lines.FirstOrDefault(l => l.StartsWith(ModePrefix, StringComparison.OrdinalIgnoreCase));
            var field = line == null ? null : line.Substring(ModePrefix.Length).Split(',')[0].Trim();
            if (field == null || !int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode))
            {
                throw new InvalidOperationException($"{_settings.Name} 读工作模式失败，回执：{string.Join(" ", reply.Lines)}");
            }
            return mode;
        }

        /// <summary>
        /// 切工作模式（AT+MODE=n）：true=AT 指令模式，false=数据透传模式。
        /// 只写 mode 一个参数是手册给的写法，其余透传参数维持网关里现有的配置。
        /// </summary>
        public Task SetModeAsync(bool atMode, CancellationToken ct = default)
            => WriteCheckedAsync($"AT+MODE={(atMode ? 0 : 1)}", atMode ? "切到 AT 指令模式" : "切到数据透传模式", ct);

        /// <summary>
        /// 设置 MAC 过滤（AT+FLT=14,3,9,{mac}）；传空则关掉过滤（类型位 255），
        /// 关过滤后上报的是视野内所有广播。地址里的分隔符会被去掉，只支持填地址前几个字节做部分匹配。
        /// </summary>
        public async Task SetMacFilterAsync(string? mac, CancellationToken ct = default)
        {
            var normalized = NormalizeMac(mac);
            var command = normalized == null
                ? DisableFilterCommand
                : string.Format(CultureInfo.InvariantCulture, MacFilterFormat, normalized);
            await WriteCheckedAsync(command, normalized == null ? "关闭 MAC 过滤" : $"设置 MAC 过滤 {normalized}", ct)
                .ConfigureAwait(false);
        }

        public Task StartScanAsync(CancellationToken ct = default)
            => WriteCheckedAsync(StartScanCommand, "开扫描", ct);

        public Task StopScanAsync(CancellationToken ct = default)
            => WriteCheckedAsync(StopScanCommand, "停扫描", ct);

        /// <summary>
        /// 开扫描收一段时间再停：这段时间内的上报按 MAC 去重（同一 MAC 保留最后一条）。
        /// mac 只在这里做软件筛选，不动网关上的过滤设置。
        /// </summary>
        public async Task<IReadOnlyList<BluetoothScanInfo>> ScanAsync(int durationMs, string? mac = null, CancellationToken ct = default)
        {
            if (durationMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationMs), $"{_settings.Name} 扫描窗口要大于 0ms，收到：{durationMs}");
            }

            var target = NormalizeMac(mac);
            await StopScanAsync(ct).ConfigureAwait(false);

            var collector = new ScanCollector();
            _collector = collector;
            try
            {
                await StartScanAsync(ct).ConfigureAwait(false);
                await Task.Delay(durationMs, ct).ConfigureAwait(false);
            }
            finally
            {
                _collector = null;
                await QuietlyStopScanAsync().ConfigureAwait(false);
            }

            var received = collector.Drain();
            if (target != null)
            {
                received = received.Where(r => NormalizeMac(r.Mac) == target).ToList();
            }

            // 同一广播地址会被反复上报，留最后一次的信号强度
            return received
                .GroupBy(r => r.Mac, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .ToList();
        }

        /// <summary>
        /// 采指定 MAC 的信号强度：停扫描 → 开 MAC 过滤 → 复位一次并接回链路 → 开扫描 → 攒够条数或到超时 → 停扫描 → 还原过滤。
        /// 中间那次复位（AT+OP=0）是把上一轮扫描的残留清干净：过滤参数刚改完就开扫，收上来的可能还是旧队列里的广播，
        /// 表现成「过滤没生效、还在收别的 MAC」。复位只清运行状态不动配置，所以过滤参数不用重发。
        /// 到时间条数没攒够就按已采到的返回，一条没有返回空集合。
        /// 上报的匹配交给网关的过滤去做，这里不再比 MAC，与厂商指令流程一致。
        /// </summary>
        public async Task<IReadOnlyList<int>> ReadRssiAsync(string mac, int? sampleCount = null, int? timeoutMs = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(mac))
            {
                throw new ArgumentException($"{_settings.Name} 采信号强度必须给 MAC", nameof(mac));
            }

            var want = sampleCount ?? _settings.RssiSampleCount;
            var budget = timeoutMs ?? _settings.RssiTimeoutMs;

            await StopScanAsync(ct).ConfigureAwait(false);
            await SetMacFilterAsync(mac, ct).ConfigureAwait(false);

            // 连复位都没人认的话，接着扫只会扫到旧数据，宁可不采
            var outcome = await ObserveRebootAsync(ResetOperationCommand, ct).ConfigureAwait(false);
            if (outcome == RebootOutcome.NotSeen)
            {
                throw new TimeoutException($"{_settings.Name} 采信号强度前的复位没动静：{RebootWaitMs}ms 内既没回 OK 也没冒上电信息");
            }
            if (outcome != RebootOutcome.Booted)
            {
                _log.Warn($"{_settings.Name} {ResetOperationCommand} 只回了 OK，没等到上电信息：模块可能没重启，按现有会话接着采");
            }
            await ReopenAfterRebootAsync(ct).ConfigureAwait(false);

            var collector = new ScanCollector();
            _collector = collector;
            try
            {
                await Task.Delay(FilterSettleDelayMs, ct).ConfigureAwait(false);
                await StartScanAsync(ct).ConfigureAwait(false);
                await collector.WaitForCountAsync(want, budget, ct).ConfigureAwait(false);
            }
            finally
            {
                _collector = null;
                await QuietlyStopScanAsync().ConfigureAwait(false);
                await QuietlyClearFilterAsync().ConfigureAwait(false);
            }

            var samples = collector.Drain().Select(r => r.Rssi).ToList();
            _log.Info($"{_settings.Name} {mac} 采到 {samples.Count}/{want} 条信号强度");
            return samples;
        }

        /// <summary>
        /// 网关复位（AT+REST=1）。模块复位不一定回一行 OK，主要是重启后把上电信息（ATI 开头那几行）重新播一遍，
        /// 所以这里等的是模块重启的动静。模块重启会把这个 TCP 会话一并带走，
        /// 不管等到什么特征，返回前都会把会话关掉，调用方要重新 OpenAsync。
        /// </summary>
        public async Task ResetAsync(CancellationToken ct = default)
        {
            var outcome = await ObserveRebootAsync(RestartCommand, ct).ConfigureAwait(false);

            // 复位把网络也一并重来，留着这个会话只会让后面的指令全压在超时上
            await CloseInternalAsync().ConfigureAwait(false);

            if (outcome == RebootOutcome.NotSeen)
            {
                throw new TimeoutException($"{_settings.Name} 复位后 {RebootWaitMs}ms 内没等到上电信息，确认模块真的重启了没");
            }
            _log.Info($"{_settings.Name} 已复位，需重新建链");
        }

        // ==================== 内部实现 ====================

        /// <summary>下发重启类指令后看到的动静</summary>
        private enum RebootOutcome
        {
            /// <summary>看到上电信息，或连接已被模块重启带走</summary>
            Booted,
            /// <summary>只等到这条指令自己的 OK，模块没重启的动静</summary>
            AckedOnly,
            /// <summary>OK 与上电信息都没有，会话也没断：这条指令根本没人认</summary>
            NotSeen,
        }

        /// <summary>
        /// 下发一条会让模块重启的指令，然后在同一条会话上等模块的动静：上电信息（ATI 开头那几行）、
        /// 这条指令自己的 OK、或者连接被模块重启带到断开，都是模块动了的证据。
        /// 全程占着发送窗口并把这几行都收进同一个盒子：上电信息不属于任何指令，
        /// 一松开挂起就会被当成「无人认领的行」丢掉，再想等就等不到了。
        /// </summary>
        private async Task<RebootOutcome> ObserveRebootAsync(string command, CancellationToken ct)
        {
            var client = _client ?? throw new InvalidOperationException($"{_settings.Name} 未连接，请先调用 OpenAsync");

            await _commandLock.WaitAsync(ct).ConfigureAwait(false);
            var box = new ReplyBox();
            _pending = box;
            var outcome = RebootOutcome.NotSeen;
            try
            {
                await client.SendAsync(command, ct: ct).ConfigureAwait(false);
                var deadline = Environment.TickCount64 + RebootWaitMs;
                while (Environment.TickCount64 < deadline)
                {
                    // 上电信息或连接断开都算模块真重启了；只回了 OK 就不能算，不然会拿重启中的模块当能用的模块
                    if (box.Lines.Any(IsBootBannerLine) || !client.IsOpen || _pumpTask?.IsCompleted == true)
                    {
                        outcome = RebootOutcome.Booted;
                        break;
                    }
                    if (box.Success)
                    {
                        outcome = RebootOutcome.AckedOnly;
                    }
                    await Task.Delay(BootProbeIntervalMs, ct).ConfigureAwait(false);
                }
                _log.Debug($"{_settings.Name} {command} -> {outcome}：{string.Join(" ", box.Lines)}");
            }
            finally
            {
                _pending = null;
                _commandLock.Release();
            }
            return outcome;
        }

        /// <summary>
        /// 复位之后把链路续上：模块起来要时间，连不上就隔一会儿再试，到点还没成就抛。
        /// 会话还开着就什么也不用做：OpenAsync 会直接返回，而上电信息本身就是模块起来的凭据。
        /// </summary>
        private async Task ReopenAfterRebootAsync(CancellationToken ct)
        {
            var deadline = Environment.TickCount64 + RebootWaitMs;
            Exception? last = null;
            while (Environment.TickCount64 < deadline)
            {
                try
                {
                    await OpenAsync(ct).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    last = ex;
                    await Task.Delay(ReconnectProbeIntervalMs, ct).ConfigureAwait(false);
                }
            }
            throw new TimeoutException($"{_settings.Name} 复位后 {RebootWaitMs}ms 内没重新连上：{last?.Message}");
        }

        /// <summary>一条挂起命令的收包盒：数据行往里攒，见到 OK / ERROR 就收口</summary>
        private sealed class ReplyBox
        {
            private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly List<string> _lines = new();

            /// <summary>收到 OK 为 true，收到 ERROR 或被断开为 false</summary>
            public bool Success { get; private set; }

            public IReadOnlyList<string> Lines
            {
                get { lock (_lines) { return _lines.ToArray(); } }
            }

            /// <summary>本轮收口信号（只表示"结束了"，成没成看 Success）</summary>
            public Task Done => _done.Task;

            public void Append(string line)
            {
                lock (_lines) { _lines.Add(line); }
            }

            public void Finish(bool success)
            {
                Success = success;
                _done.TrySetResult();
            }
        }

        /// <summary>一次扫描窗口的收数盒：读取泵往里塞，采集方按条数或超时取</summary>
        private sealed class ScanCollector
        {
            private readonly ConcurrentQueue<BluetoothScanInfo> _items = new();
            private readonly SemaphoreSlim _signal = new(0);

            public void Add(BluetoothScanInfo info)
            {
                _items.Enqueue(info);
                _signal.Release();
            }

            /// <summary>攒够 count 条就返回；到时间没攒够也返回（不抛，由调用方看条数）</summary>
            public async Task WaitForCountAsync(int count, int timeoutMs, CancellationToken ct)
            {
                var deadline = Environment.TickCount64 + timeoutMs;
                while (_items.Count < count)
                {
                    var remainingMs = deadline - Environment.TickCount64;
                    if (remainingMs <= 0)
                    {
                        return;
                    }
                    await _signal.WaitAsync(TimeSpan.FromMilliseconds(remainingMs), ct).ConfigureAwait(false);
                }
            }

            public IReadOnlyList<BluetoothScanInfo> Drain()
            {
                var all = new List<BluetoothScanInfo>(_items.Count);
                while (_items.TryDequeue(out var item))
                {
                    all.Add(item);
                }
                return all;
            }
        }

        /// <summary>起后台读取泵（只在 OpenAsync 里调一次）</summary>
        private void StartPump()
        {
            var cts = new CancellationTokenSource();
            _pumpCts = cts;
            _pumpTask = Task.Run(() => PumpAsync(cts.Token), CancellationToken.None);
        }

        /// <summary>
        /// 读取泵：一行一行读，读到就分流。空转一轮（没数据）只是继续等，
        /// 对端断开或流被Dispose 时才退出，退出时把原因写日志。
        /// </summary>
        private async Task PumpAsync(CancellationToken ct)
        {
            var client = _client;
            if (client == null)
            {
                return;
            }

            try
            {
                while (!ct.IsCancellationRequested && client.IsOpen)
                {
                    var line = await client.ReadFrameAsync(TcpLineClient.NewLine, PumpIdleSliceMs, ct)
                                   .ConfigureAwait(false);
                    if (line != null)
                    {
                        Dispatch(line);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 主动关泵，正常出口
            }
            catch (Exception ex)
            {
                _log.Warn($"{_settings.Name} 上报读取已停止：{ex.Message}");
            }
        }

        /// <summary>把一行收到的文本分到上报或命令回执两头</summary>
        private void Dispatch(string line)
        {
            if (IsUnsolicited(line))
            {
                if (line.StartsWith(ScanPrefix, StringComparison.Ordinal))
                {
                    var info = ParseScanNotification(line);
                    if (info.HasValue)
                    {
                        _log.Debug($"{_settings.Name} 上报 {info.Value}");
                        _collector?.Add(info.Value);
                    }
                }
                else
                {
                    _log.Debug($"{_settings.Name} 上报 {line}");
                }
                return;
            }

            var box = _pending;
            if (box == null)
            {
                // 没人在等这行：超时后的迟到回执，或模块自己冒的文本
                _log.Debug($"{_settings.Name} 无人认领的行：{line}");
                return;
            }

            if (line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                box.Finish(false);
            }
            else if (string.Equals(line, "OK", StringComparison.OrdinalIgnoreCase))
            {
                box.Finish(true);
            }
            else
            {
                box.Append(line);
            }
        }

        private static bool IsUnsolicited(string line)
            => UnsolicitedPrefixes.Any(p => line.StartsWith(p, StringComparison.Ordinal));

        /// <summary>
        /// 下发一条指令并等它自己的 OK / ERROR：串行占用发送窗口，期间读取泵把回执塞进收包盒。
        /// 等不到收口就抛超时并清掉本轮挂起，迟到的回执只会落成一行无人认领的日志，不会一直占着发送窗口。
        /// </summary>
        private async Task<ReplyBox> TransceiveAsync(string command, CancellationToken ct)
        {
            var client = _client ?? throw new InvalidOperationException($"{_settings.Name} 未连接，请先调用 OpenAsync");
            if (!client.IsOpen)
            {
                throw new InvalidOperationException($"{_settings.Name} 连接已断开，请重新 OpenAsync");
            }

            await _commandLock.WaitAsync(ct).ConfigureAwait(false);
            var box = new ReplyBox();
            _pending = box;
            try
            {
                await client.SendAsync(command, ct: ct).ConfigureAwait(false);
                try
                {
                    // WaitAsync 到点是抛 TimeoutException，这里换成带指令内容的消息往外抛
                    await box.Done.WaitAsync(TimeSpan.FromMilliseconds(_settings.ResponseTimeoutMs), ct)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    throw new TimeoutException($"{_settings.Name} 响应超时（{_settings.ResponseTimeoutMs}ms）：{command}");
                }

                _log.Debug($"{_settings.Name} {command} -> {(box.Success ? "OK" : "ERROR")} {string.Join(" ", box.Lines)}");
                return box;
            }
            finally
            {
                _pending = null;
                _commandLock.Release();
            }
        }

        /// <summary>下发设置类指令，模块回 ERROR 就抛（这类指令没有数据行，只认 OK）</summary>
        private async Task WriteCheckedAsync(string command, string what, CancellationToken ct)
        {
            var reply = await TransceiveAsync(command, ct).ConfigureAwait(false);
            if (!reply.Success)
            {
                throw new InvalidOperationException($"{_settings.Name} {what}失败，回执：{string.Join(" ", reply.Lines)}");
            }
            _log.Info($"{_settings.Name} {what}");
        }

        /// <summary>
        /// 解析 +SC_NTF 上报。基本格式十段；请求了回应包且数据段走 ASCII 时末尾多出 scan_data_len,scan_data 两段，
        /// 所以第 11 段之后并到回应包数据里，广播数据段（第 10 段）不会被它污染。
        /// 字段数不够或信号强度不是数字就当作没解出来（返回 null，只记日志）。
        /// </summary>
        private BluetoothScanInfo? ParseScanNotification(string line)
        {
            var parts = line.Substring(ScanPrefix.Length).Split(',');
            if (parts.Length < 10)
            {
                _log.Warn($"{_settings.Name} 上报字段数不足（{parts.Length}）：{line}");
                return null;
            }

            if (!int.TryParse(parts[4].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rssi))
            {
                _log.Warn($"{_settings.Name} 上报里的信号强度不是数字：{line}");
                return null;
            }

            TryParseInt(parts[5], out var txPower);
            TryParseInt(parts[6], out var channelIndex);

            return new BluetoothScanInfo(
                parts[0].Trim(),
                rssi,
                txPower,
                channelIndex,
                parts[9].Trim(),
                parts.Length > 11 ? string.Join(",", parts.Skip(11)).Trim() : string.Empty);
        }

        /// <summary>上电信息块的开头行（模块复位后会重新播一遍）</summary>
        private static bool IsBootBannerLine(string line)
            => line.Equals(BootBannerMarker, StringComparison.OrdinalIgnoreCase);

        private static bool TryParseInt(string text, out int value)
            => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        /// <summary>
        /// MAC 归一：过滤指令里的地址写法与上报里的地址写法可能一个带分隔符一个不带，
        /// 统一成大写无分隔符再比。
        /// </summary>
        private static string? NormalizeMac(string? mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
            {
                return null;
            }

            var chars = mac.Where(Uri.IsHexDigit).ToArray();
            return chars.Length == 0 ? null : new string(chars).ToUpperInvariant();
        }

        private async Task QuietlyStopScanAsync()
        {
            try
            {
                await StopScanAsync().ConfigureAwait(false);
                await Task.Delay(StopSettleDelayMs).ConfigureAwait(false);
            }
            catch (Exception ex) { _log.Warn($"{_settings.Name} 停扫描没成功：{ex.Message}"); }
        }

        private async Task QuietlyClearFilterAsync()
        {
            try { await SetMacFilterAsync(null).ConfigureAwait(false); }
            catch (Exception ex) { _log.Warn($"{_settings.Name} 还原 MAC 过滤没成功：{ex.Message}"); }
        }

        private async Task CloseInternalAsync()
        {
            var cts = _pumpCts;
            var pump = _pumpTask;
            var client = _client;
            _pumpCts = null;
            _pumpTask = null;
            _client = null;
            _collector = null;
            _pending?.Finish(false);

            try { cts?.Cancel(); } catch { /* 关泵时抛属正常 */ }
            if (pump != null)
            {
                try { await pump.ConfigureAwait(false); } catch { /* 同上 */ }
            }
            cts?.Dispose();

            if (client != null)
            {
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
                // 释放阶段只能同步等：给 CloseInternal 一点时间把泵收掉
                CloseInternalAsync().Wait(TimeSpan.FromSeconds(2));
            }
            catch { /* 释放阶段忽略 */ }

            _commandLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
