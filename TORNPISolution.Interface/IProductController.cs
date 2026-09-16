using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 被测产品（DUT）控制接口：通过串口 115200 8N1 二进制协议与产品通讯。
    /// 帧结构：[Head 0xEF] [Len] [Func] [Ctrl] [Data...] [CRC16-MODBUS 2B] [Tail 0xFE]
    /// </summary>
    public interface IProductController : IDisposable
    {
        /// <summary>串口是否已打开</summary>
        bool IsOpen { get; }

        /// <summary>打开与产品的串口连接</summary>
        Task OpenAsync(CancellationToken ct = default);

        /// <summary>关闭串口连接</summary>
        Task CloseAsync();

        /// <summary>心跳保活（0x01）：防止产品进入休眠模式（约10秒无通讯即休眠）</summary>
        Task KeepAliveAsync(CancellationToken ct = default);

        /// <summary>产品问询（0x72）：读取 MAC 地址和嵌软版本</summary>
        Task<string> QueryProductInfoAsync(CancellationToken ct = default);

        /// <summary>产品当前状态查询（0x74）：读取测试模式和状态</summary>
        Task<string> QueryProductStatusAsync(CancellationToken ct = default);

        /// <summary>设置测试模式（0x73）：每次测量前必须先设置对应模式</summary>
        /// <param name="mode">0=空闲 1=袖带充放气 2=血压测试 3=静态压力 4=血氧 5=心电 6=加速度</param>
        Task SetTestModeAsync(byte mode, CancellationToken ct = default);

        /// <summary>
        /// 读取产品当前静态压力值（0x7E）。
        /// BP-003 用：产品传感器检测到的 BPA700 施加的稳压值（mmHg）。
        /// </summary>
        Task<double> ReadProductPressureAsync(CancellationToken ct = default);

        /// <summary>
        /// 读取产品血压测量最终结果（0x7B）。
        /// BP-004/BP-005 用：返回收缩压/舒张压/心率。
        /// </summary>
        Task<ProductBpReading?> ReadBpResultAsync(CancellationToken ct = default);

        /// <summary>
        /// 等待产品主动推送的血压测量结果（控制码 0x7B，超时 60s）。
        /// </summary>
        Task<ProductBpReading?> WaitForBpPushResultAsync(int timeoutMs = 60000, CancellationToken ct = default);

        /// <summary>开始血压测量（0x79）</summary>
        Task StartBpMeasureAsync(CancellationToken ct = default);

        /// <summary>停止血压测量（0x7D）</summary>
        Task StopBpMeasureAsync(CancellationToken ct = default);

        /// <summary>读取 ECG 导联状态（0x88）</summary>
        Task<byte[]> ReadLeadStateAsync(CancellationToken ct = default);

        /// <summary>读取心率测量值（0x7C）</summary>
        Task<double> ReadHeartRateAsync(CancellationToken ct = default);

        /// <summary>开始/停止血氧测量（0x80）</summary>
        Task StartSpo2MeasureAsync(bool start = true, CancellationToken ct = default);

        /// <summary>读取血氧测量值（0x81，SPO2-001 用）</summary>
        Task<double> ReadSpo2ValueAsync(CancellationToken ct = default);

        /// <summary>
        /// 读取电池信息（0x6C）：返回电池电压（mV，uint32 LE）和百分比（%）。
        /// </summary>
        Task<BatteryReading?> ReadBatteryInfoAsync(CancellationToken ct = default);

        /// <summary>
        /// 读取充电状态（0x6D）：返回是否正在充电（1=充电中，0=未充电）。
        /// </summary>
        Task<byte> ReadChargingStatusAsync(CancellationToken ct = default);
    }
}
