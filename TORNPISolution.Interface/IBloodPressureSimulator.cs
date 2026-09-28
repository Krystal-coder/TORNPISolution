using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 血压模拟仪公共接口：
    /// 康伟达（串口 232）和鲸扬 BPA700（USB VCP）均实现此接口，
    /// 上层通过 SimulatorType 选择实例化哪个实现。
    /// </summary>
    public interface IBloodPressureSimulator : IDisposable
    {
        /// <summary>串口是否已打开</summary>
        bool IsOpen { get; }

        /// <summary>打开串口连接</summary>
        Task OpenAsync(CancellationToken ct = default);

        /// <summary>关闭串口连接</summary>
        Task CloseAsync();

        /// <summary>
        /// 初始化/复位模拟仪。
        /// 康伟达：发送功能 ID 0x01 帧。
        /// </summary>
        Task ResetAsync(CancellationToken ct = default);

        /// <summary>
        /// 开始血压模拟。
        /// 康伟达：发送功能 ID 0x02 帧，包含模式与参数。
        /// </summary>
        /// <param name="systolic">目标收缩压 mmHg</param>
        /// <param name="diastolic">目标舒张压 mmHg</param>
        /// <param name="heartRate">心率 bpm</param>
        Task StartSimulationAsync(int systolic, int diastolic, int heartRate, CancellationToken ct = default);

        /// <summary>
        /// 静态压力测试：设置恒定标准压力并保持稳定（用于静态压力精度测试）。
        /// BPA700：调用 BPARunStaticPressure，等待压力稳定后返回；
        /// 康伟达：发送压力源帧（0x03 功能 3）并等待 AA 回读收敛。
        /// </summary>
        /// <param name="pressure">标准压力值。BPA700 单位跟随仪器当前压力单位设置（mmHg 20~400 / kPa 2.7~53.3）；康伟达固定 mmHg</param>
        /// <param name="stableSeconds">稳压保持时间（秒）</param>
        Task SetStaticPressureAsync(double pressure, int stableSeconds = 5, CancellationToken ct = default);

        /// <summary>
        /// 逐点执行静态压力序列（BP-003 静态压力精度测试用）。
        /// BPA700：SDK 序列 API 一次下发，切换点不掉压；
        /// 康伟达：逐点压力源+AA 回读收敛，点间自动放气回零。
        /// </summary>
        /// <param name="points">启用且已排序的压力点（mmHg）</param>
        /// <param name="stableSeconds">每点稳压时长（秒）</param>
        /// <param name="inflation">是否由模拟仪充气（false=依赖外部气源）</param>
        /// <returns>每个压力点稳压后的实测均值（mmHg），未到达的点为 NaN</returns>
        Task<double[]> RunStaticPressureSequenceAsync(double[] points, int stableSeconds, bool inflation, CancellationToken ct = default);

        /// <summary>
        /// 一轮完整血压测量（BP-001/002/004/005）：下发模拟参数后等待被测产品完成充放气周期，
        /// 返回统一的状态序列与时间参数。
        /// BPA700：BPARunBPTest 回调状态机（Wait→Busy→Stable→Finish）；
        /// 康伟达：AA 压力曲线判定等价映射（充气上升=Busy、脉搏波平台=Stable、泄压归零=Finish、充气中骤降=Idle）。
        /// </summary>
        /// <param name="waitStartTimeoutMs">等待产品启动充气（离开 Wait）的超时</param>
        /// <param name="totalTimeoutMs">整轮测量总超时</param>
        Task<BpTestRunResult> RunBpMeasurementAsync(
            int systolic, int diastolic, int heartRate,
            int waitStartTimeoutMs = 30_000, int totalTimeoutMs = 60_000,
            CancellationToken ct = default);

        /// <summary>
        /// 静态压力测试（带充气方式与超时，BP-006~008 步骤用）：设定压力并等待到位。
        /// 康伟达：inflation 参数无意义（气路始终由设备自身打气），超时后记警告不抛异常。
        /// </summary>
        Task SetStaticPressureAsync(double pressure, int stableSeconds, bool inflation, CancellationToken ct, int timeoutSeconds = 180);

        /// <summary>
        /// 只发起静态压力测试不等稳压完成（使阀状态生效用，BP-007/008）。
        /// 康伟达：inflation=false 时为空操作（泄放由阀控方法的后台回路执行）。
        /// </summary>
        Task StartStaticPressureAsync(double pressure, int stableSeconds, bool inflation, CancellationToken ct = default);

        /// <summary>等待静态压力测试 Finish 信号（康伟达无异步测试态，空操作）</summary>
        Task WaitStaticPressureFinishAsync(int timeoutMs = 60_000, CancellationToken ct = default);

        /// <summary>最近一次静态压力稳压后的实测值（BP-006 读回判定用）</summary>
        double LastStaticPressure { get; }

        /// <summary>
        /// 慢释阀（匀速放气，BP-007）。
        /// BPA700：SDK 双路阀直接开关；
        /// 康伟达：单放气阀脉宽调制合成匀速泄放（后台回路，开阀脉冲→关阀读压→AA 上抛采样）。
        /// </summary>
        Task SetSlowValveAsync(bool open, CancellationToken ct = default);

        /// <summary>
        /// 快释阀（快速归零，BP-008）。
        /// BPA700：SDK 双路阀直接开关；
        /// 康伟达：单放气阀分段脉冲归零（后台回路，同 VentToZeroAsync 策略并持续上抛采样）。
        /// </summary>
        Task SetFastValveAsync(bool open, CancellationToken ct = default);

        /// <summary>
        /// 过压保护测试（BP-009）：持续充气至产品触发保护泄压，返回触发时峰值压力。
        /// 康伟达：压力源充向目标压力，AA 监测峰值后的骤降判产品保护动作。
        /// </summary>
        Task<double> RunOverpressureTestAsync(
            double pressure, double threshold1, double threshold2,
            bool inflation = true, int timeoutMs = 120_000, CancellationToken ct = default);

        /// <summary>停止模拟（发送复位或停止指令）</summary>
        Task StopSimulationAsync(CancellationToken ct = default);

        /// <summary>
        /// 压力采样数据事件（波形数据源，单位 mmHg）。
        /// BPA700：由 SDK 的 BPAStartSampling 回调驱动；
        /// 康伟达：压力表/压力源/血压模拟模式下设备持续流式回传 AA 帧（约 2Hz）驱动。
        /// </summary>
        event Action<double>? PressureSampled;

        /// <summary>
        /// 静态压力序列（BP-003）中每个压力点到达稳压保持时触发：index=点序号，pressure=仪器稳压读回值。
        /// 上层在稳压保持窗内并发回读产品上报值（实现可能从设备回调线程触发，订阅方不得阻塞）。
        /// </summary>
        event Action<int, double>? PressurePointStable;

        /// <summary>开始压力采样（波形采集）</summary>
        Task StartSamplingAsync(CancellationToken ct = default);

        /// <summary>停止压力采样</summary>
        Task StopSamplingAsync();

        /// <summary>
        /// 响应连接验证（心跳）。
        /// 康伟达：下位机空闲时发 0x09 询问，须 7 秒内原样回复 0x09 否则设备断开；
        /// 读循环已自动应答，此方法仅为长阻塞流程手动补发用。
        /// </summary>
        Task RespondToHeartbeatAsync(CancellationToken ct = default);
    }
}
