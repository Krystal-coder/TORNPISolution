using System.Globalization;
using System.Text.RegularExpressions;
using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// CRY5242/5243 四通道电流电压综测仪控制类（网口 SCPI 文本指令，一问一答以 \r\n 结束）。
    /// 每通道各一条 TCP 连接（端口 = 配置的通道 A 端口 + 通道号 - 1），四个端口都建链才能同时驱动四通道；
    /// 同一条连接上的发送与读回执由信号量串行，避免两条命令的回执互串。
    /// 电压一律 mV（0~6500）、电流一律 mA（-2000~+2000），按指令表原样传整数，不换算，只拦越界。
    /// 设置类指令回执为 CHx: done，测量类为 CHx: [数值, '单位']，回执认不出 done 即按失败抛。
    /// OUTPUT 第三参数在 5242 上是过流保护值（通道电流超过它就红灯、停输出，只有 *RST 恢复），不是恒流点；
    /// 当限流值用是高配 5243 的行为（手册 4.1.1）。
    /// </summary>
    public class Cry5242Tester : ICry5242Tester
    {
        /// <summary>测量回执：CHx: [数值, '单位']（数值可能是负数，单位由仪器按档位给）</summary>
        private static readonly Regex ReadingPattern = new(
            @"^(?<Channel>CH\d+):\s*\[\s*(?<Value>-?\d*\.?\d+)\s*,\s*'(?<Unit>[^']*)'\s*\]$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>回执里表示"这条指令做完了"的标志词</summary>
        private const string DoneToken = "done";

        /// <summary>*RST 要关四个通道的输出，比寻常指令慢得多，单独给它 10s</summary>
        private const int ResetTimeoutMs = 10000;

        // 指令表给的量程，越界就不发：本仪器直接给主板供电，量纲错了就是往主板上错输出
        private const int MaxVoltageMilliVolts = 6500;
        private const int MinCurrentMilliAmps = -2000;
        private const int MaxCurrentMilliAmps = 2000;
        private const int MinRampMilliSeconds = 10;
        private const int MaxRampMilliSeconds = 100;

        private readonly Cry5242Settings _settings;
        private readonly ILogService _log;
        private readonly Dictionary<int, ChannelLink> _channels = new();
        private readonly object _channelsLock = new();
        private bool _disposed;

        /// <summary>一台仪器的一个通道：一条 TCP 连接 + 该连接的命令串行锁</summary>
        private sealed class ChannelLink(int channel, TcpLineClient client)
        {
            public int Channel { get; } = channel;
            public TcpLineClient Client { get; } = client;
            public SemaphoreSlim Bus { get; } = new(1, 1);
        }

        public Cry5242Tester(Cry5242Settings settings, ILogService? log = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _log = log ?? new LogService($"{nameof(Cry5242Tester)}:{settings.Name}");
        }

        public string Name => _settings.Name;

        public bool IsOpen
        {
            get
            {
                lock (_channelsLock)
                {
                    return _channels.Count == Cry5242Settings.ChannelCount
                           && _channels.Values.All(l => l.Client.IsOpen);
                }
            }
        }

        /// <summary>逐通道建链并各读一次 *IDN? 验证链路；任一通道连不上就把已建的收干净再抛，不留半开状态（半开时后续指令只报"未连接"）</summary>
        public async Task OpenAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            for (var channel = 1; channel <= Cry5242Settings.ChannelCount; channel++)
            {
                TcpLineClient? client = null;
                try
                {
                    var port = _settings.PortOfChannel(channel);
                    client = new TcpLineClient(_settings.IpAddress, port, _settings.ConnectTimeoutMs, _log);
                    await client.ConnectAsync(ct).ConfigureAwait(false);
                    var idn = await QueryRawAsync(channel, client, "*IDN?", _settings.ResponseTimeoutMs, ct).ConfigureAwait(false);
                    AddLink(channel, client);
                    var remote = client.Remote;
                    client = null;   // 连接已登记，后续清理走 CloseAllChannelsAsync，不再由本轮负责
                    _log.Info($"{_settings.Name} CH{channel}（{remote}）已连接：{idn}");
                }
                catch (Exception ex)
                {
                    client?.Dispose();
                    await CloseAllChannelsAsync().ConfigureAwait(false);
                    throw new InvalidOperationException($"{_settings.Name} 通道 {channel} 建链失败：{ex.Message}", ex);
                }
            }
        }

        public async Task CloseAsync()
        {
            await CloseAllChannelsAsync().ConfigureAwait(false);
            _log.Info($"{_settings.Name} 全部通道连接已关闭");
        }

        public Task<string> GetIdentityAsync(int channel, CancellationToken ct = default)
            => QueryAsync(channel, "*IDN?", ct);

        /// <summary>
        /// 整机复位（*RST）：不带通道参数，对任一通道下发即关掉全部通道输出，过流保护动作后也只能靠它恢复。
        /// 回执仍按厂商模板等一行 done（这条给 10s）；手册 4.6 建议连续测试每半小时复位一次。
        /// </summary>
        public async Task ResetAsync(CancellationToken ct = default)
        {
            var channel = GetAnyOpenChannel();
            await WriteCheckedAsync(channel, "*RST", "复位", ct, ResetTimeoutMs).ConfigureAwait(false);
        }

        /// <summary>只设输出电压与过流保护值、不打开通路（OUTPUT 三参数，不带线制就不会走通路）；要真输出电压走带线制的重载或 OUTPUT:RAMP</summary>
        public Task ConfigureOutputAsync(int channel, int voltageMilliVolts, int currentMilliAmps, CancellationToken ct = default)
        {
            ValidateOutput(voltageMilliVolts, currentMilliAmps);
            return WriteCheckedAsync(channel, $"OUTPUT {channel},{voltageMilliVolts},{currentMilliAmps}",
                $"设输出 {voltageMilliVolts}mV/{currentMilliAmps}mA（不打开通路）", ct);
        }

        /// <summary>设输出电压/保护电流并选二线或四线通路（OUTPUT 四参数，带线制即打开通路）；线制留空取配置里的 FourWire</summary>
        public Task SetOutputAsync(int channel, int voltageMilliVolts, int currentMilliAmps, bool? fourWire = null, CancellationToken ct = default)
        {
            ValidateOutput(voltageMilliVolts, currentMilliAmps);
            var wire = ResolveFourWire(fourWire);
            return WriteCheckedAsync(channel, $"OUTPUT {channel},{voltageMilliVolts},{currentMilliAmps},{(wire ? 4 : 2)}",
                $"设输出 {voltageMilliVolts}mV/{currentMilliAmps}mA {(wire ? "四线" : "二线")}", ct);
        }

        /// <summary>带上升时间的缓输出（OUTPUT:RAMP），避开上电冲击；斜率时间 10~100ms（手册与厂商模板都用 50），线制是指令表的必选项，下发完通路即已打开</summary>
        public Task SetRampOutputAsync(int channel, int voltageMilliVolts, int currentMilliAmps, int rampMilliSeconds, bool? fourWire = null, CancellationToken ct = default)
        {
            ValidateOutput(voltageMilliVolts, currentMilliAmps);
            if (rampMilliSeconds is < MinRampMilliSeconds or > MaxRampMilliSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(rampMilliSeconds),
                    $"{Name} 斜率时间只能 {MinRampMilliSeconds}~{MaxRampMilliSeconds}ms，收到：{rampMilliSeconds}");
            }

            var wire = ResolveFourWire(fourWire);
            return WriteCheckedAsync(channel,
                $"OUTPUT:RAMP {channel},{voltageMilliVolts},{currentMilliAmps},{(wire ? 4 : 2)},{rampMilliSeconds}",
                $"缓输出 {voltageMilliVolts}mV/{currentMilliAmps}mA {(wire ? "四线" : "二线")} 斜率{rampMilliSeconds}ms", ct);
        }

        /// <summary>单独开/关输出通路（OUTPUT:STA）：未先下发 OUTPUT 时这条只空开通路、没有实际输出；OUTPUT / OUTPUT:RAMP 已带线制时通路已打开，不必再发</summary>
        public Task SetOutputEnabledAsync(int channel, bool enabled, CancellationToken ct = default)
            => WriteCheckedAsync(channel, $"OUTPUT:STA {channel},{(enabled ? 1 : 0)}",
                enabled ? "打开输出通路" : "关闭输出通路", ct);

        /// <summary>切换测量通路（FUNC:SWIT）：手册只有 DVM 与 CH_PO 两个电压测量要求先切通路（4.2.1/4.2.2）；电流档位直接带在 MEAS:CURR 参数里，手册的电流例子没先切通路（4.3.1）</summary>
        public Task SwitchFunctionAsync(int channel, Cry5242Function function, CancellationToken ct = default)
            => WriteCheckedAsync(channel, $"FUNC:SWIT {channel},\"{FunctionToken(function)}\"",
                $"切通路 {FunctionToken(function)}", ct);

        public async Task<Cry5242Reading> MeasureDvmVoltageAsync(int channel, CancellationToken ct = default)
            => ParseReading(channel, await QueryAsync(channel, $"MEAS:DVM {channel}", ct).ConfigureAwait(false));

        public async Task<Cry5242Reading> MeasureOutputVoltageAsync(int channel, CancellationToken ct = default)
            => ParseReading(channel, await QueryAsync(channel, $"MEAS:CH_PO {channel}", ct).ConfigureAwait(false));

        /// <summary>测该通道电流（MEAS:CURR，模拟电池模式）：量程由第二个参数给，'mA'/'uA'/'nA'/'auto'</summary>
        public async Task<Cry5242Reading> MeasureCurrentAsync(int channel, Cry5242CurrentRange range, CancellationToken ct = default)
            => ParseReading(channel, await QueryAsync(channel,
                $"MEAS:CURR {channel},\"{RangeToken(range)}\"", ct).ConfigureAwait(false));

        /// <summary>测外部供电时的电流（MEAS:CURR:METER）：手册 4.3.2 要求在本通道不输出时读，且不超 2A</summary>
        public async Task<Cry5242Reading> MeasureExternalCurrentAsync(int channel, Cry5242CurrentRange range, CancellationToken ct = default)
            => ParseReading(channel, await QueryAsync(channel,
                $"MEAS:CURR:METER {channel},\"{RangeToken(range)}\"", ct).ConfigureAwait(false));

        /// <summary>读通道温度（GET_TEMP，手册 4.4）：手册只给指令没给回执格式，只从整行里取第一个数值；仪器按温度自控风扇，55/60/75°C 分三档，超 80°C 过温报警（4.4/4.5）</summary>
        public async Task<double> GetTemperatureAsync(int channel, CancellationToken ct = default)
        {
            var reply = await QueryAsync(channel, $"GET_TEMP {channel}", ct).ConfigureAwait(false);
            var match = Regex.Match(reply, @"-?\d+(\.\d+)?");
            if (!match.Success || !double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidOperationException($"{_settings.Name} CH{channel} 温度回执解析不出来：{reply}");
            }
            return value;
        }

        /// <summary>读全部 IO 状态（IO:STA?）并原样返回文本：这条不带通道参数，回执不止一行时一问一答只拿到第一行；需固件 0.0.9 以上</summary>
        public Task<string> ReadIoStatusAsync(int channel, CancellationToken ct = default)
            => QueryAsync(channel, "IO:STA?", ct);

        // ==================== 内部实现 ====================

        /// <summary>发一条指令并等一行回执（同通道串行）</summary>
        private async Task<string> QueryAsync(int channel, string command, CancellationToken ct, int? timeoutMs = null)
        {
            var link = GetLink(channel);
            await link.Bus.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await QueryRawAsync(channel, link.Client, command, timeoutMs ?? _settings.ResponseTimeoutMs, ct)
                            .ConfigureAwait(false);
            }
            finally
            {
                link.Bus.Release();
            }
        }

        private async Task<string> QueryRawAsync(int channel, TcpLineClient client, string command, int timeoutMs, CancellationToken ct)
        {
            // 与厂商模板一致：发之前先清一次接收缓冲，免得上一条超时后迟到的回执被这一条读走
            client.ClearReceived();
            await client.SendAsync(command, ct: ct).ConfigureAwait(false);
            var reply = await client.ReadFrameAsync(TcpLineClient.NewLine, timeoutMs, ct)
                            .ConfigureAwait(false);
            if (reply == null)
            {
                throw new TimeoutException($"{_settings.Name} CH{channel} 响应超时（{timeoutMs}ms）：{command}");
            }

            _log.Debug($"{_settings.Name} CH{channel} {command} -> {reply}");
            return reply;
        }

        /// <summary>下发设置类指令，并要求回执带 done（不带就是仪器没认这条命令）</summary>
        private async Task WriteCheckedAsync(int channel, string command, string what, CancellationToken ct, int? timeoutMs = null)
        {
            var reply = await QueryAsync(channel, command, ct, timeoutMs).ConfigureAwait(false);
            if (!reply.Contains(DoneToken, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{_settings.Name} CH{channel} {what}失败，回执：{reply}");
            }
            _log.Info($"{_settings.Name} CH{channel} {what}");
        }

        /// <summary>线制取值：调用方没指定就用配置里的 FourWire；指令表只认 2 和 4，手册 4.1.2 里表示二线的 ,1 是指令表没给的写法，不照它发</summary>
        private bool ResolveFourWire(bool? fourWire) => fourWire ?? _settings.FourWire;

        /// <summary>输出电压与保护电流按指令表的量程拦一道：0~6500mV、-2000~+2000mA</summary>
        private void ValidateOutput(int voltageMilliVolts, int currentMilliAmps)
        {
            if (voltageMilliVolts is < 0 or > MaxVoltageMilliVolts)
            {
                throw new ArgumentOutOfRangeException(nameof(voltageMilliVolts),
                    $"{Name} 输出电压只能 0~{MaxVoltageMilliVolts}mV，收到：{voltageMilliVolts}");
            }
            if (currentMilliAmps is < MinCurrentMilliAmps or > MaxCurrentMilliAmps)
            {
                throw new ArgumentOutOfRangeException(nameof(currentMilliAmps),
                    $"{Name} 电流设定值只能 {MinCurrentMilliAmps}~{MaxCurrentMilliAmps}mA，收到：{currentMilliAmps}");
            }
        }

        /// <summary>解析 CHx: [数值, '单位']</summary>
        private Cry5242Reading ParseReading(int channel, string reply)
        {
            var match = ReadingPattern.Match(reply);
            if (!match.Success
                || !double.TryParse(match.Groups["Value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidOperationException($"{_settings.Name} CH{channel} 测量回执解析不出来：{reply}");
            }

            return new Cry5242Reading(channel, value, match.Groups["Unit"].Value);
        }

        /// <summary>电流档位词表（指令里带双引号下发，与厂商模板一致）</summary>
        private static string RangeToken(Cry5242CurrentRange range) => range switch
        {
            Cry5242CurrentRange.Auto => "auto",
            Cry5242CurrentRange.Milliamps => "mA",
            Cry5242CurrentRange.Microamps => "uA",
            Cry5242CurrentRange.Nanoamps => "nA",
            _ => throw new ArgumentOutOfRangeException(nameof(range), range, "未知电流档位"),
        };

        /// <summary>测量通路词表（指令表给的八个取值；带 out 的是对应测量外部供电电流的通路）</summary>
        private static string FunctionToken(Cry5242Function function) => function switch
        {
            Cry5242Function.Ma => "ma",
            Cry5242Function.Ua => "ua",
            Cry5242Function.Na => "na",
            Cry5242Function.MaOut => "ma_out",
            Cry5242Function.UaOut => "ua_out",
            Cry5242Function.NaOut => "na_out",
            Cry5242Function.Dvm => "dvm",
            Cry5242Function.Po => "po",
            _ => throw new ArgumentOutOfRangeException(nameof(function), function, "未知测量通路"),
        };

        private void AddLink(int channel, TcpLineClient client)
        {
            lock (_channelsLock)
            {
                if (_channels.TryGetValue(channel, out var old))
                {
                    old.Client.Dispose();
                    _channels.Remove(channel);
                }
                _channels[channel] = new ChannelLink(channel, client);
            }
        }

        private ChannelLink GetLink(int channel)
        {
            lock (_channelsLock)
            {
                if (!_channels.TryGetValue(channel, out var link) || !link.Client.IsOpen)
                {
                    throw new InvalidOperationException($"{_settings.Name} CH{channel} 未连接，请先调用 OpenAsync");
                }
                return link;
            }
        }

        private int GetAnyOpenChannel()
        {
            lock (_channelsLock)
            {
                foreach (var link in _channels.Values.Where(l => l.Client.IsOpen).OrderBy(l => l.Channel))
                {
                    return link.Channel;
                }
            }
            throw new InvalidOperationException($"{_settings.Name} 没有一条可用连接，请先调用 OpenAsync");
        }

        private async Task CloseAllChannelsAsync()
        {
            List<ChannelLink> links;
            lock (_channelsLock)
            {
                links = _channels.Values.ToList();
                _channels.Clear();
            }

            foreach (var link in links)
            {
                await link.Client.CloseAsync().ConfigureAwait(false);
                link.Client.Dispose();
                link.Bus.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            List<ChannelLink> links;
            lock (_channelsLock)
            {
                links = _channels.Values.ToList();
                _channels.Clear();
            }
            foreach (var link in links)
            {
                try { link.Client.Dispose(); } catch { /* 释放阶段忽略 */ }
                link.Bus.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}
