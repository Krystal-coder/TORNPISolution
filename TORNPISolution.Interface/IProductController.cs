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

        /// <summary>进入产测模式（0x07/0x01）：打开串口后立即发送防产品休眠（约10秒无通讯即休眠），须在 0x72/0x74 之前发送</summary>
        Task EnterProductionTestModeAsync(CancellationToken ct = default);

        /// <summary>
        /// 产品信息获取（0x72）：回读 MAC 地址与嵌软版本。
        /// MacText = 前 6 字节空格分隔 hex（如 0C 5A 7A 4E D2 8F），Version = ASCII 嵌软版本；
        /// 无响应时两者均为空串。
        /// </summary>
        Task<(string MacText, string Version)> QueryProductInfoAsync(CancellationToken ct = default);

        /// <summary>产品当前状态查询（0x74）：读取测试模式和状态</summary>
        Task<string> QueryProductStatusAsync(CancellationToken ct = default);

        /// <summary>设置测试模式（0x73）：每次测量前必须先设置对应模式；协议参数 2 字节（mode + status）</summary>
        /// <param name="mode">0=空闲 1=袖带充放气 2=血压测试 3=静态压力 4=血氧 5=心电 6=加速度</param>
        /// <param name="status">0=进入测量 1=退出测量（默认 0）</param>
        Task SetTestModeAsync(byte mode, byte status = 0, CancellationToken ct = default);

        /// <summary>
        /// 读取产品当前静态压力值（0x7E）。
        /// BP-003 用：产品传感器检测到的 BPA700 施加的稳压值（mmHg）。
        /// </summary>
        Task<double> ReadProductPressureAsync(CancellationToken ct = default);

        /// <summary>
        /// 读取产品血压测量最终结果（0x7B）。
        /// BP-004/BP-005 用：返回收缩压/舒张压/平均压（心率由 0x7C 单独读回）。
        /// </summary>
        Task<ProductBpReading?> ReadBpResultAsync(CancellationToken ct = default);

        /// <summary>
        /// 等待产品主动推送的血压测量结果（控制码 0x7B，超时 60s）。
        /// 预留：实测当前固件不主动推 0x7B（测量期间只周期推 0xCA，协议未定义），BP-004/005 已改主动轮询。
        /// </summary>
        Task<ProductBpReading?> WaitForBpPushResultAsync(int timeoutMs = 60000, CancellationToken ct = default);

        /// <summary>开始/关闭一次完整血压测量（0x79，参数 0=开始 1=关闭；协议要求测完后发一次关闭才能再次开始）</summary>
        Task StartBpMeasureAsync(bool start = true, CancellationToken ct = default);

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

        /// <summary>
        /// 通用命令发送：按协议命令配置（protocol_commands.json）发送任意命令并返回响应 payload。
        /// 命令的功能码/控制码完全取自配置，供新增命令/实机联调使用，无需为每条命令写专用方法。
        /// </summary>
        /// <param name="cmd">命令标识（功能码 + 控制码）</param>
        /// <param name="data">参数数据（可为 null）</param>
        /// <param name="timeoutMs">超时（毫秒，0 表示用默认）</param>
        /// <returns>响应帧 payload；无响应返回 null</returns>
        Task<byte[]?> SendRawCommandAsync(ProductCmd cmd, byte[]? data = null, int timeoutMs = 0, CancellationToken ct = default);
    }
}
