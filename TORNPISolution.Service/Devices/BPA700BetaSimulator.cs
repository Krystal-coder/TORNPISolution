using System.Runtime.InteropServices;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 鲸扬 BPA700 无创血压模拟仪 Beta SDK 控制类（handle-based 版本）。
    /// 当前所有 BPA700 测试项均使用此类；DLL 命名为 BPA700x64Beta.dll，
    /// 与正式版 BPA700x64.dll 共存（正式版暂时保留，供后续切换）。
    /// </summary>
    public class BPA700BetaSimulator : IDisposable, IBloodPressureSimulator
    {
        /// <summary>气阀编号（对应 SDK 的 BPAValve）</summary>
        public enum BPAValve
        {
            Port1 = 0,
            Port2 = 1,
            FastValve = 3,
            SlowValve = 4
        }

        #region  SDK P/Invoke 声明（Beta 1.0.4.0，handle-based）

        private const string DllName = "BPA700x64Beta.dll";

        /// <summary>静态压力测试回调（Beta SDK 多了一个 void* handle 参数）</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StaticPressureExCallbackDelegate(IntPtr handle, int status, double waitTime, int index);

        /// <summary>压力采样回调</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SamplingCallbackDelegate(double data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BPAConnect(uint portNumber, uint millisecondsTimeout);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void BPAFree(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPAStartSampling(IntPtr handle, SamplingCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void BPAStopSampling(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int BPAGetSampleRate();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPAStopTest(IntPtr handle);

        /// <summary>静态压力测试：pressure 20~400mmHg，time 稳压保持 0~999 秒</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPARunStaticPressure(IntPtr handle, double pressure, int time, StaticPressureExCallbackDelegate cb, [MarshalAs(UnmanagedType.I1)] bool inflation);

        /// <summary>静态压力序列测试：一次下发多个压力点，切换时不会回到 0</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPARunStaticPressureSequence(IntPtr handle, double[] pressure, int size, int time, StaticPressureExCallbackDelegate cb, [MarshalAs(UnmanagedType.I1)] bool inflation);

        /// <summary>设置气阀开关（在测试函数调用前设置，下次测试时生效）</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPAEnableAirPort(IntPtr handle, int portNumber, [MarshalAs(UnmanagedType.I1)] bool onOff);

        // Beta SDK 同样需要应用患者类型/压力单位/袖带容量等初始化设置
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPASwitchToAdultMode(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool enable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPASwitchTo500mlMode(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool enable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPAIs500mlMode(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPASetPressureUnit(IntPtr handle, int unit);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int BPAGetPressureUnit(IntPtr handle);

        // 血压模拟测试（BP-001/002/004/005）
        [StructLayout(LayoutKind.Sequential)]
        private struct BPCommon
        {
            public int MethodType;     // 0 = Default
            public double Systolic;
            public double Diastolic;
            public uint HeartRate;
            public double PulseVolume;
            public double EnvelopSystolic;
            public double EnvelopDiastolic;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void BPTestCallbackDelegate(IntPtr handle, int status, double peakPressure, double inflatableTime, double deflationTime, double totalTime);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void OverpressureTestCallbackDelegate(IntPtr handle, int status, double waitTime, double peakPressure, double inflatableTime, double deflationTime, double totalTime, double overTime, double over10Time);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPARebaseZero(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool enable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPARunBPTest(IntPtr handle, IntPtr envelope, BPTestCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPARunOverpressureTest(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool inflation, double pressure, double threshold1, double threshold2, OverpressureTestCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BPAGetSerialNumber(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BPAGetVersion();

        #endregion

        private readonly ILogService _log;
        private readonly string _portName;
        private IntPtr _handle;
        private bool _connected;

        private StaticPressureExCallbackDelegate? _staticPressureCallback;
        private SamplingCallbackDelegate? _samplingCallback;
        private TaskCompletionSource<bool>? _staticFinishTcs;
        private BPTestCallbackDelegate? _bpTestCallback;
        private OverpressureTestCallbackDelegate? _overpressureCallback;

        private readonly Queue<double> _recentSamples = new();
        private readonly object _sampleLock = new();

        /// <summary>压力采样数据事件（波形数据源，单位 mmHg）</summary>
        public event Action<double>? PressureSampled;

        /// <summary>序列模式下每个压力点到达 Stable 时触发（index=点序号，pressure=采样均值）</summary>
        public event Action<int, double>? PressurePointStable;

        public BPA700BetaSimulator(string portName, ILogService? log = null)
        {
            _portName = portName;
            _log = log ?? new LogService(nameof(BPA700BetaSimulator));
        }

        public bool IsOpen => _connected;

        /// <summary>本进程是否连接过 BPA700Beta（App 退出时据此兜底释放）</summary>
        private static volatile bool _everConnected;

        /// <summary>连接 BPA700 Beta SDK 设备</summary>
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
            _everConnected = true;
            _log.Info($"BPA700Beta：已连接 COM{portNumber}（handle=0x{_handle.ToInt64():X})");
        }, ct);

        /// <summary>断开连接并释放 SDK 资源</summary>
        public Task CloseAsync() => Task.Run(() =>
        {
            if (!_connected) return Task.CompletedTask;

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

            _connected = false;
            _handle = IntPtr.Zero;
            _log.Info("BPA700Beta：已断开连接");
            return Task.CompletedTask;
        });

        /// <summary>开始压力采样</summary>
        public async Task StartSamplingAsync(CancellationToken ct = default)
        {
            if (!_connected) throw new InvalidOperationException("BPA700Beta 未连接");

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

            var success = await Task.Run(() => BPAStartSampling(_handle, _samplingCallback!), ct).ConfigureAwait(false);
            if (!success)
                throw new IOException("BPA700Beta 启动压力采样失败");

            _log.Info($"BPA700Beta：开始压力采样（采样率 {BPAGetSampleRate()}Hz）");
        }

        /// <summary>停止压力采样</summary>
        public Task StopSamplingAsync()
        {
            if (!_connected) return Task.CompletedTask;
            return Task.Run(() =>
            {
                BPAStopSampling(_handle);
                _log.Info("BPA700Beta：已停止压力采样");
            });
        }

        /// <summary>设置恒定标准压力并等待稳定（仅静态压力测试用）</summary>
        public Task SetStaticPressureAsync(double pressure, int stableSeconds, CancellationToken ct)
            => SetStaticPressureAsync(pressure, stableSeconds, inflation: true, ct);

        /// <summary>接口实现：三参签名（stableSeconds 带默认值）委托到充气版</summary>
        Task IBloodPressureSimulator.SetStaticPressureAsync(double pressure, int stableSeconds, CancellationToken ct)
            => SetStaticPressureAsync(pressure, stableSeconds, inflation: true, ct);

        /// <summary>设置恒定标准压力并等待稳定（仅静态压力测试用）</summary>
        public async Task SetStaticPressureAsync(double pressure, int stableSeconds, bool inflation, CancellationToken ct, int timeoutSeconds = 180)
        {
            var stableReached = await StartStaticPressureTestAsync(pressure, stableSeconds, inflation, ct).ConfigureAwait(false);

            _log.Info($"BPA700Beta：设置静态压力 {pressure}，保持 {stableSeconds}s，等待到位（超时 {timeoutSeconds}s）...");

            var timeoutMs = timeoutSeconds * 1000;
            var waitStart = System.Diagnostics.Stopwatch.StartNew();
            double lastLoggedPressure = double.NaN;
            var lastChangeTime = waitStart.Elapsed;
            bool completed = false;
            const double StuckThreshold = 0.5;
            const double StuckTimeoutSeconds = 30;

            while (waitStart.ElapsedMilliseconds < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();

                if (stableReached.Task.IsCompleted)
                {
                    completed = true;
                    break;
                }

                var currentPressure = ReadRecentAveragePressure();
                if (!double.IsNaN(currentPressure))
                {
                    if (!double.IsNaN(lastLoggedPressure) && Math.Abs(currentPressure - lastLoggedPressure) < StuckThreshold)
                    {
                        var stagnant = waitStart.Elapsed - lastChangeTime;
                        if (stagnant.TotalSeconds >= StuckTimeoutSeconds)
                        {
                            throw new TimeoutException($"BPA700Beta 静态压力 {pressure} 充气卡住（实际压力 {currentPressure:0.1}，{stagnant.TotalSeconds:0.0}s 无变化），请检查气路连接");
                        }
                    }
                    else if (!double.IsNaN(lastLoggedPressure))
                    {
                        lastChangeTime = waitStart.Elapsed;
                    }
                    lastLoggedPressure = currentPressure;
                }

                await Task.Delay(500, ct).ConfigureAwait(false);
            }

            if (!completed)
            {
                var actualPressure = double.IsNaN(lastLoggedPressure) ? ReadRecentAveragePressure() : lastLoggedPressure;
                var pressureInfo = double.IsNaN(actualPressure) ? "无采样数据" : $"实际压力 {actualPressure:0.1}";
                throw new TimeoutException($"BPA700Beta 静态压力 {pressure} 等待到位超时（{timeoutSeconds}s），{pressureInfo}。请检查气路连接或增大配置页超时时间");
            }

            await Task.Delay(800, ct).ConfigureAwait(false);
            var sampled = ReadRecentAveragePressure();
            LastStaticPressure = double.IsNaN(sampled) ? pressure : sampled;
            if (double.IsNaN(sampled))
                _log.Warn("BPA700Beta：压力采样未运行，稳压值回退用目标压力");

            _log.Info($"BPA700Beta：静态压力 {pressure} 已到位（采样读回 {LastStaticPressure:0.0}）");
        }

        /// <summary>配置气阀：Port1 开，其余关（静态压力序列常用配置）</summary>
        public Task ConfigureValvesForStaticPressureAsync(CancellationToken ct = default)
        {
            if (!_connected) throw new InvalidOperationException("BPA700Beta 未连接");

            return Task.Run(() =>
            {
                BPAEnableAirPort(_handle, (int)BPAValve.Port1, true);
                BPAEnableAirPort(_handle, (int)BPAValve.Port2, false);
                BPAEnableAirPort(_handle, (int)BPAValve.FastValve, false);
                BPAEnableAirPort(_handle, (int)BPAValve.SlowValve, false);
                _log.Info("BPA700Beta：气阀已配置为 Port1=开，Port2/Fast/Slow=关");
            }, ct);
        }

        /// <summary>接口成员：启动静态压力测试不等待稳压完成（BP-007/008 使气阀状态生效用，逻辑同 StartStaticPressureTestAsync）</summary>
        public async Task StartStaticPressureAsync(double pressure, int stableSeconds, bool inflation, CancellationToken ct = default)
            => await StartStaticPressureTestAsync(pressure, stableSeconds, inflation, ct).ConfigureAwait(false);

        /// <summary>接口成员：RunBPTestAsync 的统一入口别名，供上层经接口调用（逻辑不变；pulseVolume 为接口统一参数，SDK 忽略）</summary>
        public Task<BpTestRunResult> RunBpMeasurementAsync(
            int systolic, int diastolic, int heartRate, double pulseVolume = 0,
            int waitStartTimeoutMs = 30_000, int totalTimeoutMs = 60_000,
            CancellationToken ct = default)
            => RunBPTestAsync(systolic, diastolic, heartRate, waitStartTimeoutMs, totalTimeoutMs, ct);

        /// <summary>发起静态压力测试并返回 Stable/Finish 到达信号</summary>
        public async Task<TaskCompletionSource<bool>> StartStaticPressureTestAsync(double pressure, int stableSeconds, bool inflation, CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700Beta 未连接");

            var stableReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _staticFinishTcs = finishTcs;

            _staticPressureCallback = (handle, status, waitTime, index) =>
            {
                _log.Debug($"BPA700Beta 静态压力回调：status={status} wait={waitTime:0.0} index={index}");
                if (status == 3 || status == 4)
                    stableReached.TrySetResult(true);
                if (status == 4)
                    finishTcs.TrySetResult(true);
            };

            var success = await Task.Run(() => BPARunStaticPressure(_handle, pressure, stableSeconds, _staticPressureCallback, inflation), ct)
                .ConfigureAwait(false);
            if (!success)
            {
                _log.Warn($"BPA700Beta：BPARunStaticPressure({pressure}, inflation={inflation}) 首次调用失败，BPAStopTest 后重试");
                await Task.Run(() => BPAStopTest(_handle)).ConfigureAwait(false);
                await Task.Delay(1000, ct).ConfigureAwait(false);
                success = await Task.Run(() => BPARunStaticPressure(_handle, pressure, stableSeconds, _staticPressureCallback, inflation), ct)
                    .ConfigureAwait(false);
            }
            if (!success)
            {
                _log.Error($"BPA700Beta：BPARunStaticPressure({pressure}, inflation={inflation}) 调用失败");
                throw new IOException($"BPA700Beta 静态压力设置失败（{pressure}）");
            }

            return stableReached;
        }

        /// <summary>
        /// 执行静态压力序列测试（多个压力点一次下发，切换点不会掉到 0）。
        /// 返回所有压力点 Stable 后的采样均值。
        /// 注意：Beta SDK 回调的 index 参数为保留字段，永远返回 0，
        /// 因此改用 Stable 回调计数器来追踪当前到达的压力点序号。
        /// </summary>
        public async Task<double[]> RunStaticPressureSequenceAsync(double[] points, int stableSeconds, bool inflation, CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700Beta 未连接");
            if (points.Length == 0)
                throw new ArgumentException("压力点不能为空", nameof(points));

            await ConfigureValvesForStaticPressureAsync(ct).ConfigureAwait(false);

            var stablePressures = new double[points.Length];
            for (var i = 0; i < stablePressures.Length; i++)
                stablePressures[i] = double.NaN;

            // Beta SDK 回调 index 永远为 0，用 stableCount 自增来追踪当前第几个压力点
            var stableCount = 0;

            var finishTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _staticPressureCallback = (handle, status, waitTime, index) =>
            {
                _log.Debug($"BPA700Beta 静态压力序列回调：status={status} wait={waitTime:0.0} index={index}");
                if (status == 3) // Stable
                {
                    var pointIndex = stableCount;
                    stableCount++;
                    var pressure = ReadRecentAveragePressure();
                    if (pointIndex >= 0 && pointIndex < stablePressures.Length && !double.IsNaN(pressure))
                    {
                        stablePressures[pointIndex] = pressure;
                        LastStaticPressure = pressure;
                        PressurePointStable?.Invoke(pointIndex, pressure);
                        _log.Info($"BPA700Beta 压力点 {pointIndex}（目标 {points[pointIndex]} mmHg）已稳定：{pressure:0.0} mmHg");
                    }
                }
                if (status == 4) // Finish
                {
                    _log.Info($"BPA700Beta 静态压力序列完成，共 {stableCount} 个点稳定");
                    finishTcs.TrySetResult(true);
                }
            };

            var pressures = points.ToArray();
            var success = await Task.Run(() => BPARunStaticPressureSequence(_handle, pressures, pressures.Length, stableSeconds, _staticPressureCallback, inflation), ct)
                .ConfigureAwait(false);
            if (!success)
                throw new IOException("BPA700Beta 静态压力序列启动失败");

            var timeoutMs = Math.Max(30_000, (points.Length * (stableSeconds + 15) + 30) * 1000);
            if (await Task.WhenAny(finishTcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false) != finishTcs.Task)
            {
                _log.Warn($"BPA700Beta：静态压力序列 {timeoutMs / 1000}s 未收到 Finish，主动 BPAStopTest 结束");
                // 不传 ct：走到这里常常正是因为取消才没等到 Finish，带上已取消的令牌会让委托根本不执行，
                // 仪器就停在带压状态（与 WaitStaticPressureFinishAsync 里的同类兜底保持一致）
                await Task.Run(() => BPAStopTest(_handle)).ConfigureAwait(false);
            }

            return stablePressures;
        }

        /// <summary>等待当前静态压力测试完成（回调 status=Finish）</summary>
        public async Task WaitStaticPressureFinishAsync(int timeoutMs = 60_000, CancellationToken ct = default)
        {
            var tcs = _staticFinishTcs;
            _staticFinishTcs = null;
            if (tcs == null) return;

            if (await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false) != tcs.Task)
            {
                _log.Warn($"BPA700Beta：静态压力测试 {timeoutMs / 1000}s 未收到 Finish，主动 BPAStopTest 结束本轮");
                await Task.Run(() => BPAStopTest(_handle)).ConfigureAwait(false);
            }
        }

        /// <summary>静态压力到位后的实际稳压值</summary>
        public double LastStaticPressure { get; private set; }

        /// <summary>读取近期采样窗口均值</summary>
        private double ReadRecentAveragePressure()
        {
            lock (_sampleLock)
            {
                if (_recentSamples.Count == 0) return double.NaN;
                return _recentSamples.Average();
            }
        }

        /// <summary>
        /// 应用仪器初始化设置：患者类型（Adult/Neonatal）、压力单位（mmHg/kPa）、袖带容量（500ml/100ml）。
        /// Beta SDK 同样需要显式下发这三项，否则静态压力测试可能收不到采样/回调。
        /// </summary>
        public async Task ApplySettingsAsync(string category, string pressureUnit, string airReservoir, CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700Beta 未连接");

            // 患者类型：Adult=true，Neonatal=false
            var adultRtn = BPASwitchToAdultMode(_handle, category == "Adult");
            if (adultRtn)
            {
                _log.Info($"BPA700Beta：患者类型已设为 {category}");
                await Task.Delay(1500, ct).ConfigureAwait(false);
            }
            else
            {
                _log.Warn($"BPA700Beta：切换患者类型 {category} 返回 false");
            }

            // 压力单位：0=mmHg 1=kPa
            var unitCode = pressureUnit == "kPa" ? 1 : 0;
            if (BPASetPressureUnit(_handle, unitCode))
            {
                var readBack = BPAGetPressureUnit(_handle);
                if (readBack == unitCode)
                    _log.Info($"BPA700Beta：压力单位已设为 {pressureUnit}");
                else
                    _log.Warn($"BPA700Beta：压力单位读回 {readBack} 与期望 {unitCode} 不符");
            }
            else
            {
                _log.Warn($"BPA700Beta：设置压力单位 {pressureUnit} 失败");
            }

            // 袖带容量：500ml=true，100ml=false
            var want500 = airReservoir == "500ml";
            var switchRtn = BPASwitchTo500mlMode(_handle, want500);
            var applied = await WaitUntilAsync(() => BPAIs500mlMode(_handle) == want500, timeoutMs: 6000, pollMs: 500, ct).ConfigureAwait(false);
            if (!applied)
            {
                _log.Warn($"BPA700Beta：{airReservoir} 切换读回未生效，重试一次...");
                await Task.Delay(1500, ct).ConfigureAwait(false);
                switchRtn = BPASwitchTo500mlMode(_handle, want500);
                applied = await WaitUntilAsync(() => BPAIs500mlMode(_handle) == want500, timeoutMs: 6000, pollMs: 500, ct).ConfigureAwait(false);
            }

            if (applied)
                _log.Info($"BPA700Beta：袖带容量已设为 {airReservoir}");
            else
                _log.Warn($"BPA700Beta：切换 {airReservoir} 未生效（读回={BPAIs500mlMode(_handle)} 返回={switchRtn}）");

            _log.Info($"BPA700Beta：初始化设置应用完成 Category={category} PressureUnit={pressureUnit} AirReservoir={airReservoir}");
        }

        /// <summary>轮询等待条件成立（仪器执行模式切换需要时间），响应停止令牌</summary>
        private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs, int pollMs, CancellationToken ct)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (condition())
                    return true;
                await Task.Delay(pollMs, ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            return condition();
        }

        /// <summary>压力归零（相当于复位）</summary>
        public async Task ResetAsync(CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700Beta 未连接");

            await Task.Run(() => BPARebaseZero(_handle, true), ct).ConfigureAwait(false);
            _log.Info("BPA700Beta：压力已归零");
        }

        /// <summary>
        /// 接口兼容方法：直接调用 BPARunBPTest 进入待测状态，等待 Finish（pulseVolume 为接口统一参数，SDK 忽略）。
        /// </summary>
        public async Task StartSimulationAsync(int systolic, int diastolic, int heartRate, double pulseVolume = 0, CancellationToken ct = default)
        {
            await RunBPTestAsync(systolic, diastolic, heartRate, ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 执行一轮血压模拟测试（BP-001/002/004/005），返回完整 status 序列与结果。
        /// </summary>
        public async Task<BpTestRunResult> RunBPTestAsync(
            int systolic, int diastolic, int heartRate,
            int waitStartTimeoutMs = 30_000, int totalTimeoutMs = 60_000,
            CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700Beta 未连接");

            var bpParams = new BPCommon
            {
                MethodType = 0,
                Systolic = systolic,
                Diastolic = diastolic,
                HeartRate = (uint)heartRate,
                PulseVolume = 0.5,
                EnvelopSystolic = 0,
                EnvelopDiastolic = 0
            };

            var sequence = new List<int>();
            var startedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var doneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lastStatus = 1;
            double peak = 0, inflateTime = 0, deflateTime = 0, totalTime = 0;

            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<BPCommon>());
            try
            {
                Marshal.StructureToPtr(bpParams, ptr, false);

                _bpTestCallback = (handle, status, peakPressure, inflatableTime, deflationTime, total) =>
                {
                    lock (sequence) { sequence.Add(status); }
                    lastStatus = status;
                    peak = peakPressure;
                    inflateTime = inflatableTime;
                    deflateTime = deflationTime;
                    totalTime = total;
                    _log.Debug($"BPA700Beta BP测试回调：status={status} peak={peakPressure:0.0} " +
                               $"inflate={inflatableTime:0.0}s deflate={deflationTime:0.0}s total={total:0.0}s");

                    if (status != 1) startedTcs.TrySetResult(true);
                    if (status == 0 || status == 4) doneTcs.TrySetResult(true);
                };

                if (!BPARunBPTest(_handle, ptr, _bpTestCallback))
                {
                    _log.Error("BPA700Beta：BPARunBPTest 调用失败");
                    throw new IOException("BPA700Beta 血压模拟启动失败");
                }

                _log.Info($"BPA700Beta：血压测试待测中 SYS={systolic} DIA={diastolic} HR={heartRate}，等待产品启动测量...");

                if (await Task.WhenAny(startedTcs.Task, Task.Delay(waitStartTimeoutMs, ct)).ConfigureAwait(false) != startedTcs.Task)
                {
                    _log.Warn($"BPA700Beta：{waitStartTimeoutMs / 1000}s 内 status 一直停留在 Wait，产品未启动或气路未连通");
                    await Task.Run(() => BPAStopTest(_handle), ct).ConfigureAwait(false);
                    return new BpTestRunResult(lastStatus, sequence.ToArray(), peak, inflateTime, deflateTime, totalTime);
                }

                if (await Task.WhenAny(doneTcs.Task, Task.Delay(totalTimeoutMs, ct)).ConfigureAwait(false) != doneTcs.Task)
                {
                    _log.Warn($"BPA700Beta：血压测试整轮超时（{totalTimeoutMs / 1000}s），强制停止");
                    await Task.Run(() => BPAStopTest(_handle), ct).ConfigureAwait(false);
                }

                return new BpTestRunResult(lastStatus, sequence.ToArray(), peak, inflateTime, deflateTime, totalTime);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>停止当前模拟</summary>
        public async Task StopSimulationAsync(CancellationToken ct = default)
        {
            if (!_connected) return;

            await Task.Run(() =>
            {
                BPAStopTest(_handle);
            }, ct).ConfigureAwait(false);
            _log.Info("BPA700Beta：已停止模拟");
        }

        /// <summary>慢速放气阀开关（BP-007 匀速放气用）</summary>
        public async Task SetSlowValveAsync(bool open, CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700Beta 未连接");
            var ok = await Task.Run(() => BPAEnableAirPort(_handle, (int)BPAValve.SlowValve, open), ct).ConfigureAwait(false);
            if (!ok) _log.Warn($"BPA700Beta：慢速放气阀 {(open ? "打开" : "关闭")} 调用失败");
            else _log.Info($"BPA700Beta：慢速放气阀已{(open ? "打开" : "关闭")}（下一次测试调用时生效）");
        }

        /// <summary>快速放气阀开关（BP-008 快释放气用）</summary>
        public async Task SetFastValveAsync(bool open, CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700Beta 未连接");
            var ok = await Task.Run(() => BPAEnableAirPort(_handle, (int)BPAValve.FastValve, open), ct).ConfigureAwait(false);
            if (!ok) _log.Warn($"BPA700Beta：快速放气阀 {(open ? "打开" : "关闭")} 调用失败");
            else _log.Info($"BPA700Beta：快速放气阀已{(open ? "打开" : "关闭")}（下一次测试调用时生效）");
        }

        /// <summary>
        /// 接口成员：泄放回零（点后/模块结束残压释放）。BPA700 阀态在下一次 SDK 测试调用才生效，
        /// 此处按序列点间泄压同款尽力泄：快阀开 3s 后关；若仍有残压由下一次测试调用内的泄压补偿。
        /// </summary>
        public async Task<bool> VentToZeroAsync(int timeoutSeconds = 60, CancellationToken ct = default)
        {
            await SetFastValveAsync(true, ct).ConfigureAwait(false);
            await Task.Delay(3000, ct).ConfigureAwait(false);
            await SetFastValveAsync(false, ct).ConfigureAwait(false);
            return true;
        }

        /// <summary>仪器过压保护测试（保留本能力）</summary>
        public async Task<double> RunOverpressureTestAsync(
            double pressure, double threshold1, double threshold2,
            bool inflation = true, int timeoutMs = 120_000, CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700Beta 未连接");

            var doneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var peakPressure = 0.0;
            var lastStatus = -1;
            double inflateTime = 0, deflateTime = 0, totalTime = 0, overTime = 0, over10Time = 0;

            _overpressureCallback = (handle, status, waitTime, peak, inflatableTime, deflationTime, total, ovT, ov10T) =>
            {
                if (peak > peakPressure) peakPressure = peak;
                inflateTime = inflatableTime;
                deflateTime = deflationTime;
                totalTime = total;
                overTime = ovT;
                over10Time = ov10T;
                if (status != lastStatus)
                {
                    _log.Info($"BPA700Beta 过压测试回调：status={status} wait={waitTime:0.0} peak={peak:0.0}");
                    lastStatus = status;
                }
                else
                {
                    _log.Debug($"BPA700Beta 过压测试回调：status={status} wait={waitTime:0.0} peak={peak:0.0}");
                }
                if (status == 4 || (status == 0 && peakPressure > threshold1))
                    doneTcs.TrySetResult(true);
            };

            var success = await Task.Run(
                () => BPARunOverpressureTest(_handle, inflation, pressure, threshold1, threshold2, _overpressureCallback), ct)
                .ConfigureAwait(false);
            if (!success)
            {
                _log.Error($"BPA700Beta：BPARunOverpressureTest(inflation={inflation}, {pressure}/{threshold1}/{threshold2}) 调用失败");
                throw new IOException("BPA700Beta 过压保护测试启动失败");
            }

            _log.Info($"BPA700Beta：过压保护测试充气中（目标 {pressure}，阈值 {threshold1}/{threshold2}）...");

            if (await Task.WhenAny(doneTcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false) != doneTcs.Task)
            {
                await Task.Run(() => BPAStopTest(_handle), ct).ConfigureAwait(false);
                _log.Error($"BPA700Beta：过压保护测试 {timeoutMs / 1000}s 未完成（peak={peakPressure:0.0}），已 BPAStopTest 强制结束");
                throw new TimeoutException($"BPA700Beta 过压保护测试超时（{timeoutMs / 1000}s）");
            }

            _log.Info($"BPA700Beta：过压保护已触发，峰值压力 {peakPressure:0.0}，" +
                      $"充气 {inflateTime:0.0}s 放气 {deflateTime:0.0}s 总 {totalTime:0.0}s，overTime={overTime:0.0} over10Time={over10Time:0.0}");
            return peakPressure;
        }

        /// <summary>获取设备序列号</summary>
        public string GetSerialNumber()
        {
            if (!_connected) return string.Empty;
            var ptr = BPAGetSerialNumber(_handle);
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? "" : "";
        }

        /// <summary>获取 SDK 版本</summary>
        public string GetSdkVersion()
        {
            var ptr = BPAGetVersion();
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? "" : "";
        }

        /// <summary>BPA700 SDK 内部处理连接保活，无需外部心跳</summary>
        public Task RespondToHeartbeatAsync(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>释放连接并关闭设备</summary>
        public void Dispose()
        {
            if (_connected)
            {
                try
                {
                    BPAStopSampling(_handle);
                    BPAStopTest(_handle);
                    BPAFree(_handle);
                }
                catch { }
                _connected = false;
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>进程级兜底释放设备控制</summary>
        public static void ReleaseDeviceControl()
        {
            if (!_everConnected) return;

            try
            {
                BPAStopSampling(IntPtr.Zero);
                BPAStopTest(IntPtr.Zero);
                BPAFree(IntPtr.Zero);
            }
            catch { }
            _everConnected = false;
        }

        /// <summary>从 COM 口号提取端口号，如 "COM5" → 5</summary>
        private static uint ExtractPortNumber(string portName)
        {
            var cleaned = portName.Replace("COM", "").Replace("com", "").Trim();
            return uint.TryParse(cleaned, out var num) ? num : 0;
        }
    }
}
