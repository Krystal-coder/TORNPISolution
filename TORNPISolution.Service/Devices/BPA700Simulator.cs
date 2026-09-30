using System.Runtime.InteropServices;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 鲸扬 BPA700 无创血压模拟仪控制类。
    /// 通过 P/Invoke 调用 BPA700x64.dll 原生 SDK，
    /// 不走串口协议，SDK 内部处理 USB VCP 通讯。
    ///
    /// SDK 关键 API：
    ///   BPAConnect(portNumber, timeout)  → 连接设备
    ///   BPARunBPTest(envelope, callback) → 执行血压模拟
    ///   BPAStopTest()                    → 停止测试
    ///   BPAFree()                        → 释放资源
    /// </summary>
    public class BPA700Simulator : IBloodPressureSimulator
    {
        #region 原生 SDK P/Invoke 声明

        private const string DllName = "BPA700x64.dll";

        /// <summary>
        /// 连接状态回调
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ConnectedCallbackDelegate([MarshalAs(UnmanagedType.I1)] bool connected);

        /// <summary>
        /// 血压测试结果回调
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void BPTestCallbackDelegate(
            int status, double peakPressure, double inflatableTime,
            double deflationTime, double totalTime);

        /// <summary>
        /// BPCommon 结构体（默认血压模拟参数）
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct BPCommon
        {
            public int MethodType;          // 0 = Default
            public double Systolic;         // 收缩压 25~300 mmHg
            public double Diastolic;        // 舒张压 10~250 mmHg
            public uint HeartRate;          // 心率 30~300 BPM
            public double PulseVolume;      // 脉搏容量 0.1~2.0 ml
            public double EnvelopSystolic;  // 包络收缩压偏移 -20~20 mmHg
            public double EnvelopDiastolic; // 包络舒张压偏移 -20~20 mmHg
        }

        /// <summary>
        /// 静态压力测试回调：status 见 BPAStatus（0=Idle 1=Wait 2=Busy 3=Stable 4=Finish），
        /// waitTime 为 Wait 状态时需要等待的秒数
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StaticPressureExCallbackDelegate(int status, double waitTime, int index);

        /// <summary>
        /// 压力采样回调：每个采样点推送一次当前压力值（mmHg）
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SamplingCallbackDelegate(double data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPAConnect(uint portNumber, uint millisecondsTimeout);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void BPAFree();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPARebaseZero([MarshalAs(UnmanagedType.I1)] bool enable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPARunBPTest(IntPtr envelope, BPTestCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPAStopTest();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BPAGetSerialNumber();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BPAGetVersion();

        // 三项初始化设置的下发/读回（签名已按官方 BPA700.h 1.0.3.1 核对）：
        // 压力单位：BPASetPressureUnit / BPAGetPressureUnit（0=mmHg 1=kPa）
        // 患者类型：BPASwitchToAdultMode(enable)（true=Adult，false=Neonatal，双向可切）
        // 袖带容量：BPASwitchTo500mlMode(enable) / BPAIs500mlMode（true=500ml，false=100ml，双向可切）
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPASetPressureUnit(int unit);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int BPAGetPressureUnit();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPASwitchToAdultMode([MarshalAs(UnmanagedType.I1)] bool enable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPASwitchTo500mlMode([MarshalAs(UnmanagedType.I1)] bool enable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPAIs500mlMode();

        // 静态压力测试：pressure 20~400mmHg，time 稳压保持 0~999 秒，inflation=true 由 BPA700 自行充气，
        // false=由外部设备充气（无"放气模式"，放气靠 SlowValve/FastValve 气阀，见 BPAEnableAirPort）
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPARunStaticPressure(double pressure, int time, StaticPressureExCallbackDelegate cb, [MarshalAs(UnmanagedType.I1)] bool inflation);

        // 气阀控制（官方 BPA700.h）：Port1=0, Port2=1, FastValve=3 快速放气阀, SlowValve=4 慢速放气阀。
        // 开关在测试开始前设置，下一次调用测试函数时才实际生效
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPAEnableAirPort(int portNumber, [MarshalAs(UnmanagedType.I1)] bool onOff);

        private const int ValveFastRelease = 3;   // BPAValve.FastValve 快速放气阀
        private const int ValveSlowRelease = 4;   // BPAValve.SlowValve 慢速放气阀

        // 过压保护测试（BP-009）：官方 BPA700.h 签名为 (bool inflation, double pressure, double threshold1, double threshold2, cb)；
        // 回调共 8 个参数，peakPressure 即触发保护时的峰值压力（测试值），overTime/over10Time 为超时类结果参数
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void OverpressureTestCallbackDelegate(int status, double waitTime, double peakPressure, double inflatableTime, double deflationTime, double totalTime, double overTime, double over10Time);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPARunOverpressureTest([MarshalAs(UnmanagedType.I1)] bool inflation, double pressure, double threshold1, double threshold2, OverpressureTestCallbackDelegate cb);

        // 压力采样：开始/停止/采样率（波形数据源）
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool BPAStartSampling(SamplingCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void BPAStopSampling();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int BPAGetSampleRate();

        #endregion

        private readonly ILogService _log;
        private readonly string _portName;
        private bool _connected;

        // 保持回调委托引用，防止被 GC 回收导致崩溃
        private BPTestCallbackDelegate? _bpTestCallback;
        private StaticPressureExCallbackDelegate? _staticPressureCallback;
        private SamplingCallbackDelegate? _samplingCallback;
        private OverpressureTestCallbackDelegate? _overpressureCallback;
        private TaskCompletionSource<bool>? _testCompletionSource;

        // 静态压力测试：Finish(4) 信号（上一轮未 Finish 前再调 BPARunStaticPressure 会被设备拒绝）
        private TaskCompletionSource<bool>? _staticFinishTcs;

        // 压力采样近期窗口（算平均稳压值用，200Hz 下 100 点 ≈ 0.5s）
        private readonly Queue<double> _recentSamples = new();
        private readonly object _sampleLock = new();

        /// <summary>
        /// 压力采样数据事件（波形数据源，单位 mmHg），
        /// UI 层订阅此事件即可实时绘制压力波形
        /// </summary>
        public event Action<double>? PressureSampled;

        /// <summary>序列模式下每个压力点到达 Stable 时触发：index=点序号，pressure=稳压读回值；BP-003 用于保持窗内回读产品上报值</summary>
        public event Action<int, double>? PressurePointStable;

        /// <summary>
        /// 创建 BPA700 模拟仪控制器。
        /// </summary>
        /// <param name="portName">USB VCP 映射的 COM 口号，如 "COM5"</param>
        public BPA700Simulator(string portName, ILogService? log = null)
        {
            _portName = portName;
            _log = log ?? new LogService(nameof(BPA700Simulator));
        }

        public bool IsOpen => _connected;

        /// <summary>
        /// 连接 BPA700 设备。
        /// SDK 通过 COM 口号提取端口号进行连接。
        /// </summary>
        public Task OpenAsync(CancellationToken ct = default) => Task.Run(() =>
        {
            if (_connected) return;

            var portNumber = ExtractPortNumber(_portName);
            _log.Info($"BPA700：正在连接 COM{portNumber}...");

            // 超时 5 秒
            var success = BPAConnect(portNumber, 5000);
            if (success)
            {
                _connected = true;
                _everConnected = true;   // 进程级标记：退出时兜底释放，确保仪器不被一直控制
                _log.Info($"BPA700：已连接 COM{portNumber}");
            }
            else
            {
                _log.Error($"BPA700：连接 COM{portNumber} 超时或失败");
                throw new IOException($"BPA700 连接失败（COM{portNumber}）");
            }
        }, ct);

        /// <summary>
        /// 断开连接并释放 SDK 资源
        /// </summary>
        public Task CloseAsync() => Task.Run(() =>
        {
            if (!_connected) return;

            BPAStopSampling();
            BPAStopTest();
            BPAFree();
            _connected = false;
            _log.Info("BPA700：已断开连接");
        });

        /// <summary>
        /// 压力归零（相当于复位）
        /// </summary>  
        public async Task ResetAsync(CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700 未连接");

            await Task.Run(() => BPARebaseZero(true), ct).ConfigureAwait(false);
            _log.Info("BPA700：压力已归零");
        }

        /// <summary>
        /// 应用仪器初始化设置：患者类型（Adult/Neonatal）、压力单位（mmHg/kPa）、袖带容量（500ml/100ml）。
        /// 经 SDK 下发到仪器：模式切换需要仪器执行气路动作，各项之间留出等待时间；
        /// 500ml 切换用读回轮询确认，未生效自动重试一次。单项失败仅记日志不阻断。
        /// 所有等待/轮询均响应停止令牌（ct），保证运行中点"停止"能尽快中断。
        /// </summary>
        public async Task ApplySettingsAsync(string category, string pressureUnit, string airReservoir, CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700 未连接");

            // 患者类型：BPASwitchToAdultMode(true)=Adult，false=Neonatal（官方头文件确认双向可切）
            var adultRtn = BPASwitchToAdultMode(category == "Adult");
            if (adultRtn)
            {
                _log.Info($"BPA700：患者类型已设为 {category}");
                // 模式切换涉及气路阀门动作，等仪器执行完再下发下一项设置，避免被拒
                await Task.Delay(1500, ct).ConfigureAwait(false);
            }
            else
            {
                _log.Warn($"BPA700：切换患者类型 {category} 返回 false（如仪器当前已是该模式可忽略，否则检查连接）");
            }

            // 压力单位：约定 0=mmHg 1=kPa；下发后读回校验
            var unitCode = pressureUnit == "kPa" ? 1 : 0;
            if (BPASetPressureUnit(unitCode))
            {
                var readBack = BPAGetPressureUnit();
                if (readBack == unitCode)
                    _log.Info($"BPA700：压力单位已设为 {pressureUnit}");
                else
                    _log.Warn($"BPA700：压力单位读回 {readBack} 与期望 {unitCode} 不符，请确认 SDK 单位编码");
            }
            else
            {
                _log.Warn($"BPA700：设置压力单位 {pressureUnit} 失败（如签名不符请联系鲸扬确认 SDK）");
            }

            // 袖带容量：BPASwitchTo500mlMode(true)=500ml，false=100ml（双向可切），读回轮询确认，未生效重试一次
            var want500 = airReservoir == "500ml";
            var switchRtn = BPASwitchTo500mlMode(want500);
            var applied = await WaitUntilAsync(() => BPAIs500mlMode() == want500, timeoutMs: 6000, pollMs: 500, ct).ConfigureAwait(false);
            if (!applied)
            {
                _log.Warn($"BPA700：{airReservoir} 切换读回未生效（切换函数返回={switchRtn}），等待后重试一次...");
                await Task.Delay(1500, ct).ConfigureAwait(false);
                switchRtn = BPASwitchTo500mlMode(want500);
                applied = await WaitUntilAsync(() => BPAIs500mlMode() == want500, timeoutMs: 6000, pollMs: 500, ct).ConfigureAwait(false);
            }

            if (applied)
                _log.Info($"BPA700：袖带容量已设为 {airReservoir}（读回确认）");
            else
                _log.Warn($"BPA700：切换 {airReservoir} 未生效（读回 Is500mlMode={BPAIs500mlMode()}，切换函数返回={switchRtn}），请检查仪器状态");

            Category = category;
            PressureUnit = pressureUnit;
            AirReservoir = airReservoir;
            _log.Info($"BPA700：初始化设置应用完成 Category={category} PressureUnit={pressureUnit} AirReservoir={airReservoir}");
        }

        /// <summary>轮询等待条件成立（仪器执行模式切换需要时间，读回确认用），响应停止令牌</summary>
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

        /// <summary>当前生效的患者类型（Adult/Neonatal）</summary>
        public string Category { get; private set; } = "Adult";

        /// <summary>当前生效的压力单位（mmHg/kPa）</summary>
        public string PressureUnit { get; private set; } = "mmHg";

        /// <summary>当前生效的袖带容量（500ml/100ml）</summary>
        public string AirReservoir { get; private set; } = "500ml";

        /// <summary>
        /// 开始血压模拟（调用 BPARunBPTest）。
        /// 使用默认模式（BPCommon 结构体）。
        /// 注意（官方头文件）：Systolic/Diastolic 单位同样跟随仪器当前压力单位设置
        /// （mmHg：25~300/10~250；kPa：3.3~39.9/1.3~33.3）。
        /// </summary>
        /// <param name="systolic">收缩压（单位跟随仪器设置，mmHg 25~300）</param>
        /// <param name="diastolic">舒张压（单位跟随仪器设置，mmHg 10~250）</param>
        /// <param name="heartRate">心率 BPM（30~300）</param>
        public async Task StartSimulationAsync(int systolic, int diastolic, int heartRate, CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700 未连接");

            var bpParams = new BPCommon
            {
                MethodType = 0,            // Default 模式
                Systolic = systolic,
                Diastolic = diastolic,
                HeartRate = (uint)heartRate,
                PulseVolume = 0.5,         // 默认 0.5 ml
                EnvelopSystolic = 0,       // 不偏移
                EnvelopDiastolic = 0
            };

            // 将结构体固定到非托管内存
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<BPCommon>());
            try
            {
                Marshal.StructureToPtr(bpParams, ptr, false);

                _testCompletionSource = new TaskCompletionSource<bool>();

                // 保持回调引用防止 GC
                _bpTestCallback = (status, peakPressure, inflatableTime, deflationTime, totalTime) =>
                {
                    _log.Debug($"BPA700 回调：status={status} peak={peakPressure:0.0} " +
                               $"inflate={inflatableTime:0.0}s deflate={deflationTime:0.0}s total={totalTime:0.0}s");

                    // status: 0=Idle, 1=Wait, 2=Busy, 3=Stable, 4=Finish
                    if (status == 4) // Finish
                    {
                        _testCompletionSource?.TrySetResult(true);
                    }
                };

                var success = BPARunBPTest(ptr, _bpTestCallback);
                if (!success)
                {
                    _log.Error("BPA700：BPARunBPTest 调用失败");
                    throw new IOException("BPA700 血压模拟启动失败");
                }

                _log.Info($"BPA700：开始血压模拟 SYS={systolic} DIA={diastolic} HR={heartRate}");

                // 等待测试完成（异步）
                if (_testCompletionSource != null)
                {
                    await _testCompletionSource.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// 停止当前模拟
        /// </summary>
        public async Task StopSimulationAsync(CancellationToken ct = default)
        {
            if (!_connected) return;

            await Task.Run(() =>
            {
                BPAStopTest();
                _testCompletionSource?.TrySetResult(false);
            }, ct).ConfigureAwait(false);
            _log.Info("BPA700：已停止模拟");
        }

        /// <summary>
        /// 执行一轮血压模拟测试（BP-001/002/004/005）：调用 BPARunBPTest 进入待测状态，
        /// 等待产品启动/停止测量，完整记录回调 status 序列与时间参数供结果验证。
        /// 与 StartSimulationAsync 的区别：不只看 Finish，还跟踪 Busy/Stable/Idle 全过程。
        /// </summary>
        /// <param name="systolic">收缩压（单位跟随仪器设置）</param>
        /// <param name="diastolic">舒张压（单位跟随仪器设置）</param>
        /// <param name="heartRate">心率 BPM（30~300）</param>
        /// <param name="waitStartTimeoutMs">等待 status 离开 Wait（测试员按产品按键）的最长时间，超时判产品未启动</param>
        /// <param name="totalTimeoutMs">整轮测试超时兜底</param>
        public async Task<BpTestRunResult> RunBPTestAsync(
            int systolic, int diastolic, int heartRate,
            int waitStartTimeoutMs = 30_000, int totalTimeoutMs = 60_000,
            CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700 未连接");

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

                // 保持回调引用防止 GC；status: 0=Idle 1=Wait 2=Busy 3=Stable 4=Finish
                _bpTestCallback = (status, peakPressure, inflatableTime, deflationTime, total)
                    =>
                {
                    lock (sequence) { sequence.Add(status); }
                    lastStatus = status;
                    peak = peakPressure;
                    inflateTime = inflatableTime;
                    deflateTime = deflationTime;
                    totalTime = total;
                    _log.Debug($"BPA700 BP测试回调：status={status} peak={peakPressure:0.0} " +
                               $"inflate={inflatableTime:0.0}s deflate={deflationTime:0.0}s total={total:0.0}s");

                    if (status != 1) startedTcs.TrySetResult(true);   // 离开 Wait：检测到压力变化/被停止
                    if (status == 0 || status == 4) doneTcs.TrySetResult(true);  // Idle（被停止）或 Finish
                };

                if (!BPARunBPTest(ptr, _bpTestCallback))
                {
                    _log.Error("BPA700：BPARunBPTest 调用失败");
                    throw new IOException("BPA700 血压模拟启动失败");
                }

                _log.Info($"BPA700：血压测试待测中 SYS={systolic} DIA={diastolic} HR={heartRate}，等待产品启动测量...");

                // 阶段 1：等 status 离开 Wait（测试员按产品按键）
                if (await Task.WhenAny(startedTcs.Task, Task.Delay(waitStartTimeoutMs, ct)).ConfigureAwait(false) != startedTcs.Task)
                {
                    _log.Warn($"BPA700：{waitStartTimeoutMs / 1000}s 内 status 一直停留在 Wait，产品未启动或气路未连通");
                    await Task.Run(() => BPAStopTest()).ConfigureAwait(false);
                    return new BpTestRunResult(lastStatus, sequence.ToArray(), peak, inflateTime, deflateTime, totalTime);
                }

                // 阶段 2：等 Finish（正常完成）或 Idle（产品按停止键，BP-002）
                if (await Task.WhenAny(doneTcs.Task, Task.Delay(totalTimeoutMs, ct)).ConfigureAwait(false) != doneTcs.Task)
                {
                    _log.Warn($"BPA700：血压测试整轮超时（{totalTimeoutMs / 1000}s），强制停止");
                    await Task.Run(() => BPAStopTest()).ConfigureAwait(false);
                }

                return new BpTestRunResult(lastStatus, sequence.ToArray(), peak, inflateTime, deflateTime, totalTime);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>接口兼容重载：默认 BPA700 自行充气（inflation=true），默认超时 180 秒</summary>
        public Task SetStaticPressureAsync(double pressure, int stableSeconds = 5, CancellationToken ct = default)
            => SetStaticPressureAsync(pressure, stableSeconds, inflation: true, ct);

        /// <summary>
        /// 逐点执行静态压力序列（接口成员；Formal SDK 无一次下发序列 API，
        /// 故单点循环：加压取稳压值 → 等 Finish → 快阀泄压衔接下一点）。
        /// </summary>
        public async Task<double[]> RunStaticPressureSequenceAsync(double[] points, int stableSeconds, bool inflation, CancellationToken ct = default)
        {
            if (points is not { Length: > 0 })
                throw new ArgumentException("压力点不能为空", nameof(points));

            var results = new double[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                results[i] = double.NaN;
                await SetStaticPressureAsync(points[i], stableSeconds, inflation, ct).ConfigureAwait(false);
                results[i] = LastStaticPressure;

                // 逐点稳定事件：上层在稳压保持窗内并发回读产品上报值（不阻塞本序列）
                PressurePointStable?.Invoke(i, LastStaticPressure);

                await WaitStaticPressureFinishAsync(ct: ct).ConfigureAwait(false);

                if (i < points.Length - 1)
                {
                    // 点间快阀泄压，残压降到下一点附近再重新加压
                    await SetFastValveAsync(true, ct).ConfigureAwait(false);
                    await Task.Delay(3000, ct).ConfigureAwait(false);
                    await SetFastValveAsync(false, ct).ConfigureAwait(false);
                }
            }
            return results;
        }

        /// <summary>
        /// 静态压力测试：设置恒定标准压力并等待稳定（BPARunStaticPressure）。
        /// inflation=true 时 BPA700 自行充气到目标压力并保持 stableSeconds 秒；
        /// inflation=false 为由外部设备充气（官方语义，无"放气模式"；放气用 SetSlowValve/SetFastValve 开气阀）。
        /// 回调状态到达 Stable(3) 认为压力到位，方法返回；此时设备上本轮测试仍在激活态（稳压保持），
        /// 调用方做完本点操作后必须调 WaitStaticPressureFinishAsync 等 Finish(4)，
        /// 否则下一次 BPARunStaticPressure 会被设备拒绝（返回 false）。
        /// 等待过程中持续监控实际压力采样值：若压力长时间不变化（≥30s）则记录警告；
        /// 超时后抛出 TimeoutException 并报告实际达到的压力值，便于诊断气路问题。
        /// 实际稳压值取自压力采样回调的近期均值（回调的 waitTime 参数不是压力），记录在 LastStaticPressure；
        /// 未开采样时回退用目标压力值。
        /// 压力值单位跟随仪器当前压力单位设置，
        /// mmHg 时 20~400，kPa 时 2.7~53.3，调用方不要自行换算。
        /// </summary>
        /// <param name="pressure">标准压力值（单位跟随仪器当前设置：mmHg 20~400 / kPa 2.7~53.3）</param>
        /// <param name="stableSeconds">稳压保持时间（秒，0~999）</param>
        /// <param name="inflation">true=BPA700 自行充气；false=由外部设备充气</param>
        /// <param name="timeoutSeconds">充气超时时间（秒），默认 180。设备气路异常时可在配置页增大</param>
        public async Task SetStaticPressureAsync(double pressure, int stableSeconds, bool inflation, CancellationToken ct, int timeoutSeconds = 180)
        {
            var stableReached = await StartStaticPressureTestAsync(pressure, stableSeconds, inflation, ct).ConfigureAwait(false);

            _log.Info($"BPA700：设置静态压力 {pressure}{PressureUnit}，保持 {stableSeconds}s，等待到位（超时 {timeoutSeconds}s）...");

            // 等待压力到位（充气 + 稳压），带压力进度监控
            var timeoutMs = timeoutSeconds * 1000;
            var waitStart = System.Diagnostics.Stopwatch.StartNew();
            double lastLoggedPressure = double.NaN;
            var lastChangeTime = waitStart.Elapsed;
            bool completed = false;
            const double StuckThreshold = 0.5;      // mmHg，低于此变化视为卡住
            const double StuckTimeoutSeconds = 30;  // 卡住 30s 立即失败，不等满超时

            while (waitStart.ElapsedMilliseconds < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();

                // 检查 Stable 信号
                if (stableReached.Task.IsCompleted)
                {
                    completed = true;
                    break;
                }

                // 压力进度监控：从采样读回当前实际压力
                var currentPressure = ReadRecentAveragePressure();
                if (!double.IsNaN(currentPressure))
                {
                    // 检测压力是否卡住（StuckTimeoutSeconds 内变化 < StuckThreshold）
                    if (!double.IsNaN(lastLoggedPressure) && Math.Abs(currentPressure - lastLoggedPressure) < StuckThreshold)
                    {
                        var stagnant = waitStart.Elapsed - lastChangeTime;
                        if (stagnant.TotalSeconds >= StuckTimeoutSeconds)
                        {
                            throw new TimeoutException($"BPA700 静态压力 {pressure}{PressureUnit} 充气卡住（实际压力 {currentPressure:0.1}{PressureUnit}，{stagnant.TotalSeconds:0.0}s 无变化），请检查气路连接");
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
                var pressureInfo = double.IsNaN(actualPressure) ? "无采样数据" : $"实际压力 {actualPressure:0.1}{PressureUnit}";
                throw new TimeoutException($"BPA700 静态压力 {pressure}{PressureUnit} 等待到位超时（{timeoutSeconds}s），{pressureInfo}。请检查气路连接或增大配置页超时时间");
            }

            // 稳压值从采样读回（回调 waitTime 不是压力）：稍等压力稳定后取近期采样均值
            await Task.Delay(800, ct).ConfigureAwait(false);
            var sampled = ReadRecentAveragePressure();
            LastStaticPressure = double.IsNaN(sampled) ? pressure : sampled;
            if (double.IsNaN(sampled))
                _log.Warn("BPA700：压力采样未运行，稳压值回退用目标压力（如需真实读回值请先 StartSamplingAsync）");

            _log.Info($"BPA700：静态压力 {pressure}{PressureUnit} 已到位（采样读回 {LastStaticPressure:0.0}{PressureUnit}）");
        }

        /// <summary>接口成员：启动静态压力测试不等待稳压完成（逻辑同 StartStaticPressureTestAsync）</summary>
        public async Task StartStaticPressureAsync(double pressure, int stableSeconds, bool inflation, CancellationToken ct = default)
            => await StartStaticPressureTestAsync(pressure, stableSeconds, inflation, ct).ConfigureAwait(false);

        /// <summary>接口成员：RunBPTestAsync 的统一入口别名，供上层经接口调用（逻辑不变）</summary>
        public Task<BpTestRunResult> RunBpMeasurementAsync(
            int systolic, int diastolic, int heartRate,
            int waitStartTimeoutMs = 30_000, int totalTimeoutMs = 60_000,
            CancellationToken ct = default)
            => RunBPTestAsync(systolic, diastolic, heartRate, waitStartTimeoutMs, totalTimeoutMs, ct);

        /// <summary>
        /// 只发起一轮静态压力测试并立即返回（不等 Stable），返回 Stable/Finish 到达信号。
        /// 用途：应用气阀状态——BPAEnableAirPort 的开关在下一次调用测试函数时才实际生效，
        /// 例如开慢释阀后发起 inflation=false 的测试，压力即经慢释阀缓慢泄放（BP-007 匀速放气）。
        /// 发起后本轮同样要等 Finish（WaitStaticPressureFinishAsync）或 BPAStopTest 才能再发起下一轮。
        /// </summary>
        public async Task<TaskCompletionSource<bool>> StartStaticPressureTestAsync(double pressure, int stableSeconds, bool inflation, CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700 未连接");

            var stableReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Finish 信号用局部引用：WaitStaticPressureFinishAsync 会把字段置空，
            // 回调若读字段会漏掉 Finish 导致等待超时（历史 bug）
            var finishTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _staticFinishTcs = finishTcs;

            // 保持回调引用防止 GC；status: 0=Idle, 1=Wait, 2=Busy, 3=Stable, 4=Finish
            _staticPressureCallback = (status, waitTime, index) =>
            {
                _log.Debug($"BPA700 静态压力回调：status={status} wait={waitTime:0.0} index={index}");
                if (status == 3 || status == 4)
                    stableReached.TrySetResult(true);
                if (status == 4)
                    finishTcs.TrySetResult(true);   // 本轮测试结束，可开始下一轮
            };

            var success = await Task.Run(() => BPARunStaticPressure(pressure, stableSeconds, _staticPressureCallback, inflation), ct)
                .ConfigureAwait(false);
            if (!success)
            {
                // 紧跟 BPAStopTest 后设备可能处于残留态：先停再延迟重试一次
                _log.Warn($"BPA700：BPARunStaticPressure({pressure}, inflation={inflation}) 首次调用失败，BPAStopTest 后重试");
                await Task.Run(() => BPAStopTest()).ConfigureAwait(false);
                await Task.Delay(1000, ct).ConfigureAwait(false);
                success = await Task.Run(() => BPARunStaticPressure(pressure, stableSeconds, _staticPressureCallback, inflation), ct)
                    .ConfigureAwait(false);
            }
            if (!success)
            {
                _log.Error($"BPA700：BPARunStaticPressure({pressure}, inflation={inflation}) 调用失败（如上一轮未 Finish，请先 WaitStaticPressureFinishAsync）");
                throw new IOException($"BPA700 静态压力设置失败（{pressure}）");
            }

            return stableReached;
        }

        /// <summary>
        /// 等待当前静态压力测试完成（回调 status=Finish）。
        /// 逐点循环时每个压力点读值后必须调用，否则下一轮 BPARunStaticPressure 会被设备拒绝；
        /// 超时未收到 Finish 则主动 BPAStopTest 强制结束，保证后续调用可用。
        /// </summary>
        public async Task WaitStaticPressureFinishAsync(int timeoutMs = 60_000, CancellationToken ct = default)
        {
            var tcs = _staticFinishTcs;
            _staticFinishTcs = null;
            if (tcs == null)
                return;

            if (await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false) != tcs.Task)
            {
                _log.Warn($"BPA700：静态压力测试 {timeoutMs / 1000}s 未收到 Finish，主动 BPAStopTest 结束本轮");
                await Task.Run(() => BPAStopTest()).ConfigureAwait(false);
            }
        }

        /// <summary>读取近期采样窗口均值（无采样时返回 NaN）</summary>
        private double ReadRecentAveragePressure()
        {
            lock (_sampleLock)
            {
                if (_recentSamples.Count == 0)
                    return double.NaN;
                return _recentSamples.Average();
            }
        }

        /// <summary>
        /// 慢速放气阀开关（BPAEnableAirPort SlowValve，BP-007 匀速放气用）。
        /// 注意：开关在下一次调用测试函数时才实际生效，
        /// 典型用法：SetSlowValveAsync(true) 后调 StartStaticPressureTestAsync(..., inflation:false)。
        /// </summary>
        public async Task SetSlowValveAsync(bool open, CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700 未连接");
            var ok = await Task.Run(() => BPAEnableAirPort(ValveSlowRelease, open), ct).ConfigureAwait(false);
            if (!ok) _log.Warn($"BPA700：慢速放气阀 {(open ? "打开" : "关闭")} 调用失败");
            else _log.Info($"BPA700：慢速放气阀已{(open ? "打开" : "关闭")}（下一次测试调用时生效）");
        }

        /// <summary>
        /// 快速放气阀开关（BPAEnableAirPort FastValve，BP-008 快释放气用），
        /// 同样在下一次调用测试函数时生效。
        /// </summary>
        public async Task SetFastValveAsync(bool open, CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700 未连接");
            var ok = await Task.Run(() => BPAEnableAirPort(ValveFastRelease, open), ct).ConfigureAwait(false);
            if (!ok) _log.Warn($"BPA700：快速放气阀 {(open ? "打开" : "关闭")} 调用失败");
            else _log.Info($"BPA700：快速放气阀已{(open ? "打开" : "关闭")}（下一次测试调用时生效）");
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

        /// <summary>静态压力到位后的实际稳压值（采样读回均值，BP-003 标准值 / BP-006 最高充气压力验证用）</summary>
        public double LastStaticPressure { get; private set; }

        /// <summary>
        /// 过压保护测试（BP-009，官方 BPA700.h）：BPARunOverpressureTest(inflation, pressure, threshold1, threshold2, cb)。
        /// BPA700 充气至超过阈值触发保护放气，回调 Finish(4) 时直接回传结果：
        /// peakPressure=触发保护时的峰值压力（测试值），另有充气/放气/总时长及 overTime/over10Time。
        /// 产品需在充气全程实时上报压力（协议预留），若产品未先触发保护则由 BPA700 兜底。
        /// </summary>
        /// <param name="pressure">目标充气压力（如 350）</param>
        /// <param name="threshold1">保护阈值 1（如 300）</param>
        /// <param name="threshold2">保护阈值 2，超过即触发快速放气（如 320）</param>
        /// <param name="inflation">true=BPA700 自行充气；false=外部设备充气</param>
        /// <param name="timeoutMs">整轮超时兜底（过压测试物理上不应超过 2 分钟）</param>
        public async Task<double> RunOverpressureTestAsync(
            double pressure, double threshold1, double threshold2,
            bool inflation = true, int timeoutMs = 120_000, CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700 未连接");

            var doneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var peakPressure = 0.0;
            var lastStatus = -1;
            double inflateTime = 0, deflateTime = 0, totalTime = 0, overTime = 0, over10Time = 0;

            // 保持回调引用防止 GC；签名按官方头文件 8 参数；Finish(4)=本轮结束可取结果
            _overpressureCallback = (status, waitTime, peak, inflatableTime, deflationTime, total, ovT, ov10T) =>
            {
                if (peak > peakPressure) peakPressure = peak;
                inflateTime = inflatableTime;
                deflateTime = deflationTime;
                totalTime = total;
                overTime = ovT;
                over10Time = ov10T;
                if (status != lastStatus)
                {
                    _log.Info($"BPA700 过压测试回调：status={status} wait={waitTime:0.0} peak={peak:0.0}");
                    lastStatus = status;
                }
                else
                {
                    _log.Debug($"BPA700 过压测试回调：status={status} wait={waitTime:0.0} peak={peak:0.0}");
                }
                if (status == 4 || (status == 0 && peakPressure > threshold1))
                    doneTcs.TrySetResult(true);
            };

            var success = await Task.Run(
                () => BPARunOverpressureTest(inflation, pressure, threshold1, threshold2, _overpressureCallback), ct)
                .ConfigureAwait(false);
            if (!success)
            {
                _log.Error($"BPA700：BPARunOverpressureTest(inflation={inflation}, {pressure}/{threshold1}/{threshold2}) 调用失败");
                throw new IOException("BPA700 过压保护测试启动失败");
            }

            _log.Info($"BPA700：过压保护测试充气中（目标 {pressure}{PressureUnit}，阈值 {threshold1}/{threshold2}）...");

            if (await Task.WhenAny(doneTcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false) != doneTcs.Task)
            {
                await Task.Run(() => BPAStopTest()).ConfigureAwait(false);
                _log.Error($"BPA700：过压保护测试 {timeoutMs / 1000}s 未完成（peak={peakPressure:0.0}），已 BPAStopTest 强制结束，请检查回调日志");
                throw new TimeoutException($"BPA700 过压保护测试超时（{timeoutMs / 1000}s）");
            }

            _log.Info($"BPA700：过压保护已触发，峰值压力 {peakPressure:0.0}{PressureUnit}，" +
                      $"充气 {inflateTime:0.0}s 放气 {deflateTime:0.0}s 总 {totalTime:0.0}s，overTime={overTime:0.0} over10Time={over10Time:0.0}");
            return peakPressure;
        }

        /// <summary>
        /// 开始压力采样（波形采集）。
        /// SDK 按自身采样率持续回调 PressureSampled 事件，
        /// UI 层订阅事件即可实时绘制压力波形。
        /// </summary>
        public async Task StartSamplingAsync(CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("BPA700 未连接");

            lock (_sampleLock) _recentSamples.Clear();
            _samplingCallback = data =>
            {
                // 近期窗口保留最近 100 点（200Hz ≈ 0.5s），供稳压均值计算
                lock (_sampleLock)
                {
                    _recentSamples.Enqueue(data);
                    if (_recentSamples.Count > 100) _recentSamples.Dequeue();
                }
                PressureSampled?.Invoke(data);
            };

            var success = await Task.Run(() => BPAStartSampling(_samplingCallback), ct).ConfigureAwait(false);
            if (!success)
                throw new IOException("BPA700 启动压力采样失败");

            _log.Info($"BPA700：开始压力采样（采样率 {BPAGetSampleRate()}Hz）");
        }

        /// <summary>
        /// 停止压力采样
        /// </summary>
        public Task StopSamplingAsync()
        {
            if (!_connected) return Task.CompletedTask;
            return Task.Run(() =>
            {
                BPAStopSampling();
                _log.Info("BPA700：已停止压力采样");
            });
        }

        /// <summary>
        /// BPA700 无需手动心跳（SDK 内部管理连接状态）
        /// </summary>
        public Task RespondToHeartbeatAsync(CancellationToken ct = default)
        {
            // BPA700 SDK 内部处理连接保活，无需外部心跳
            return Task.CompletedTask;
        }

        /// <summary>
        /// 获取设备序列号
        /// </summary>
        public string GetSerialNumber()
        {
            if (!_connected) return string.Empty;
            var ptr = BPAGetSerialNumber();
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? "" : "";
        }

        /// <summary>
        /// 获取 SDK 版本
        /// </summary>  
        public string GetSdkVersion()
        {
            var ptr = BPAGetVersion();
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? "" : "";
        }

        public void Dispose()
        {
            if (_connected)
            {
                BPAStopSampling();
                BPAStopTest();
                BPAFree();
                _connected = false;
            }
            GC.SuppressFinalize(this);
        }

        // ==================== 进程级兜底释放 ====================

        /// <summary>本进程是否连接过 BPA700（App 退出时据此兜底释放）</summary>
        private static volatile bool _everConnected;

        /// <summary>
        /// 进程级释放设备控制：停止采样/测试并 BPAFree 断开连接，
        /// 仪器回到可手动操作状态。由 App 退出时调用，
        /// 保证无论模块 Dispose 是否走到（异常/强关窗口），关程序即断开对仪器的控制。
        /// </summary>
        public static void ReleaseDeviceControl()
        {
            if (!_everConnected)
            {
                return;
            }

            BPAStopSampling();
            BPAStopTest();
            BPAFree();
            _everConnected = false;
        }

        /// <summary>
        /// 从 COM 口号提取端口号，如 "COM5" → 5
        /// </summary>  
        private static uint ExtractPortNumber(string portName)
        {
            var cleaned = portName.Replace("COM", "").Replace("com", "").Trim();
            return uint.TryParse(cleaned, out var num) ? num : 0;
        }
    }
}
