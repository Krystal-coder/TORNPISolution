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
        /// 康伟达：发送功能 ID 0x04 压力测试帧。
        /// </summary>
        /// <param name="pressure">标准压力值。BPA700 单位跟随仪器当前压力单位设置（mmHg 20~400 / kPa 2.7~53.3）；康伟达固定 mmHg</param>
        /// <param name="stableSeconds">稳压保持时间（秒）</param>
        Task SetStaticPressureAsync(double pressure, int stableSeconds = 5, CancellationToken ct = default);

        /// <summary>停止模拟（发送复位或停止指令）</summary>
        Task StopSimulationAsync(CancellationToken ct = default);

        /// <summary>
        /// 压力采样数据事件（波形数据源，单位 mmHg）。
        /// BPA700：由 SDK 的 BPAStartSampling 回调驱动，可用于实时波形显示；
        /// 康伟达：不支持采样，订阅后不会收到数据。
        /// </summary>
        event Action<double>? PressureSampled;

        /// <summary>开始压力采样（波形采集）</summary>
        Task StartSamplingAsync(CancellationToken ct = default);

        /// <summary>停止压力采样</summary>
        Task StopSamplingAsync();

        /// <summary>
        /// 响应连接验证（心跳）。
        /// 康伟达：设备发送 0x03 询问，主机回复 0x09；
        /// 若 7 秒内未回复则设备自动断开。
        /// </summary>
        Task RespondToHeartbeatAsync(CancellationToken ct = default);
    }
}
