using System.IO.Ports;
using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 迈斯威志 WizPro 系列烧录器控制类：一路一台烧录器、各占一个 COM，八个通道可真并发、互不影响。
    /// 串口参数厂商规定 1M 波特率、8 数据位、无校验、2 停止位；USB 转串口芯片不支持 1M 时表现为一直收不到帧。
    /// 帧以 0xC0 定界（0xC0 + 数据 + 0xC0）：数据里出现 0xC0 写成 0xDB 0xDC、出现 0xDB 写成 0xDB 0xDD；
    /// 去掉首尾定界符后的字节整段累加、模 256 为 0 即校验通过。三条下发指令连各自的校验字节按文档原样发、不要推算，它们用的不是这个累加规则。
    /// 过程状态由烧录器主动连发，启动烧写后只能一直读、按帧分派，不能一问一答。
    /// 校验不过或遇到文档没列的状态码只记日志不判失败：厂商几版文档字段口径对不齐，把没见过的当失败会造成现场随机误判；原始帧一律留在 Trace 里供事后校准。
    /// </summary>
    public class WizProProgrammer : IWizProProgrammer
    {
        private const byte Marker = 0xC0;
        private const byte Escape = 0xDB;

        /// <summary>复位编程器：让它重新自检内部已下载的数据，回 WPKO 才算可烧</summary>
        private static readonly byte[] ResetPayload =
            { 0x8A, 0x52, 0x45, 0x53, 0x54, 0xA8, 0x02, 0x00, 0x00, 0x00, 0xCD, 0x02 };   // REST

        /// <summary>启动一轮烧写</summary>
        private static readonly byte[] StartProgramPayload =
            { 0xF9, 0x41, 0x50, 0x52, 0x47, 0x9F, 0x00, 0x00, 0xE3, 0x4D };               // APRG

        /// <summary>取烧写通道号与预置产品名称（用来把 COM 和通道对上）</summary>
        private static readonly byte[] QueryIdentityPayload =
            { 0xF3, 0x0C, 0x52, 0x44, 0x4D, 0x43, 0x00, 0x00, 0x9E, 0x61 };               // RDMC

        // 过程状态帧（0xFD 开头）里的状态码1，只列本项目用得到的
        private const byte StateEnter = 0x10;         // 进入烧写模式
        private const byte StateBlank = 0x20;         // 查空
        private const byte StateErase = 0x30;         // 擦除
        private const byte StateWrite = 0x40;         // 写主 Flash 数据区
        private const byte StateWriteAux = 0x50;      // 写 EEP 或用户数据区
        private const byte StateVerify = 0x70;        // 校验主数据区
        private const byte StateDone1 = 0xF0;         // 结束（状态码2 为 0x06 是成功，否则就是错误码）
        private const byte StateDone2 = 0xF1;
        private const byte StateDone3 = 0xF2;
        private const byte StateStatus = 0xFA;        // 烧录器与座子状态（WPKO/WPGN/PCNI/PCSR/LOCK/CERR）

        /// <summary>结束帧里表示成功的那个状态码2</summary>
        private const byte DoneOk = 0x06;

        /// <summary>单帧负载上限：复位应答的长信息帧超过 152 字节，留一倍余量</summary>
        private const int MaxFrameBytes = 512;

        /// <summary>攒一帧时最长等多久，再回头看总超时有没有到（也决定取消的响应速度）</summary>
        private const int FrameWaitMs = 200;

        private readonly WizProFlashSettings _settings;
        private readonly ILogService _log;
        private readonly string _tag;
        private SerialClient? _client;
        private bool _disposed;

        public WizProProgrammer(int channel, string portName, WizProFlashSettings settings, ILogService? log = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException($"通道 {channel} 没有配烧录器串口（烧录配置里这一路的 WizProPortName 是空的）", nameof(portName));

            Channel = channel;
            PortName = portName;
            _tag = $"烧录通道{channel}";
            _log = log ?? new LogService($"{nameof(WizProProgrammer)}:CH{channel}");
        }

        public int Channel { get; }

        public string PortName { get; }

        public bool IsOpen => _client?.IsOpen == true;

        public Task OpenAsync(CancellationToken ct = default) => Task.Run(async () =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_client != null) return;

            var client = new SerialClient(PortName, _settings.BaudRate, Parity.None,
                _settings.DataBits, _settings.StopBits == 1 ? StopBits.One : StopBits.Two,
                _settings.ResponseTimeoutMs, _log);
            await client.OpenAsync(ct).ConfigureAwait(false);
            _client = client;
            _log.Info($"{_tag} 已连上烧录器 {PortName}（{_settings.BaudRate},{_settings.DataBits}N{_settings.StopBits}）");
        }, ct);

        public async Task CloseAsync()
        {
            var client = _client;
            _client = null;
            if (client == null) return;

            try { await client.CloseAsync().ConfigureAwait(false); }
            finally { client.Dispose(); }
        }

        /// <summary>
        /// 读设备自报的通道号与预置产品信息。文档说返回帧长度大于 80 走解析法1（偏移 8 是通道号）、否则走法2（偏移 9 是通道号），
        /// 但没给完整示例帧、偏移是否含定界符不确定，所以两个位置都取一次、认落在 0~9 的那一个；解不出来返回 null（有些型号不支持这条指令），原始帧留在日志里供校准。
        /// </summary>
        public Task<WizProIdentity?> QueryIdentityAsync(CancellationToken ct = default) => Task.Run<WizProIdentity?>(() =>
        {
            var client = RequireClient();
            var trace = new List<string>();

            Send(client, QueryIdentityPayload, trace, ct);

            var deadline = Environment.TickCount64 + _settings.ResponseTimeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                ct.ThrowIfCancellationRequested();
                var frame = ReadFrame(client, deadline, trace, ct);
                if (frame == null) continue;

                trace.Add($"RX {Hex(frame)}");
                if (frame.Count < 10) continue;

                var channelId = PickChannelId(frame);
                var product = PickProductInfo(frame);
                if (channelId == null && string.IsNullOrEmpty(product)) continue;

                FlushTrace(trace);
                return new WizProIdentity(channelId ?? -1, product ?? string.Empty, Hex(frame));
            }

            FlushTrace(trace);
            _log.Warn($"{_tag} 没读到自报通道号（这台设备可能不支持 RDMC 指令），COM 与通道的对应只能人工核对");
            return null;
        }, ct);

        /// <summary>发 REST 让烧录器复位并自检内部已下载的数据：回 WPKO 表示数据正常可烧，回 WPGN 表示内部数据有问题、必须重新下载工程（此时直接开烧只会整批失败，且报错看不出真因）。</summary>
        public Task<bool> SelfCheckAsync(CancellationToken ct = default) => Task.Run(() =>
        {
            var client = RequireClient();
            var trace = new List<string>();

            Send(client, ResetPayload, trace, ct);

            var deadline = Environment.TickCount64 + _settings.ResponseTimeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                ct.ThrowIfCancellationRequested();
                var frame = ReadFrame(client, deadline, trace, ct);
                if (frame == null) continue;

                trace.Add($"RX {Hex(frame)}");
                if (!TryReadStatus(frame, out var state1, out _, out var data)) continue;
                if (state1 != StateStatus) continue;     // 复位后还会吐一串上电信息，这里只认状态帧

                if (IsAscii(data, "WPKO")) { FlushTrace(trace); _log.Info($"{_tag} 自检通过：数据正常，可以烧写"); return true; }
                if (IsAscii(data, "WPGN"))
                {
                    FlushTrace(trace);
                    _log.Error($"{_tag} 自检不通过：内部数据异常，要重新下载工程数据");
                    return false;
                }
            }

            FlushTrace(trace);
            throw new TimeoutException(
                $"{_tag} 等自检结果超时（{_settings.ResponseTimeoutMs}ms）：既没说可烧、也没说数据异常，先确认 {PortName} 是不是这台烧录器");
        }, ct);

        /// <summary>启动一轮烧写并一直读到结束帧，过程状态（查空/擦除/写入/校验/结束）每步经 onProgress 报出；烧写失败不抛异常，统一用返回结果的 Success=false 加错误码表达，只有取消才往上抛。</summary>
        public Task<FlashResult> ProgramAsync(IProgress<FlashProgress>? onProgress, CancellationToken ct = default)
            => Task.Run<FlashResult>(() =>
        {
            var client = RequireClient();
            var trace = new List<string>();

            Send(client, StartProgramPayload, trace, ct);
            onProgress?.Report(new FlashProgress(FlashStage.WaitingReset, -1, "已发出启动烧写，等设备响应"));

            var deadline = Environment.TickCount64 + _settings.ProgramTimeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                ct.ThrowIfCancellationRequested();

                var frame = ReadFrame(client, deadline, trace, ct);
                if (frame == null) continue;
                trace.Add($"RX {Hex(frame)}");

                if (!TryReadStatus(frame, out var state1, out var state2, out var data)) continue;

                // 结束帧：状态码2 是 0x06 就成，否则它本身就是错误代码（厂商没给错误码对照表，只能原样带出）
                if (state1 is StateDone1 or StateDone2 or StateDone3)
                {
                    FlushTrace(trace);
                    return state2 == DoneOk
                        ? FlashResult.Ok($"烧写完成（成功码 0x{state2:X2}）", trace)
                        : FlashResult.Fail($"烧写失败，错误代码 0x{state2:X2}（厂商未提供错误码对照表，原始帧见明细）",
                            state2, FlashStage.Failed, trace);
                }

                if (state1 == StateStatus)
                {
                    if (IsAscii(data, "CERR"))
                    {
                        FlushTrace(trace);
                        return FlashResult.Fail("烧录器回 Command Error：这台设备的固件不认这条指令", 0xA3, FlashStage.Failed, trace);
                    }

                    // 座子没芯片 / 芯片被拿走 / 座子锁定：不当失败处理，操作人员补压之后设备还会继续往下走
                    var notice = StatusNotice(data);
                    if (notice != null)
                    {
                        onProgress?.Report(new FlashProgress(FlashStage.WaitingReset, -1, notice));
                        _log.Warn($"{_tag} {notice}");
                    }
                    continue;
                }

                var (stage, text) = DescribeState(state1);
                onProgress?.Report(new FlashProgress(stage, PagePercent(state1, data), text));
            }

            FlushTrace(trace);
            return FlashResult.Fail($"等到超时（{_settings.ProgramTimeoutMs}ms）也没收到烧写结果帧：检查这一路有没有压到位、排线是否接对",
                0, FlashStage.Failed, trace);
        }, ct);

        // ==================== 帧的编解码 ====================

        /// <summary>按厂商规则组帧：定界符包起来，数据里的 0xC0 写成 DB DC、0xDB 写成 DB DD</summary>
        private static byte[] BuildFrame(byte[] payload)
        {
            var frame = new List<byte>(payload.Length + 4) { Marker };
            foreach (var b in payload)
            {
                if (b == Marker) { frame.Add(Escape); frame.Add(0xDC); }
                else if (b == Escape) { frame.Add(Escape); frame.Add(0xDD); }
                else frame.Add(b);
            }
            frame.Add(Marker);
            return frame.ToArray();
        }

        /// <summary>把帧内转义还原回原值（DB DC 是 0xC0、DB DD 是 0xDB；后面不跟这两个的 DB 原样留着）</summary>
        private static List<byte> Unescape(List<byte> body)
        {
            var result = new List<byte>(body.Count);
            for (var i = 0; i < body.Count; i++)
            {
                if (body[i] != Escape || i + 1 >= body.Count)
                {
                    result.Add(body[i]);
                    continue;
                }

                switch (body[i + 1])
                {
                    case 0xDC: result.Add(Marker); i++; break;
                    case 0xDD: result.Add(Escape); i++; break;
                    default: result.Add(body[i]); break;
                }
            }
            return result;
        }

        /// <summary>发一帧：先把接收缓冲清掉，免得上一轮没读完的状态帧被当成本次的应答</summary>
        private void Send(SerialClient client, byte[] payload, List<string> trace, CancellationToken ct)
        {
            var frame = BuildFrame(payload);
            client.DiscardInBuffer();
            client.WriteRaw(frame, ct);
            trace.Add($"TX {BitConverter.ToString(frame)}");
        }

        /// <summary>
        /// 攒出一帧完整数据（定界符到定界符）并还原转义，超时返回 null；相邻两帧之间可能连着两个定界符（前一帧尾兼后一帧头时只有一个），内容为空就接着攒。
        /// 一帧攒到超过上限还没等到结束符，说明波特率或转义口径对不上，丢掉重来并留一条日志。
        /// </summary>
        private List<byte>? ReadFrame(SerialClient client, long deadlineMs, List<string> trace, CancellationToken ct)
        {
            var body = new List<byte>();
            var inFrame = false;

            while (Environment.TickCount64 < deadlineMs)
            {
                ct.ThrowIfCancellationRequested();

                foreach (var b in client.ReadRaw(FrameWaitMs, ct))
                {
                    if (b == Marker)
                    {
                        if (!inFrame) { inFrame = true; body.Clear(); continue; }
                        if (body.Count == 0) continue;
                        return Unescape(body);
                    }

                    if (!inFrame) continue;                       // 帧外的杂音直接丢

                    body.Add(b);
                    if (body.Count > MaxFrameBytes)
                    {
                        _log.Warn($"{_tag} 单帧超过 {MaxFrameBytes} 字节仍没等到结束符，丢弃重攒（多半是波特率不对或转义口径不一致）");
                        trace.Add($"[overrun] {BitConverter.ToString(body.ToArray())}");
                        inFrame = false;
                        body.Clear();
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 认出一条过程状态帧并取出状态码1、状态码2、4 字节数据：帧里第一个字节是命令字 0xFD，后面依次是状态码1、状态码2、数据 4 字节、校验和。
        /// 校验不过也照样返回内容——厂商文档的字节数与示例对不齐，宁可收下再记一条日志，不要在这里丢数据。
        /// </summary>
        private bool TryReadStatus(List<byte> frame, out byte state1, out byte state2, out byte[] data)
        {
            state1 = 0;
            state2 = 0;
            data = Array.Empty<byte>();

            if (frame.Count < 2 || frame[1] != 0xFD) return false;     // 不是过程状态帧（上电信息长帧等）
            if (frame.Count < 9)
            {
                _log.Warn($"{_tag} 状态帧只有 {frame.Count} 字节，比文档说的短，原样留着：{Hex(frame)}");
                return false;
            }

            state1 = frame[2];
            state2 = frame[3];
            data = frame.Skip(4).Take(4).ToArray();

            if (frame.Sum(b => (int)b) % 256 != 0)
                _log.Warn($"{_tag} 状态帧累加校验不为 0，仍按内容解析：{Hex(frame)}");

            return true;
        }

        /// <summary>状态码1 翻译成界面阶段与文案（文档没列的状态码按进行中处理，原文进 Trace）</summary>
        private static (FlashStage Stage, string Text) DescribeState(byte state1) => state1 switch
        {
            StateEnter => (FlashStage.Programming, "进入烧写模式"),
            StateBlank => (FlashStage.Erasing, "芯片查空"),
            StateErase => (FlashStage.Erasing, "芯片擦除"),
            StateWrite => (FlashStage.Programming, "写主 Flash 数据区"),
            StateWriteAux => (FlashStage.Programming, "写 EEP 或用户数据区"),
            StateVerify => (FlashStage.Verifying, "校验主数据区"),
            _ => (FlashStage.Programming, $"烧录进行中（状态码 0x{state1:X2}，文档未列，按进行中处理）"),
        };

        /// <summary>擦除/写入/校验这几步的数据是「当前页(2B) + 总页数(2B)」，可折成百分比；该数只用于界面显示、不参与任何判定，字节序厂商没写就按低位在前解，解错也不会把好的判成坏。</summary>
        private int PagePercent(byte state1, byte[] data)
        {
            if (data.Length < 4) return -1;
            if (state1 is not (StateBlank or StateErase or StateWrite or StateWriteAux or StateVerify)) return -1;

            var current = (uint)(data[0] | data[1] << 8);
            var total = (uint)(data[2] | data[3] << 8);
            if (total == 0) return -1;

            return (int)Math.Clamp(current * 100 / total, 0, 100);
        }

        /// <summary>座子状态里的三种提示：不是错误，但操作人员该知道（补压之后设备会自己往下走）</summary>
        private static string? StatusNotice(byte[] data)
        {
            if (IsAscii(data, "PCNI")) return "座子里检测到芯片";
            if (IsAscii(data, "PCSR")) return "芯片被拿开了，等重新压到位";
            if (IsAscii(data, "LOCK")) return "座子被锁定";
            return null;
        }

        private static bool IsAscii(byte[] data, string text)
            => data.Length >= text.Length
               && System.Text.Encoding.ASCII.GetString(data, 0, text.Length) == text;

        /// <summary>通道号：文档给的两个偏移都取一次，认落在 0~9 的那一个</summary>
        private static int? PickChannelId(List<byte> frame)
        {
            foreach (var offset in new[] { 8, 9 })
            {
                if (frame.Count > offset && frame[offset] is >= 0 and <= 9) return frame[offset];
            }
            return null;
        }

        /// <summary>产品型号与版本：文档说从偏移 26 或 28 开始、0xFF 结束，也允许从偏移 13 起扫 Wiz 字样，三种都试</summary>
        private static string? PickProductInfo(List<byte> frame)
        {
            foreach (var start in new[] { 26, 28, 13 })
            {
                if (frame.Count <= start) continue;

                var bytes = frame.Skip(start).TakeWhile(b => b != 0xFF && b != 0x00).ToArray();
                if (bytes.Length < 3) continue;

                var text = System.Text.Encoding.UTF8.GetString(bytes).Trim();
                if (text.Length >= 3 && text.Any(char.IsLetter)) return text;
            }
            return null;
        }

        private SerialClient RequireClient()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var client = _client;
            if (client == null || !client.IsOpen)
                throw new InvalidOperationException($"{_tag} 的串口 {PortName} 没打开");
            return client;
        }

        private static string Hex(List<byte> frame) => BitConverter.ToString(frame.ToArray());

        /// <summary>把这一趟与设备的原始往来落到日志里（界面详情另走 Trace，两边都要，排查时少翻一层）</summary>
        private void FlushTrace(List<string> trace)
        {
            foreach (var line in trace) _log.Debug($"{_tag} {line}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _client?.Dispose();
            _client = null;
            GC.SuppressFinalize(this);
        }
    }
}
