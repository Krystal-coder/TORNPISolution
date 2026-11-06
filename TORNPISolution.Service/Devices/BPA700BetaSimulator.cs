using System.Runtime.InteropServices;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 鲸扬 BPA700 的 1.0.4.0-BETA 句柄版 SDK 控制类（BPA700x64Beta.dll），只服务需要"压力点直切、点间不泄回零"的气路类测试项（A18/A22）。
    /// 其余血压测试项仍走正式版 <see cref="BPA700Simulator"/>；两枚 DLL 同目录不同名，可在同一进程各自连接。
    /// 本类未实现的能力（血压模拟、过压、阻塞式逐点序列、初始化设置下发）统一抛 NotSupportedException。
    /// </summary>
    public sealed class BPA700BetaSimulator : IBloodPressureSimulator
    {
        #region SDK P/Invoke 声明（Beta 1.0.4.0，句柄版）

        private const string DllName = "BPA700x64Beta.dll";

        /// <summary>静态压力序列回调：1.0.4.0 实测 index 带当前压力点序号（第二点 Busy 回调 index=1），直接透传给 PressurePointStable</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StaticPressureExCallbackDelegate(IntPtr handle, int status, double waitTime, int index);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SamplingCallbackDelegate(double data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BPAConnect(uint portNumber, uint millisecondsTimeout);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void BPAFree(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPAStopTest(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPARebaseZero(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool enable);

        // 静态压力序列：pressure 数组 1~10 个点，每个点稳压 time 秒（0~999）后直接切到下一点，点间不泄回零；
        // 超出量程（mmHg 20~400 / kPa 2.7~53.3）的点由设备当作禁用跳过；单位跟随仪器当前压力单位设置
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPARunStaticPressureSequence(IntPtr handle, double[] pressure, int size, int time,
            StaticPressureExCallbackDelegate cb, [MarshalAs(UnmanagedType.I1)] bool inflation);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPAStartSampling(IntPtr handle, SamplingCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void BPAStopSampling(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int BPAGetSampleRate();

        // 上一轮由本机充气后，下一次发起前需等待的恢复秒数（0~10s，随上一轮充气高度变化）
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int BPAGetWaitStableTime(IntPtr handle);

        // 气阀（BPAValve）：Port1=0 Port2=1 FastValve=3 SlowValve=4；阀态在下一次调用测试函数时才实际生效
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPAEnableAirPort(IntPtr handle, int portNumber, [MarshalAs(UnmanagedType.I1)] bool onOff);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int BPAGetPressureUnit(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BPAGetVersion();

        private const int ValvePort1 = 0;
        private const int ValvePort2 = 1;
        private const int ValveFastRelease = 3;
        private const int ValveSlowRelease = 4;
        private const int MaxSequencePoints = 10;

        #endregion

        private readonly ILogService _log;
        private readonly string _portName;

        private IntPtr _handle;
        private bool _connected;
        private string _pressureUnit = "mmHg";

        // 保持回调委托引用，防止被 GC 回收导致崩溃
        private StaticPressureExCallbackDelegate? _staticPressureCallback;
        private SamplingCallbackDelegate? _samplingCallback;
        private TaskCompletionSource<bool>? _staticFinishTcs;
        /// <summary>上一次序列回调的 (status,index)：同一点同一状态（尤其 Wait 0.2s 一条）不重复打日志</summary>
        private int _lastSeqStatus = -1;
        private int _lastSeqIndex = -1;

        private readonly Queue<double> _recentSamples = new();
        private readonly object _sampleLock = new();

        /// <summary>进程退出兜底用的句柄：模块 Dispose 没走到（异常/强关窗口）时也要释放对仪器的控制</summary>
        private static IntPtr _heldHandle;

        public BPA700BetaSimulator(string portName, ILogService? log = null)
        {
            _portName = portName;
            _log = log ?? new LogService(nameof(BPA700BetaSimulator));
        }

        public bool IsOpen => _connected;

        /// <summary>压力采样数据事件（波形与到压/切压判定的数据源，单位跟随仪器压力单位）</summary>
        public event Action<double>? PressureSampled;

        /// <summary>序列中每个压力点到达 Stable 时触发（index=第几个点，pressure=该点采样均值）</summary>
        public event Action<int, double>? PressurePointStable;

        /// <summary>稳压回读只来自 SDK 采样回调，不开采样就没有回读</summary>
        public bool RequiresSamplingForReadback => true;

        public double LastStaticPressure { get; private set; }

        /// <summary>
        /// 连接并按厂商静态压力序列示例把仪器调到可跑状态：归零 → Port1 通、Port2 与快慢释阀关 → 读压力单位。
        /// Port1 不通则压力进不了被测气路，所以每次连接都显式设好，不依赖仪器残留的阀态。
        /// </summary>
        public Task OpenAsync(CancellationToken ct = default) => Task.Run(() =>
        {
            if (_connected) return;

            var portNumber = ExtractPortNumber(_portName);
            _log.Info($"BPA700Beta：正在连接 COM{portNumber}...");

            var handle = BPAConnect(portNumber, 5000);
            if (handle == IntPtr.Zero)
            {
                _log.Error($"BPA700Beta：连接 COM{portNumber} 超时或失败");
                throw new IOException($"BPA700Beta 连接失败（COM{portNumber}）");
            }

            _handle = handle;
            _connected = true;
            Interlocked.Exchange(ref _heldHandle, handle);

            if (!BPARebaseZero(handle, true))
                _log.Warn("BPA700Beta：压力归零（BPARebaseZero）返回 false");
            SetAirPort(ValvePort1, true);
            SetAirPort(ValvePort2, false);
            SetAirPort(ValveFastRelease, false);
            SetAirPort(ValveSlowRelease, false);

            var unitCode = BPAGetPressureUnit(handle);
            _pressureUnit = unitCode switch { 0 => "mmHg", 1 => "kPa", _ => $"未知({unitCode})" };
            _log.Info($"BPA700Beta：已连接 COM{portNumber}（handle=0x{handle.ToInt64():X}，SDK 版本 {GetVersionText()}，压力单位 {_pressureUnit}）");
            if (unitCode != 0)
                _log.Warn($"BPA700Beta：仪器压力单位是 {_pressureUnit}，气路类两项的压力点按 mmHg 下发，需先在血压初始化项把单位设为 mmHg");
        }, ct);

        public Task CloseAsync() => Task.Run(() => ReleaseHandle());

        /// <summary>压力归零（BPARebaseZero）</summary>
        public async Task ResetAsync(CancellationToken ct = default)
        {
            EnsureConnected();
            await Task.Run(() => BPARebaseZero(_handle, true), ct).ConfigureAwait(false);
            _log.Info("BPA700Beta：压力已归零");
        }

        /// <summary>
        /// 只发起一轮静态压力序列不等完成：一次下发全部压力点，设备按点充气、稳压 holdSeconds 秒后直接切到下一点（点间不泄回零），
        /// 到压与切压过程由采样回调上抛，上层用采样流判每个点的到达时刻；回调 index 即当前点序号（20:56 台架第二点回调 index=1）。
        /// 本轮结束（Finish 或 StopSimulationAsync）前不能再发起下一轮，否则被设备拒绝。
        /// </summary>
        public async Task StartStaticPressureSequenceAsync(double[] points, int holdSeconds, bool inflation, CancellationToken ct = default)
        {
            EnsureConnected();
            if (points is not { Length: > 0 } || points.Length > MaxSequencePoints)
                throw new ArgumentException($"压力点数量需在 1~{MaxSequencePoints} 之间", nameof(points));

            var finishTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _staticFinishTcs = finishTcs;
            _lastSeqStatus = -1;
            _lastSeqIndex = -1;

            _staticPressureCallback = (handle, status, waitTime, index) =>
            {
                // SDK 每 200ms 回调一次，Wait 阶段连发几十条；只在该点状态切换时记一行
                if (status != _lastSeqStatus || index != _lastSeqIndex)
                {
                    _lastSeqStatus = status;
                    _lastSeqIndex = index;
                    _log.Info($"BPA700Beta 静态压力：点{index + 1} {StaticStatusText(status, waitTime)}");
                }
                if (status == 3)
                {
                    LastStaticPressure = ReadRecentAveragePressure();
                    PressurePointStable?.Invoke(index, LastStaticPressure);
                }
                else if (status == 4)
                {
                    finishTcs.TrySetResult(true);
                }
            };

            var sequence = points.ToArray();
            var ok = await Task.Run(() => BPARunStaticPressureSequence(_handle, sequence, sequence.Length, holdSeconds, _staticPressureCallback, inflation), ct)
                .ConfigureAwait(false);
            if (!ok)
                throw new IOException($"BPA700Beta 静态压力序列发起失败（{string.Join("/", sequence)}{_pressureUnit}，如上一轮未结束请先停止）");

            _log.Info($"BPA700Beta：启动静态压力序列 {string.Join("→", sequence)}{_pressureUnit}，每点保持 {holdSeconds}s，点间直接切压不泄回零");
        }

        /// <summary>静态压力状态文本（日志用）：SDK status 0=Idle / 1=Wait / 2=Busy / 3=Stable / 4=Finish</summary>
        private static string StaticStatusText(int status, double waitTime) => status switch
        {
            0 => "Idle",
            1 => $"等待设备恢复 {waitTime:0.#}s",
            2 => "充气/稳压中",
            3 => "已稳定（Stable）",
            4 => "本轮完成（Finish）",
            _ => $"status={status}"
        };

        /// <summary>等待本轮序列 Finish；超时未收到则主动 BPAStopTest 结束，保证仪器不留在带压态</summary>
        public async Task WaitStaticPressureFinishAsync(int timeoutMs = 60_000, CancellationToken ct = default)
        {
            var tcs = _staticFinishTcs;
            _staticFinishTcs = null;
            if (tcs == null) return;

            if (await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false) != tcs.Task)
            {
                _log.Warn($"BPA700Beta：静态压力序列 {timeoutMs / 1000}s 未收到 Finish，主动结束本轮");
                // 不传 ct：走到这里常常正是因为取消才没等到 Finish，带上已取消的令牌会让委托根本不执行，仪器就停在带压状态
                await Task.Run(() => BPAStopTest(_handle)).ConfigureAwait(false);
            }
        }

        public async Task StopSimulationAsync(CancellationToken ct = default)
        {
            if (!_connected) return;
            await Task.Run(() =>
            {
                BPAStopTest(_handle);
                _staticFinishTcs?.TrySetResult(false);
            }, ct).ConfigureAwait(false);
            _log.Info("BPA700Beta：已停止本轮测试");
        }

        /// <summary>慢速放气阀开关（阀态在下一次测试调用时才生效）</summary>
        public Task SetSlowValveAsync(bool open, CancellationToken ct = default)
        {
            EnsureConnected();
            return Task.Run(() => SetAirPort(ValveSlowRelease, open), ct);
        }

        /// <summary>快速放气阀开关（阀态在下一次测试调用时才生效）</summary>
        public Task SetFastValveAsync(bool open, CancellationToken ct = default)
        {
            EnsureConnected();
            return Task.Run(() => SetAirPort(ValveFastRelease, open), ct);
        }

        /// <summary>
        /// 泄放回零：SDK 无泄压 API，残压只在本轮结束后自行回落，按仪器自报恢复时长（BPAGetWaitStableTime，0~10s）等待后用采样判零；
        /// 不碰气阀——阀态要下一次测试调用才生效，在这里开阀只会在下一点加压时才动作。
        /// false 只用于有回读且确认未归零；无回读可验证时按已泄压返回 true。
        /// </summary>
        public async Task<bool> VentToZeroAsync(int timeoutSeconds = 60, CancellationToken ct = default)
        {
            EnsureConnected();

            var recoverSeconds = await Task.Run(() => BPAGetWaitStableTime(_handle), ct).ConfigureAwait(false);
            recoverSeconds = Math.Clamp(recoverSeconds, 0, Math.Min(15, timeoutSeconds));
            _log.Info($"BPA700Beta：等待仪器放气恢复 {recoverSeconds}s");
            if (recoverSeconds > 0)
                await Task.Delay(recoverSeconds * 1000, ct).ConfigureAwait(false);

            lock (_sampleLock) _recentSamples.Clear();
            await Task.Delay(1500, ct).ConfigureAwait(false);
            var residual = ReadTailAveragePressure();
            if (double.IsNaN(residual))
            {
                _log.Info("BPA700Beta：压力采样未运行，残压无回读，按已泄压处理");
                return true;
            }

            var deadline = DateTime.UtcNow.AddSeconds(Math.Min(timeoutSeconds, Math.Max(10, residual / 10 + 5)));
            while (true)
            {
                if (residual < 5)
                {
                    _log.Info($"BPA700Beta：已泄压回零（回读 {residual:0.0}{_pressureUnit}）");
                    return true;
                }
                if (DateTime.UtcNow >= deadline)
                {
                    _log.Warn($"BPA700Beta：泄压回零超时，残压 {residual:0.0}{_pressureUnit}");
                    return false;
                }

                await Task.Delay(500, ct).ConfigureAwait(false);
                residual = ReadTailAveragePressure();
                if (double.IsNaN(residual))
                {
                    _log.Info("BPA700Beta：采样已停止，残压无回读，按已泄压处理");
                    return true;
                }
            }
        }

        /// <summary>开始压力采样：SDK 按自身采样率持续回调，同时维护近期窗口供稳压均值读回</summary>
        public async Task StartSamplingAsync(CancellationToken ct = default)
        {
            EnsureConnected();

            lock (_sampleLock) _recentSamples.Clear();
            _samplingCallback = data =>
            {
                lock (_sampleLock)
                {
                    _recentSamples.Enqueue(data);
                    if (_recentSamples.Count > 100) _recentSamples.Dequeue();
                }
                PressureSampled?.Invoke(data);
            };

            var ok = await Task.Run(() => BPAStartSampling(_handle, _samplingCallback), ct).ConfigureAwait(false);
            if (!ok) throw new IOException("BPA700Beta 启动压力采样失败");
            _log.Info($"BPA700Beta：开始压力采样（采样率 {BPAGetSampleRate()}Hz）");
        }

        public Task StopSamplingAsync()
        {
            if (!_connected) return Task.CompletedTask;
            return Task.Run(() =>
            {
                BPAStopSampling(_handle);
                _log.Info("BPA700Beta：已停止压力采样");
            });
        }

        /// <summary>SDK 内部管理连接状态，无需心跳</summary>
        public Task RespondToHeartbeatAsync(CancellationToken ct = default) => Task.CompletedTask;

        #region 本类不支持的能力

        /// <summary>Beta 类只给气路类两项用，血压模拟/过压/初始化设置仍由正式版 SDK 承担</summary>
        public Task ApplySettingsAsync(string category, string pressureUnit, string airReservoir, CancellationToken ct = default)
            => throw new NotSupportedException("BPA700Beta：初始化设置由正式版 SDK 下发，本类不驱动血压模拟");

        public Task StartSimulationAsync(int systolic, int diastolic, int heartRate, double pulseVolume = 0, CancellationToken ct = default)
            => throw new NotSupportedException("BPA700Beta：不支持血压模拟，请用正式版 SDK");

        public Task<BpTestRunResult> RunBpMeasurementAsync(
            int systolic, int diastolic, int heartRate, double pulseVolume = 0,
            int waitStartTimeoutMs = 30_000, int totalTimeoutMs = 60_000, CancellationToken ct = default)
            => throw new NotSupportedException("BPA700Beta：不支持一轮血压测量，请用正式版 SDK");

        public Task<OverpressureRunResult> RunOverpressureTestAsync(
            double pressure, double threshold1, double threshold2,
            bool inflation = true, int timeoutMs = 120_000, CancellationToken ct = default)
            => throw new NotSupportedException("BPA700Beta：不支持过压保护测试，请用正式版 SDK");

        public Task SetStaticPressureAsync(double pressure, int stableSeconds = 5, CancellationToken ct = default)
            => throw new NotSupportedException("BPA700Beta：单点静态压力请用 StartStaticPressureSequenceAsync（一个点）");

        public Task SetStaticPressureAsync(double pressure, int stableSeconds, bool inflation, CancellationToken ct, int timeoutSeconds = 180)
            => throw new NotSupportedException("BPA700Beta：单点静态压力请用 StartStaticPressureSequenceAsync（一个点）");

        public Task StartStaticPressureAsync(double pressure, int stableSeconds, bool inflation, CancellationToken ct = default)
            => throw new NotSupportedException("BPA700Beta：单点静态压力请用 StartStaticPressureSequenceAsync（一个点）");

        public Task<double[]> RunStaticPressureSequenceAsync(double[] points, int stableSeconds, bool inflation, CancellationToken ct = default)
            => throw new NotSupportedException("BPA700Beta：本类只做非阻塞序列，阻塞式逐点循环请用正式版 SDK");

        #endregion

        private void EnsureConnected()
        {
            if (!_connected) throw new InvalidOperationException("BPA700Beta 未连接");
        }

        private void SetAirPort(int portNumber, bool onOff)
        {
            if (BPAEnableAirPort(_handle, portNumber, onOff)) return;
            var name = portNumber switch { ValvePort1 => "Port1", ValvePort2 => "Port2", ValveFastRelease => "快速放气阀", ValveSlowRelease => "慢速放气阀", _ => $"气阀{portNumber}" };
            _log.Warn($"BPA700Beta：{name}{(onOff ? "开" : "关")} 调用失败（下一次测试调用时才生效）");
        }

        private string GetVersionText()
        {
            var ptr = BPAGetVersion();
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? "" : "";
        }

        private double ReadRecentAveragePressure()
        {
            lock (_sampleLock)
            {
                return _recentSamples.Count == 0 ? double.NaN : _recentSamples.Average();
            }
        }

        /// <summary>取采样窗口尾部若干点的均值（判"当前压力"用；全窗均值会把降压前的高压一起平均进来，归零永远判不到）</summary>
        private double ReadTailAveragePressure(int count = 20)
        {
            lock (_sampleLock)
            {
                return _recentSamples.Count == 0 ? double.NaN : _recentSamples.Skip(Math.Max(0, _recentSamples.Count - count)).Average();
            }
        }

        private void ReleaseHandle()
        {
            if (!_connected) return;
            try
            {
                BPAStopSampling(_handle);
                BPAStopTest(_handle);
                BPAFree(_handle);
            }
            catch (Exception ex)
            {
                _log.Warn($"BPA700Beta：关闭连接时发生异常：{ex.Message}");
            }
            finally
            {
                Interlocked.CompareExchange(ref _heldHandle, IntPtr.Zero, _handle);
                _connected = false;
                _handle = IntPtr.Zero;
                _log.Info("BPA700Beta：已断开连接");
            }
        }

        public void Dispose()
        {
            ReleaseHandle();
            GC.SuppressFinalize(this);
        }

        /// <summary>进程退出兜底释放：停止采样/测试并归还设备控制权，仪器回到可手动操作状态（App 退出时调用）。</summary>
        public static void ReleaseDeviceControl()
        {
            var handle = Interlocked.Exchange(ref _heldHandle, IntPtr.Zero);
            if (handle == IntPtr.Zero) return;
            try
            {
                BPAStopSampling(handle);
                BPAStopTest(handle);
                BPAFree(handle);
            }
            catch { /* 退出路径忽略 */ }
        }

        /// <summary>从 COM 口号提取端口号，如 "COM5" → 5</summary>
        private static uint ExtractPortNumber(string portName)
        {
            var cleaned = portName.Replace("COM", "").Replace("com", "").Trim();
            return uint.TryParse(cleaned, out var num) ? num : 0;
        }
    }
}
