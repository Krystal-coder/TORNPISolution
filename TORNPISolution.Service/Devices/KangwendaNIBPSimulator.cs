using System.IO.Ports;
using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 康伟达 NIBP 无创血压模拟仪控制类（串口 232，实测协议：《无创血压接收发送表》）。
    /// 帧格式：变长短帧，byte0=功能模块ID，下位机收到任何命令后回显 byte0 作 ACK；
    /// 回包 AA 帧 8 字节：AA (压力+10)高8 (压力+10)低8 + 5 字节填充，当前压力 = 原始值/100 − 偏移(默认10)。
    /// 心跳：下位机空闲时发 0x09 询问，读循环内自动原样回 0x09，7 秒未回设备断开。
    /// 模块ID：0x00断开 0x01复位 0x02血压模拟(0x30臂式自定义) 0x03压力测试(0漏气率/1过压释放/2压力表/3压力源)
    /// 0x05开排气阀 0x06关排气阀 0x07调零 0x09连接验证。
    /// 通讯仪式以控制台实测工程 NibpQuickTest 为唯一基准（启动序列/清场/放气回零/收敛判据/断开仪式）。
    /// 断开仪式 = 复位等回显（退出采样/模拟模式）→ 连发 3 次 0x00 归还本地控制，避免设备遗留采样态；
    /// 启动握手单轮快速失败：设备一旦被遗留在程控采样态（AA 洪泛、复位无回显），软件无法恢复，直接失败并提示断电重启。
    /// </summary>
    public sealed class KangwendaNIBPSimulator : IBloodPressureSimulator
    {
        #region 协议常量

        private const byte CmdDisconnect = 0x00;      // 断开连接
        private const byte CmdReset = 0x01;           // 初始化界面/复位
        private const byte FuncBpSimulation = 0x02;   // 血压模拟
        private const byte FuncPressureTest = 0x03;   // 压力测试
        private const byte CmdOpenVent = 0x05;        // 开启放气
        private const byte CmdCloseVent = 0x06;       // 关闭放气
        private const byte CmdZero = 0x07;            // 压力调零
        private const byte FuncHeartbeat = 0x09;      // 连接验证（双向均为 0x09）
        private const byte HeadFeedback = 0xAA;       // 压力回包帧头

        /// <summary>血压模拟模式：臂式自定义（帧内直接携带收缩压/舒张压/心率/脉搏量）</summary>
        private const byte ModeArmCustom = 0x30;

        /// <summary>AA 回包载荷长度（AA 之后 7 字节：压力高、压力低 + 5 字节填充）</summary>
        private const int AaPayloadLength = 7;

        /// <summary>等待模块号 ACK 回显的超时（毫秒），超时重发一包（同事实测参数）</summary>
        private static readonly TimeSpan AckTimeout = TimeSpan.FromMilliseconds(500);

        #endregion

        private readonly ILogService _log;
        private readonly object _writeLock = new();
        private SerialPort? _port;
        private CancellationTokenSource? _loopCts;
        private Task? _loopTask;

        // AA 帧组包状态（仅读循环线程访问）
        private readonly List<byte> _aaBuf = [];
        private bool _inAa;
        private DateTime _lastAaLog = DateTime.MinValue;

        // 最近一次收到设备任何字节（ACK/AA/心跳）的时刻：设备彻底静默时快速失败用，避免干等收敛超时
        private DateTime _lastRxUtc = DateTime.UtcNow;

        // ACK 等待（单飞：同一时刻只有一条命令在等回显）
        private TaskCompletionSource<byte>? _ackTcs;
        private byte _expectAck;
        private bool _disposed;

        // 后台泄放回路（慢释/快释阀脉宽调制，单放气阀合成双路阀行为）
        private CancellationTokenSource? _deflateCts;
        private Task? _deflateTask;

        /// <summary>袖带类型：0 外部袖带 / 1 内部袖带 / 2 内部新生儿（默认外部）。
        /// 注意：血压模拟/过压释放/漏气率等帧带外部袖带字节的命令，无真实袖带时设备
        /// 漏气/堵转保护会在 ≈50mmHg 中止加压；台架无袖带验证链路可用 1（内部袖带）。</summary>
        public byte CuffType { get; set; } = 0;

        /// <summary>脉搏量（0.1~2.4，血压模拟帧内 ×100 传输）</summary>
        public double PulseAmplitude { get; set; } = 1.0;

        /// <summary>压力反馈偏移：当前压力 = 原始值/100 − 本偏移（同事实测默认 10）</summary>
        public double FeedbackOffset { get; set; } = 10.0;

        /// <summary>最近一次 AA 回包解析出的压力（mmHg）</summary>
        public double LastPressure { get; private set; }

        /// <summary>最近一次静态压力稳压后的实测值（接口成员，BP-006 读回判定用）</summary>
        public double LastStaticPressure { get; private set; }

        /// <summary>收敛判定保持窗内的回读样本（约 100ms 一个）：稳压值取该窗均值，即"稳定 2.5s 后读回"</summary>
        public IReadOnlyList<double> LastStableSamples => _stableSamples;
        private readonly List<double> _stableSamples = new();

        /// <summary>慢释阀合成匀速放气的目标速率（mmHg/s，规格 2~6 取中值，脉宽自适应逼近）</summary>
        private const double SlowDeflateRate = 4.0;

        public bool IsOpen => _port is { IsOpen: true };

        /// <summary>压力采样事件：由 AA 回包流驱动（压力表/压力源/血压模拟模式下设备持续回传）</summary>
        public event Action<double>? PressureSampled;

        /// <summary>序列模式下每个压力点收敛到位（进入稳压保持窗）时触发：index=点序号，pressure=当前回读值；BP-003 用于保持窗内回读产品上报值</summary>
        public event Action<int, double>? PressurePointStable;

        /// <param name="portName">串口号，如 "COM5"</param>
        /// <param name="baudRate">波特率（康伟达真机为 115200）</param>
        public KangwendaNIBPSimulator(string portName, int baudRate = 115200, ILogService? log = null)
        {
            _log = log ?? new LogService(nameof(KangwendaNIBPSimulator));
            PortName = portName;
            BaudRate = baudRate;
        }

        private string PortName { get; }
        private int BaudRate { get; }

        // ==================== 连接管理 ====================

        /// <summary>打开串口并启动读循环，按控制台实测序列初始化：
        /// 0x00 进入 PC 控制（裸发不等应答，设备不回显 0x00）→ 等 1.5s → 0x01 复位（等回显，无应答判 FAIL）→ 0x07 调零（裸发）。
        /// 复位无应答多为设备被上次测试遗留在程控采样态（AA 帧洪泛、命令通道不应答），实测软件重试/探测均无法恢复，
        /// 故单轮快速失败（不重试），避免长时间卡住与停止无响应，直接提示断电重启。</summary>
        public async Task OpenAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(PortName))
                throw new InvalidOperationException("康伟达模拟仪串口未配置。");
            if (IsOpen) return;

            var port = new SerialPort(PortName.Trim(), BaudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 80,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true
            };
            SerialPortOpener.Open(port);   // 限时打开：原生 Open() 无超时，端口异常时会无限期挂起卡死整个方案
            _port = port;
            _inAa = false;
            _aaBuf.Clear();
            _loopCts = new CancellationTokenSource();
            _loopTask = Task.Run(() => ReadLoop(_loopCts.Token), CancellationToken.None);

            // 实测启动序列：裸发 0x00 把设备切入 PC 控制（设备不回显 0x00）→ 等 1.5s → 0x01 复位等回显（SendWithAckCheck 内置重发一次）
            _port.DiscardInBuffer();               // 清掉设备遗留采样态的洪泛残留字节，避免读循环组包错位
            WriteRaw([CmdDisconnect]);
            await Task.Delay(1500, ct).ConfigureAwait(false);
            if (!await SendWithAckCheckAsync(new[] { CmdReset }, CmdReset, ct).ConfigureAwait(false))
            {
                await CloseAsync().ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"康伟达模拟仪对复位(0x01)无应答（{PortName} {BaudRate}bps）。设备很可能被上次测试遗留在程控采样态（软件无法恢复），请断电重启模拟仪后重试；并检查串口线路、波特率（实测 115200）与是否被其他程序占用。");
            }
            WriteRaw([CmdZero]);                                 // 调零（实测：回显为 00 而非 07，不作应答结算）
            await Task.Delay(1000, ct).ConfigureAwait(false);    // 实测调零后设备回 00 耗时约 1s，等它走完再进测试序列
            WriteRaw([CmdCloseVent]);                            // 关阀待机（实测仪式：功能帧前阀必须处于关闭状态）
            await Task.Delay(150, ct).ConfigureAwait(false);
            _lastRxUtc = DateTime.UtcNow;
            _log.Info($"康伟达模拟仪：已打开 {PortName} {BaudRate}，复位应答正常，已调零");
        }

        /// <summary>发送并等待回显，超时重发一次；两次都无应答返回 false</summary>
        private async Task<bool> SendWithAckCheckAsync(byte[] data, byte ack, CancellationToken ct)
        {
            for (var attempt = 0; attempt < 2; attempt++)
                if (await TrySendAndWaitAckAsync(data, ack, ct).ConfigureAwait(false))
                    return true;
            return false;
        }

        /// <summary>
        /// 连接活性校验（共享会话下 M01 用）：重发复位并等待回显，两次无应答抛异常。
        /// 注：复位会使设备回到待命界面，对后续模块无影响（各模块首条指令自带模式切换）。
        /// </summary>
        public async Task VerifyAliveAsync(CancellationToken ct = default)
        {
            if (!IsOpen)
                throw new InvalidOperationException("康伟达模拟仪串口未打开。");
            if (!await SendWithAckCheckAsync(new[] { CmdReset }, CmdReset, ct).ConfigureAwait(false))
                throw new InvalidOperationException($"康伟达模拟仪连接活性校验失败：对复位(0x01)无应答（{PortName} {BaudRate}bps）");
            _log.Info("康伟达模拟仪：连接活性校验通过（复位应答正常）");
        }

        /// <summary>
        /// 发送断开连接指令（0x00）后关闭串口与读循环。
        /// 断开仪式（NibpQuickTest 实测）：连发 3 次 00 各隔 150ms，确保设备可靠退出连接；
        /// 仪式前先复位并等回显——M06 等模块结束时设备常停留在压力表模式（StopSampling 空操作，协议无停止命令），
        /// 实测此时裸发 0x00 不可靠，设备遗留在程控采样态会 AA 帧洪泛且不再应答任何命令，下一会话只能断电重启。
        /// </summary>
        public async Task CloseAsync()
        {
            await StopDeflateLoopAsync().ConfigureAwait(false);

            try
            {
                if (IsOpen)
                {
                    // 先复位退出采样/模拟模式（等回显确认命令通道有效），再走断开仪式归还本地控制
                    if (!await SendWithAckCheckAsync(new[] { CmdReset }, CmdReset, CancellationToken.None).ConfigureAwait(false))
                        _log.Warn("康伟达模拟仪：关闭前复位无应答，直接执行断开仪式（设备可能遗留在程控模式，若下次连接失败请断电重启模拟仪）");
                    for (var i = 0; i < 3; i++)
                    {
                        WriteRaw([CmdDisconnect]);
                        await Task.Delay(150).ConfigureAwait(false);
                    }
                }
            }
            catch { /* 端口已失效时直接释放 */ }

            try
            {
                _loopCts?.Cancel();
                if (_loopTask != null)
                    await Task.WhenAny(_loopTask, Task.Delay(500)).ConfigureAwait(false);
            }
            catch { /* 关闭路径忽略读循环异常 */ }

            _loopCts?.Dispose();
            _loopCts = null;
            _loopTask = null;

            try { _port?.Close(); }
            catch { /* 端口已失效时直接释放 */ }

            _port?.Dispose();
            _port = null;
        }

        // ==================== 核心指令 ====================

        /// <summary>初始化 / 复位（0x01），设备回到待命界面</summary>
        public Task ResetAsync(CancellationToken ct = default)
        {
            WriteRaw([CmdReset]);
            _log.Info("康伟达模拟仪：已发送复位指令");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 开始血压模拟（0x02 + 0x30 臂式自定义模式）：
        /// [0x02, 0x30, 袖带, 收缩压高, 收缩压低, 舒张压高, 舒张压低, 心率高, 心率低, 脉搏量×100]，
        /// 模拟过程中设备持续回传 AA 帧（当前测量压力），经 PressureSampled 上抛作波形。
        /// </summary>
        public async Task StartSimulationAsync(int systolic, int diastolic, int heartRate, CancellationToken ct = default)
        {
            var sys = ToU16(systolic);
            var dia = ToU16(diastolic);
            var hr = ToU16(heartRate);
            var pulse = (byte)Math.Clamp(Math.Round(PulseAmplitude * 100), 10, 240);
            var frame = new byte[] { FuncBpSimulation, ModeArmCustom, CuffType,
                (byte)(sys >> 8), (byte)sys, (byte)(dia >> 8), (byte)dia,
                (byte)(hr >> 8), (byte)hr, pulse };
            _log.Info($"康伟达模拟仪：血压模拟 SYS={systolic} DIA={diastolic} HR={heartRate} 脉搏量={PulseAmplitude:0.0}");
            await SendWithRetryAsync(frame, FuncBpSimulation, ct).ConfigureAwait(false);
        }

        /// <summary>停止模拟（协议无专用停止命令，发送复位使设备回到待命）</summary>
        public Task StopSimulationAsync(CancellationToken ct = default)
        {
            _log.Info("康伟达模拟仪：停止模拟（复位）");
            return ResetAsync(ct);
        }

        /// <summary>
        /// 静态压力（压力测试模块 3 功能 3 压力源）：先执行清场仪式（控制台实测：缺步设备不应答 0x03 帧），
        /// 再设定目标压力并等待 AA 回读收敛（|回读-目标|&lt;1.5mmHg 连续保持 2.5s，控制台实测判据，稳压值取保持窗回读均值），
        /// 设备将保持该压力直到下一条命令。收敛超时随目标压力放大（≤100→20s、≤150→30s、250→45s），超时记警告不抛异常。
        /// </summary>
        public Task SetStaticPressureAsync(double pressure, int stableSeconds = 5, CancellationToken ct = default)
            => SetStaticPressureCoreAsync(pressure, stableSeconds, timeoutSeconds: null, throwOnTimeout: false, ct);

        /// <summary>
        /// 接口成员（BP-006~008 步骤用）：inflation 无意义（气路始终由设备自身打气）；
        /// 超时抛 TimeoutException（与 BPA700 行为一致，BP-008 依赖它重试）。
        /// </summary>
        public Task SetStaticPressureAsync(double pressure, int stableSeconds, bool inflation, CancellationToken ct, int timeoutSeconds = 180)
            => SetStaticPressureCoreAsync(pressure, stableSeconds, timeoutSeconds, throwOnTimeout: true, ct);

        private async Task SetStaticPressureCoreAsync(double pressure, int stableSeconds, int? timeoutSeconds, bool throwOnTimeout, CancellationToken ct)
        {
            // 实测仪式：功能帧前必须清场（06→压力表→泄残压→01→07→06），否则设备不应答 0x03 帧
            await CleanStateAsync(ct).ConfigureAwait(false);

            var p = ToU16(pressure);
            var frame = BuildPressureTestFrame(3, p, 0, 0, 0);
            _log.Info($"康伟达模拟仪：设定压力源 {pressure:0.#} mmHg（报文 {FormatFrame(frame)}）");
            await SendWithRetryAsync(frame, FuncPressureTest, ct).ConfigureAwait(false);

            // 等待压力收敛：|回读-目标| < 1.5mmHg 连续保持 2.5s（控制台实测判据），保持窗内回读取均值作稳压值
            const double tolerance = 1.5;
            const int stableHoldMs = 2500;
            _stableSamples.Clear();
            // 收敛超时映射（NibpQuickTest 基准）：目标 ≤100→20s、≤150→30s、其余→45s
            var timeout = timeoutSeconds ?? (pressure <= 100 ? 20 : pressure <= 150 ? 30 : 45);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(timeout);
            DateTime stableSince = DateTime.MaxValue;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                // 快速失败：设备超过 8s 无任何回传（无 ACK/AA/心跳），不再干等收敛超时造成"卡死"观感
                if ((DateTime.UtcNow - _lastRxUtc).TotalSeconds > 8)
                {
                    LastStaticPressure = LastPressure;
                    var dead = "康伟达模拟仪：设备无任何回传（无 ACK、无 AA 帧），已中止本点。请断电重启设备并确认气路连接后重试。";
                    if (throwOnTimeout)
                        throw new TimeoutException(dead);
                    _log.Warn(dead);
                    return;
                }

                if (Math.Abs(LastPressure - pressure) < tolerance)
                {
                    if (stableSince == DateTime.MaxValue)
                    {
                        stableSince = DateTime.UtcNow;
                        _stableSamples.Clear();
                    }
                    _stableSamples.Add(LastPressure);
                    if ((DateTime.UtcNow - stableSince).TotalMilliseconds >= stableHoldMs)
                    {
                        LastStaticPressure = _stableSamples.Count > 0 ? _stableSamples.Average() : LastPressure;
                        _log.Info($"康伟达模拟仪：已稳压至目标附近，保持窗 {_stableSamples.Count} 次回读均值 {LastStaticPressure:0.0} mmHg");
                        return;
                    }
                }
                else stableSince = DateTime.MaxValue;
                await Task.Delay(100, ct).ConfigureAwait(false);
            }

            LastStaticPressure = LastPressure;
            var msg = $"康伟达模拟仪：稳压等待超时（{timeout}s），当前回读 {LastPressure:0.0} mmHg（目标 {pressure:0.#}）。"
                + (CuffType == 0 ? "若为外部袖带通道，请确认已接真实袖带（无袖带时设备漏气保护会中止加压）。" : "");
            if (throwOnTimeout)
                throw new TimeoutException(msg);
            _log.Warn(msg);
        }

        /// <summary>
        /// 接口成员：只发起不等稳压（BP-007/008 使阀状态生效用）。
        /// 康伟达无"阀状态在下次测试才生效"的 SDK 语义：inflation=false 为空操作
        /// （泄放已由 SetSlowValveAsync/SetFastValveAsync 的后台回路执行）；true 则只发压力源帧。
        /// </summary>
        public async Task StartStaticPressureAsync(double pressure, int stableSeconds, bool inflation, CancellationToken ct = default)
        {
            if (!inflation)
            {
                _log.Info("康伟达模拟仪：StartStaticPressure(inflation=false) 空操作（泄放由阀控后台回路执行）");
                return;
            }
            await CleanStateAsync(ct).ConfigureAwait(false);   // 实测仪式：清场后设备才应答压力源帧
            var frame = BuildPressureTestFrame(3, ToU16(pressure), 0, 0, 0);
            _log.Info($"康伟达模拟仪：发起压力源 {pressure:0.#} mmHg（不等稳压）");
            await SendWithRetryAsync(frame, FuncPressureTest, ct).ConfigureAwait(false);
        }

        /// <summary>接口成员：康伟达无异步测试态，SetStaticPressureAsync 返回即已到位，空操作</summary>
        public Task WaitStaticPressureFinishAsync(int timeoutMs = 60_000, CancellationToken ct = default)
            => Task.CompletedTask;

        /// <summary>
        /// 逐点执行静态压力序列（BP-003）：每点清场仪式（自动泄残压+复位调零）→压力源加压→AA 收敛
        /// （收敛判据含 2.5s 稳压并保持窗取均值作该点标准值，不再额外保持等待）。
        /// inflation/stableSeconds 参数对康伟达无意义（气路始终由设备自身打气，保持窗由收敛判据定型；stableSeconds 仅 BPA700 有效）。
        /// </summary>
        public async Task<double[]> RunStaticPressureSequenceAsync(double[] points, int stableSeconds, bool inflation, CancellationToken ct = default)
        {
            if (points is not { Length: > 0 })
                throw new ArgumentException("压力点不能为空", nameof(points));

            var results = new double[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                _log.Info($"康伟达模拟仪：静态压力序列 点{i + 1}/{points.Length} → {points[i]:0.#} mmHg");
                await SetStaticPressureAsync(points[i], stableSeconds, ct).ConfigureAwait(false);

                // 收敛到位即触发逐点稳定事件：上层并发回读产品上报值（不阻塞本序列）
                PressurePointStable?.Invoke(i, LastStaticPressure);

                // 该点标准值 = 收敛保持窗回读均值（到位后总稳压约 2.5~3.5s，不再叠加 MeasureSeconds 额外保持）
                results[i] = LastStaticPressure;
                _log.Info($"康伟达模拟仪：点 {points[i]:0.#} 稳压实测均值 {results[i]:0.0} mmHg");

                // 点间无需单独放气：下一点 SetStaticPressureAsync 的清场仪式会泄残压并重新调零（控制台实测同款流程）
            }
            return results;
        }

        /// <summary>
        /// 压力表模式（功能 2）：设备持续回传当前压力 AA 帧，作为 PressureSampled 波形数据源。
        /// </summary>
        public async Task StartSamplingAsync(CancellationToken ct = default)
        {
            await CleanStateAsync(ct).ConfigureAwait(false);   // 实测仪式：清场后设备才应答压力表帧并恢复 AA 流
            var frame = BuildPressureTestFrame(2, 0, 0, 0, 0);
            _log.Info("康伟达模拟仪：进入压力表模式（压力采样）");
            await SendWithRetryAsync(frame, FuncPressureTest, ct).ConfigureAwait(false);
        }

        /// <summary>退出采样（协议无停止命令，下一条指令自然覆盖压力表模式）</summary>
        public Task StopSamplingAsync()
        {
            _log.Info("康伟达模拟仪：采样结束（等待后续指令覆盖）");
            return Task.CompletedTask;
        }

        /// <summary>手动发送一次心跳应答（读循环已自动应答 0x09，此方法仅为接口/保活补发用）</summary>
        public Task RespondToHeartbeatAsync(CancellationToken ct = default)
        {
            WriteRaw([FuncHeartbeat]);
            _log.Debug("康伟达模拟仪：已发送心跳应答（0x09）");
            return Task.CompletedTask;
        }

        // ==================== 扩展指令（压力测试模块 / 阀控，供后续接口化使用） ====================

        /// <summary>
        /// 压力测试模块 3 通用发送：function 0漏气率检测 / 1过压释放 / 2压力表 / 3压力源。
        /// 帧格式见协议《上位机发送》表；结果经 AA 回包解析。
        /// </summary>
        public async Task SendPressureTestAsync(byte function, ushort pressure, byte stabilizeSeconds,
            ushort durationSeconds, byte sensitivity, CancellationToken ct = default)
        {
            var frame = BuildPressureTestFrame(function, pressure, stabilizeSeconds, durationSeconds, sensitivity);
            _log.Info($"康伟达模拟仪：{PressureTestName(function)} 袖带{CuffType} 压力{pressure}（报文 {FormatFrame(frame)}）");
            await SendWithRetryAsync(frame, FuncPressureTest, ct).ConfigureAwait(false);
        }

        /// <summary>按协议构造压力测试模块 3 的帧（同事实测帧格式，袖带取 CuffType）</summary>
        public byte[] BuildPressureTestFrame(byte function, ushort pressure,
            byte stabilizeSeconds, ushort durationSeconds, byte sensitivity)
        {
            function = Math.Clamp(function, (byte)0, (byte)3);
            var cuff = Math.Min(CuffType, (byte)2);
            var ph = (byte)(pressure >> 8);
            var pl = (byte)pressure;
            return function switch
            {
                0 => [0x03, 0x00, cuff, ph, pl, stabilizeSeconds, (byte)(durationSeconds >> 8), (byte)durationSeconds], // 漏气率：袖带+设定压力+稳压时间+测量时长
                1 => [0x03, 0x01, cuff, ph, pl, sensitivity],   // 过压释放：袖带+设定压力+测压灵敏度
                2 => [0x03, 0x02, cuff],                        // 压力表：持续回传当前压力
                _ => [0x03, 0x03, cuff, ph, pl],                // 压力源：设定恒定压力
            };
        }

        public static string PressureTestName(byte function) => function switch
        {
            0 => "漏气率检测",
            1 => "过压释放",
            2 => "压力表",
            3 => "压力源",
            _ => $"功能{function}"
        };

        /// <summary>打开放气阀（0x05）</summary>
        public Task OpenVentAsync(CancellationToken ct = default)
        {
            WriteRaw([CmdOpenVent]);
            _log.Info("康伟达模拟仪：打开放气阀");
            return Task.CompletedTask;
        }

        /// <summary>关闭放气阀（0x06）</summary>
        public Task CloseVentAsync(CancellationToken ct = default)
        {
            WriteRaw([CmdCloseVent]);
            _log.Info("康伟达模拟仪：关闭放气阀");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 连续放气回零（NibpQuickTest 基准仪式）：05 开阀泄放（泄放期间补发压力表帧维持 AA 回读，实测开阀会中断 AA 流），
        /// 回读 &lt;5mmHg 提前返回后 06 关阀，返回是否成功回零。
        /// </summary>
        public async Task<bool> VentToZeroAsync(int timeoutSeconds = 60, CancellationToken ct = default)
        {
            WriteRaw(BuildPressureTestFrame(2, 0, 0, 0, 0));   // 切压力表，AA 持续回传当前压力
            await Task.Delay(800, ct).ConfigureAwait(false);

            if (await VentToBaselineAsync(Math.Max(5, timeoutSeconds), ct).ConfigureAwait(false))
            {
                _log.Info($"康伟达模拟仪：已放气回零（回读 {LastPressure:0.0}）");
                return true;
            }

            _log.Warn($"康伟达模拟仪：放气回零超时，残压 {LastPressure:0.0} mmHg");
            return false;
        }

        /// <summary>
        /// 05 开阀泄放并等回读归零：实测开阀后设备中断 AA 回传，干等基线会等满上限（点间卡死真因），
        /// 故泄放期间周期补发压力表帧维持回读；&lt;5mmHg 提前返回，等待上限按残压 3~capSeconds 秒缩放，最后 06 关阀。
        /// </summary>
        private async Task<bool> VentToBaselineAsync(double capSeconds, CancellationToken ct)
        {
            WriteRaw([CmdOpenVent]);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Clamp(LastPressure / 10, 3, capSeconds));
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (LastPressure < 5)
                    break;
                WriteRaw(BuildPressureTestFrame(2, 0, 0, 0, 0));   // 补发压力表帧，AA 流未断时即可看到压力下降
                await Task.Delay(600, ct).ConfigureAwait(false);
            }
            WriteRaw([CmdCloseVent]);
            await Task.Delay(200, ct).ConfigureAwait(false);
            return LastPressure < 5;
        }

        /// <summary>压力调零（0x07）</summary>
        public Task ZeroAsync(CancellationToken ct = default)
        {
            WriteRaw([CmdZero]);
            _log.Info("康伟达模拟仪：压力调零");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 功能帧前的清场仪式（NibpQuickTest 实测定型序列，缺步设备不应答 0x03/0x02 功能帧）：
        /// 06 关阀 → 压力表帧（拿 ACK 并恢复 AA 流）→ 残压 &gt;10 则连续放气归零 → 01 复位 → 07 调零 → 06 关阀。
        /// </summary>
        private async Task CleanStateAsync(CancellationToken ct)
        {
            WriteRaw([CmdCloseVent]);
            await Task.Delay(150, ct).ConfigureAwait(false);
            await SendWithRetryAsync(BuildPressureTestFrame(2, 0, 0, 0, 0), FuncPressureTest, ct).ConfigureAwait(false);
            await Task.Delay(800, ct).ConfigureAwait(false);
            if (LastPressure > 10)
            {
                // 残压泄放：开阀期间 AA 回传中断，不能干等基线（曾致点间卡死 30s）；
                // VentToBaseline 内补发压力表帧维持回读、归零提前返回、上限按残压缩放
                _log.Info($"康伟达模拟仪：清场泄放残压（当前 {LastPressure:0.0} mmHg）");
                if (!await VentToBaselineAsync(15, ct).ConfigureAwait(false))
                    _log.Warn($"康伟达模拟仪：泄放后残压未确认归零（回读 {LastPressure:0.0} mmHg），继续后续调零流程");
            }
            WriteRaw([CmdReset]);
            await Task.Delay(300, ct).ConfigureAwait(false);
            WriteRaw([CmdZero]);
            await Task.Delay(300, ct).ConfigureAwait(false);
            WriteRaw([CmdCloseVent]);
            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        // ==================== 接口成员：血压测量/过压/阀控（曲线判定兼容实现） ====================

        /// <summary>
        /// 一轮完整血压测量（BP-001/002/004/005）：发 0x02 血压模拟帧后监测 AA 压力曲线，
        /// 把 BPA700 回调状态机等价映射为曲线阶段：
        /// Wait(1)=压力低位等产品充气；Busy(2)=产品开始充气（压力≥30）；
        /// Stable(3)=2s 滑窗内峰峰值≥3mmHg（设备叠加脉搏波，产品测量中）；
        /// Finish(4)=Stable 后泄压至 15 以下（测量完成）；
        /// Idle(0)=Busy 后未进入 Stable 即骤降（充气中手动停止，BP-002 判定），
        /// 放气时间 = 峰值时刻→压力降至 10 以下。
        /// </summary>
        public async Task<BpTestRunResult> RunBpMeasurementAsync(
            int systolic, int diastolic, int heartRate,
            int waitStartTimeoutMs = 30_000, int totalTimeoutMs = 60_000,
            CancellationToken ct = default)
        {
            if (!IsOpen)
                throw new InvalidOperationException("康伟达模拟仪串口未打开。");

            await CleanStateAsync(ct).ConfigureAwait(false);   // 实测仪式：0x02 功能帧同样要先清场
            await StartSimulationAsync(systolic, diastolic, heartRate, ct).ConfigureAwait(false);
            _log.Info($"康伟达模拟仪：血压测量轮开始（曲线判定），等待产品启动充气（最长 {waitStartTimeoutMs / 1000}s）...");

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var seq = new List<int> { 1 };      // 从 Wait 开始
            var phase = 1;                       // 1=Wait 2=Busy 3=Stable 4=Finish 0=Idle
            double peak = 0, peakT = 0, deflateEndT = -1;
            var win = new Queue<(double T, double P)>();   // 2s 滑动窗口，脉搏波检测用
            var waitDeadline = TimeSpan.FromMilliseconds(waitStartTimeoutMs);
            var totalDeadline = TimeSpan.FromMilliseconds(totalTimeoutMs);

            while (watch.Elapsed < totalDeadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(100, ct).ConfigureAwait(false);
                var t = watch.Elapsed.TotalSeconds;
                var p = LastPressure;

                win.Enqueue((t, p));
                while (win.Count > 0 && t - win.Peek().T > 2.0) win.Dequeue();
                if (p > peak) { peak = p; peakT = t; }

                switch (phase)
                {
                    case 1: // Wait：等产品充气把压力拉起来
                        if (p >= 30)
                        {
                            phase = 2; seq.Add(2);
                            _log.Info($"康伟达模拟仪：曲线判定 Wait→Busy（产品开始充气，{p:0.0} mmHg @ {t:0.0}s）");
                        }
                        break;
                    case 2: // Busy：脉搏波平台→Stable；充气中骤降→Idle（产品手动停止）
                        var pp = win.Count >= 4 ? win.Max(x => x.P) - win.Min(x => x.P) : 0;
                        if (pp >= 3.0)
                        {
                            phase = 3; seq.Add(3);
                            _log.Info($"康伟达模拟仪：曲线判定 Busy→Stable（检测到脉搏波，峰峰 {pp:0.0} mmHg @ {t:0.0}s）");
                        }
                        else if (p < peak - 20 && p < 40)
                        {
                            phase = 0; seq.Add(0);
                            _log.Info($"康伟达模拟仪：曲线判定 Busy→Idle（充气中骤降，峰值 {peak:0.0}→{p:0.0} mmHg，产品手动停止）");
                        }
                        break;
                    case 3: // Stable：泄压归零→Finish
                        if (p < 15)
                        {
                            phase = 4; seq.Add(4);
                            _log.Info($"康伟达模拟仪：曲线判定 Stable→Finish（测量完成泄压，{p:0.0} mmHg @ {t:0.0}s）");
                        }
                        break;
                }

                if (phase is 4 or 0)
                {
                    if (p < 10 && deflateEndT < 0) deflateEndT = t;
                    if (deflateEndT > 0 && t - deflateEndT > 2) break;   // 归零后再观察 2s 结束
                }
                else if (phase == 1 && watch.Elapsed > waitDeadline)
                {
                    _log.Warn("康伟达模拟仪：等待产品启动充气超时（产品未启动或气路未连通）");
                    break;
                }
            }

            var totalTime = watch.Elapsed.TotalSeconds;
            var deflation = deflateEndT > peakT ? deflateEndT - peakT : 0;
            await StopSimulationAsync(ct).ConfigureAwait(false);   // 复位回待命，不影响后续模块
            _log.Info($"康伟达模拟仪：血压测量轮结束 状态序列[{string.Join("→", seq)}] 峰值={peak:0.0} 放气时间={deflation:0.0}s 总时长={totalTime:0.0}s");
            return new BpTestRunResult(phase, seq, peak, peakT, deflation, totalTime);
        }

        /// <summary>
        /// 过压保护测试（BP-009）：压力源充向目标压力（高于 threshold2），产品触发保护自行泄压后，
        /// AA 曲线表现为峰值后骤降（降 ≥15mmHg 且 300ms 内不回弹），触发压力 = 峰值；
        /// 超时未触发返回最高峰值（超出 threshold2 自然判 FAIL）。
        /// </summary>
        public async Task<double> RunOverpressureTestAsync(
            double pressure, double threshold1, double threshold2,
            bool inflation = true, int timeoutMs = 120_000, CancellationToken ct = default)
        {
            if (!IsOpen)
                throw new InvalidOperationException("康伟达模拟仪串口未打开。");

            await CleanStateAsync(ct).ConfigureAwait(false);   // 实测仪式：功能帧前先清场
            var frame = BuildPressureTestFrame(3, ToU16(pressure), 0, 0, 0);
            _log.Info($"康伟达模拟仪：过压测试充气至 {pressure:0.#} mmHg，监测产品保护动作（阈值 {threshold1:0.#}~{threshold2:0.#}）");
            await SendWithRetryAsync(frame, FuncPressureTest, ct).ConfigureAwait(false);

            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
            double peak = 0;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(150, ct).ConfigureAwait(false);
                var p = LastPressure;
                if (p > peak) peak = p;

                // 峰值已过阈值且骤降：疑似产品保护泄压，再确认不回弹（排除设备补压波动）
                if (peak >= threshold1 && p < peak - 15)
                {
                    await Task.Delay(300, ct).ConfigureAwait(false);
                    if (LastPressure < peak - 10)
                    {
                        _log.Info($"康伟达模拟仪：过压触发确认，触发压力（峰值）= {peak:0.0} mmHg");
                        return peak;
                    }
                }
            }
            _log.Warn($"康伟达模拟仪：过压测试超时（{timeoutMs / 1000}s）未监测到产品保护泄压，返回最高峰值 {peak:0.0} mmHg");
            return peak;
        }

        /// <summary>
        /// 接口成员：慢释阀。康伟达只有一个放气阀（全开速率远超 2~6mmHg/s 规格），
        /// 用脉宽调制合成匀速泄放：后台回路开阀短脉冲→关阀→切压力表读压（AA 上抛采样），
        /// 脉宽按实测降幅自适应逼近 SlowDeflateRate。
        /// </summary>
        public Task SetSlowValveAsync(bool open, CancellationToken ct = default)
            => open ? StartDeflateLoopAsync(slow: true) : StopDeflateLoopAsync();

        /// <summary>
        /// 接口成员：快释阀。单阀全开连续放气（NibpQuickTest 实测：05 放气期间 AA 持续回传实时压力），
        /// 后台回路只监控归零后关阀，采样持续上抛供 BP-008 计时。
        /// </summary>
        public Task SetFastValveAsync(bool open, CancellationToken ct = default)
            => open ? StartDeflateLoopAsync(slow: false) : StopDeflateLoopAsync();

        private async Task StartDeflateLoopAsync(bool slow)
        {
            await StopDeflateLoopAsync().ConfigureAwait(false);
            var cts = new CancellationTokenSource();
            _deflateCts = cts;
            _deflateTask = Task.Run(() => DeflateLoopAsync(slow, cts.Token), CancellationToken.None);
            _log.Info($"康伟达模拟仪：{(slow ? $"慢释泄放回路已启动（脉宽调制，目标 {SlowDeflateRate:0.#} mmHg/s）" : "快释泄放回路已启动（阀全开连续放气）")}");
        }

        private async Task StopDeflateLoopAsync()
        {
            var cts = _deflateCts;
            var task = _deflateTask;
            _deflateCts = null;
            _deflateTask = null;
            if (cts == null) return;
            try
            {
                cts.Cancel();
                if (task != null)
                    await Task.WhenAny(task, Task.Delay(3000)).ConfigureAwait(false);
            }
            catch { /* 泄放回路收尾忽略 */ }
            finally { cts.Dispose(); }
            try { if (IsOpen) WriteRaw([CmdCloseVent]); } catch { /* 端口已失效 */ }
        }

        /// <summary>
        /// 泄放回路主体：慢释=开阀短脉冲→关阀→压力表读压（AA 自动上抛 PressureSampled），脉宽自适应；
        /// 快释=阀全开连续放气（AA 放气期间持续回传），归零后关阀待机。
        /// </summary>
        private async Task DeflateLoopAsync(bool slow, CancellationToken ct)
        {
            var pulseMs = 250;   // 仅慢释使用
            var prevP = LastPressure;
            var prevT = DateTime.UtcNow;
            var lastLog = DateTime.MinValue;

            try
            {
                if (!slow)
                {
                    // 快释：05 全开连续放气，AA 持续回传实时压力供上层采样计时，归零后关阀待机
                    WriteRaw([CmdOpenVent]);
                    while (!ct.IsCancellationRequested && IsOpen)
                    {
                        await Task.Delay(100, ct).ConfigureAwait(false);
                        if (LastPressure < 5)
                        {
                            WriteRaw([CmdCloseVent]);
                            _log.Debug("康伟达模拟仪：快释归零，关阀待机");
                            break;
                        }
                    }
                    while (!ct.IsCancellationRequested && IsOpen)
                        await Task.Delay(200, ct).ConfigureAwait(false);
                    return;
                }

                while (!ct.IsCancellationRequested && IsOpen)
                {
                    WriteRaw([CmdOpenVent]);
                    await Task.Delay(pulseMs, ct).ConfigureAwait(false);
                    WriteRaw([CmdCloseVent]);
                    WriteRaw(BuildPressureTestFrame(2, 0, 0, 0, 0));   // 切压力表，等 AA 首帧回传
                    await Task.Delay(450, ct).ConfigureAwait(false);

                    var now = DateTime.UtcNow;
                    var p = LastPressure;
                    var cycleSec = (now - prevT).TotalSeconds;

                    // 脉宽自适应：本周期实际降幅 vs 目标降幅（SlowDeflateRate×周期），比例修正脉宽
                    if (cycleSec > 0.2)
                    {
                        var drop = prevP - p;
                        if (drop > 0.3 && p > 5)
                        {
                            var targetDrop = SlowDeflateRate * cycleSec;
                            pulseMs = (int)Math.Clamp(pulseMs * targetDrop / drop, 40, 2500);
                        }
                    }
                    prevP = p;
                    prevT = now;

                    if ((now - lastLog).TotalSeconds >= 2)
                    {
                        lastLog = now;
                        _log.Debug($"康伟达模拟仪：慢释泄放中 回读 {p:0.0} mmHg 脉宽 {pulseMs}ms");
                    }

                    if (p < 5)
                        await Task.Delay(500, ct).ConfigureAwait(false);   // 已归零：关阀待机，等上层停止
                }
            }
            catch (OperationCanceledException) { /* 正常停止 */ }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    _log.Warn($"康伟达模拟仪：泄放回路异常退出（{ex.Message}）");
            }
        }

        // ==================== 发送与应答 ====================

        /// <summary>发送命令并等待模块号回显 ACK；500ms 无应答重发一包（同事实测策略）</summary>
        private async Task SendWithRetryAsync(byte[] data, byte ack, CancellationToken ct)
        {
            if (await TrySendAndWaitAckAsync(data, ack, ct).ConfigureAwait(false))
                return;

            _log.Warn("康伟达模拟仪：命令无应答，重发一包。");
            if (!await TrySendAndWaitAckAsync(data, ack, ct).ConfigureAwait(false))
                _log.Warn("康伟达模拟仪：重发后仍无应答（设备未连接或未上电？）。");
        }

        private async Task<bool> TrySendAndWaitAckAsync(byte[] data, byte ack, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_writeLock)
            {
                _expectAck = ack;
                _ackTcs = tcs;
                WriteRawLocked(data);
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(AckTimeout);
            await using var reg = linked.Token.Register(() => tcs.TrySetCanceled());
            try
            {
                await tcs.Task.ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                lock (_writeLock)
                {
                    if (ReferenceEquals(_ackTcs, tcs))
                        _ackTcs = null;
                }
            }
        }

        private void TryCompleteAck(byte b)
        {
            TaskCompletionSource<byte>? tcs;
            lock (_writeLock)
            {
                tcs = _ackTcs;
                if (tcs == null || b != _expectAck)
                    return;
                _ackTcs = null;
            }
            tcs.TrySetResult(b);
        }

        private void WriteRaw(byte[] data)
        {
            lock (_writeLock)
            {
                WriteRawLocked(data);
            }
        }

        private void WriteRawLocked(byte[] data)
        {
            _log.Debug($"康伟达模拟仪 TX: {FormatFrame(data)}");
            if (_port is not { IsOpen: true })
                throw new InvalidOperationException("康伟达模拟仪串口未打开。");
            _port.Write(data, 0, data.Length);
        }

        // ==================== 接收解析（读循环） ====================

        private void ReadLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var port = _port;
                    if (port is not { IsOpen: true })
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    // 轮询读：线程不得停留在阻塞 ReadByte 里——实测并发 Close() 会等待挂起的读操作而永久阻塞，
                    // 导致关闭/停止链卡死、界面无响应；有字节才读，ReadByte 立即返回
                    if (port.BytesToRead == 0)
                    {
                        // 帧间空闲：AA 帧已收满 2 字节以上则按截断帧结算，避免最后一帧滞留
                        if (_inAa && _aaBuf.Count >= 2)
                            FinishAaFrame();
                        Thread.Sleep(20);
                        continue;
                    }

                    ProcessByte((byte)port.ReadByte());
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        _log.Error("康伟达模拟仪读取异常", ex);
                        Thread.Sleep(200);
                    }
                }
            }
        }

        private void ProcessByte(byte b)
        {
            _lastRxUtc = DateTime.UtcNow;   // 任何设备回传（ACK/AA/心跳）都刷新活性时刻
            if (_inAa)
            {
                _aaBuf.Add(b);
                if (_aaBuf.Count >= AaPayloadLength)
                    FinishAaFrame();
                return;
            }

            switch (b)
            {
                case FuncHeartbeat:
                    // 下位机心跳询问：7 秒内原样回复 0x09，否则设备断开
                    _log.Debug("康伟达模拟仪 RX: 09（心跳询问，已应答）");
                    WriteRaw([FuncHeartbeat]);
                    break;
                case HeadFeedback:
                    _inAa = true;
                    _aaBuf.Clear();
                    break;
                default:
                    // 命令回显的模块号（ACK）等其它单字节
                    _log.Debug($"康伟达模拟仪 RX: {b:X2}");
                    TryCompleteAck(b);
                    break;
            }
        }

        private void FinishAaFrame()
        {
            _inAa = false;
            if (_aaBuf.Count >= 2)
            {
                // AA (压力+10)高 (压力+10)低 + 填充：原始值 = (P+偏移)×100
                var raw = (_aaBuf[0] << 8) | _aaBuf[1];
                LastPressure = raw / 100.0 - FeedbackOffset;

                if ((DateTime.Now - _lastAaLog).TotalMilliseconds >= 200)
                {
                    _lastAaLog = DateTime.Now;
                    var frame = new byte[1 + _aaBuf.Count];
                    frame[0] = HeadFeedback;
                    _aaBuf.CopyTo(0, frame, 1, _aaBuf.Count);
                    _log.Debug($"康伟达模拟仪 RX: {FormatFrame(frame)} → {LastPressure:0.0} mmHg");
                }

                PressureSampled?.Invoke(LastPressure);
            }
            _aaBuf.Clear();
        }

        // ==================== 工具 ====================

        public static string FormatFrame(byte[] frame) => BitConverter.ToString(frame).Replace('-', ' ');

        private static ushort ToU16(double value) => (ushort)Math.Clamp(Math.Round(value), 0, 65535);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                CloseAsync().GetAwaiter().GetResult();
            }
            catch { /* 释放路径忽略 */ }
            GC.SuppressFinalize(this);
        }
    }
}
