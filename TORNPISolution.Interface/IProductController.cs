using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 被测产品（DUT）控制接口
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
        /// 产品信息获取（0x72）：回读 MAC 与嵌软版本。MacText = 前 6 字节空格分隔 hex（如 0C 5A 7A 4E D2 8F），Version = ASCII 嵌软版本；无响应时两者均为空串。
        /// </summary>
        Task<(string MacText, string Version)> QueryProductInfoAsync(CancellationToken ct = default);

        /// <summary>产品当前状态查询（0x74）：读取测试模式和状态</summary>
        Task<string> QueryProductStatusAsync(CancellationToken ct = default);

        /// <summary>
        /// 读设备端状态（0x74）原始两字节（测试模式 + 测试状态）供按字节判定：短按启动键后应为 03 01=测量中、短按停止键后应为 03 00=待机；无响应或回包长度不足 2 字节返回 null，由调用方按"读不到"处理。
        /// </summary>
        Task<(byte TestMode, byte TestState)?> ReadDeviceStateAsync(CancellationToken ct = default);

        /// <summary>设置测试模式（0x73）：每次测量前必须先设置对应模式；协议参数 2 字节（mode + status）</summary>
        /// <param name="mode">0=空闲 1=袖带充放气 2=血压测试 3=静态压力 4=血氧 5=心电 6=加速度</param>
        /// <param name="status">0=进入测量 1=退出测量（默认 0）</param>
        Task SetTestModeAsync(byte mode, byte status = 0, CancellationToken ct = default);

        /// <summary>启动压力传感器（0x07 0x65）：产品进入压力静态测试态，静态压力校准流程的前置命令</summary>
        Task StartPressureSensorAsync(CancellationToken ct = default);

        /// <summary>校准压力传感器（0x07 0x66 + uint16 LE 校准压力）：仪器稳压到位后逐点发送</summary>
        Task CalibratePressureSensorAsync(ushort pressureMmHg, CancellationToken ct = default);

        /// <summary>关闭压力传感器（0x07 0x67）：校准模块全部测试项结束后发送，归还产品常态</summary>
        Task ClosePressureSensorAsync(CancellationToken ct = default);

        /// <summary>读取产品当前静态压力值（0x76 读产品压力值）：BP-003 用，产品传感器检测到的 BPA700 施加稳压值（mmHg）</summary>
        Task<double> ReadProductPressureAsync(CancellationToken ct = default);

        /// <summary>读取产品血压测量最终结果（0x7B）：BP-004/BP-005 用，返回收缩压/舒张压/平均压（心率由 0x7C 单独读回）</summary>
        Task<ProductBpReading?> ReadBpResultAsync(CancellationToken ct = default);

        /// <summary>
        /// 等待产品主动推送的血压测量结果（控制码 0x7B，超时 60s）：实测当前固件不主动推 0x7B（测量期间只周期推 0xCA，协议未定义），属预留接口，BP-004/005 走主动轮询。
        /// </summary>
        Task<ProductBpReading?> WaitForBpPushResultAsync(int timeoutMs = 60000, CancellationToken ct = default);

        /// <summary>开始/关闭一次完整血压测量（0x79，参数 0=开始 1=关闭；协议要求测完后发一次关闭才能再次开始）</summary>
        Task StartBpMeasureAsync(bool start = true, CancellationToken ct = default);

        /// <summary>
        /// 开始充气（0x75 + 4 字节参数：uint16 LE 目标压力mmHg 在前、uint16 LE 最长充气时间秒在后）：由产品气泵自行充到目标压（仪器不参与加压），超时后产品底层自行放气。
        /// </summary>
        Task StartInflateAsync(ushort targetMmHg, ushort timeoutSeconds, CancellationToken ct = default);

        /// <summary>读取过压状态（0x80，返回 2 字节 uint16：0=无过压 1=过压）：实时状态，无响应回 null</summary>
        Task<bool?> ReadOverPressureStateAsync(CancellationToken ct = default);

        /// <summary>读取 ECG 导联状态（0x88）</summary>
        Task<byte[]> ReadLeadStateAsync(CancellationToken ct = default);

        /// <summary>
        /// 开始 ECG 测试（0x73 设置测试模式 mode=5 心电 status=0 进入测量）：帧 EF 04 05 73 05 00 15 03 FE，产品收到后开始持续推送心电波形（0x8D）。
        /// </summary>
        Task StartEcgTestAsync(CancellationToken ct = default);

        /// <summary>
        /// 结束 ECG 测试（0x73 设置测试模式 mode=5 心电 status=1 退出测量）：帧 EF 04 05 73 05 01 D4 C3 FE，产品停止推送心电波形。
        /// </summary>
        Task StopEcgTestAsync(CancellationToken ct = default);

        /// <summary>
        /// 采集产品主动推送的心电波形（Func=0x05 / Ctrl=0x8D，每帧 75 点 = 300ms @ 250SPS）：整段窗口（skipMs + durationMs）独占串口，期间不得并发收发其他命令。
        /// </summary>
        /// <param name="durationMs">有效记录时长（毫秒）</param>
        /// <param name="skipMs">先丢弃不记录的时长（毫秒）：避开刚进入心电测量的起始瞬态，回调里的帧时刻已含本偏移量</param>
        /// <param name="onFrame">每解出一帧回调（帧首点绝对时刻 秒（含 skipMs 偏移），该帧电压数组 mV）；后台线程调用</param>
        /// <returns>实际记录到的采样点数（不含丢弃段）；0 = 未收到任何波形帧</returns>
        Task<int> CaptureEcgWaveAsync(int durationMs, int skipMs = 0, Action<double, double[]>? onFrame = null, CancellationToken ct = default);

        /// <summary>读取心率测量值（0x7C）</summary>
        Task<double> ReadHeartRateAsync(CancellationToken ct = default);

        /// <summary>开始/停止血氧测量（0x81，参数 0=开始 1=停止）</summary>
        Task StartSpo2MeasureAsync(bool start = true, CancellationToken ct = default);

        /// <summary>读取血氧测量值（0x82，SPO2-001 用）</summary>
        Task<double> ReadSpo2ValueAsync(CancellationToken ct = default);

        /// <summary>读取电池信息（0x6C）：返回电池电压（mV，uint32 LE）和百分比（%）</summary>
        Task<BatteryReading?> ReadBatteryInfoAsync(CancellationToken ct = default);

        /// <summary>读取充电状态（0x6D）：返回是否正在充电（1=充电中，0=未充电）</summary>
        Task<byte> ReadChargingStatusAsync(CancellationToken ct = default);

        /// <summary>通用命令发送：按协议命令配置（protocol_commands.json）里的功能码/控制码下发任意命令，供未封装成专用方法的命令使用</summary>
        /// <param name="cmd">命令标识（功能码 + 控制码）</param>
        /// <param name="data">参数数据（可为 null）</param>
        /// <param name="timeoutMs">超时（毫秒，0 表示用默认）</param>
        /// <returns>响应帧 payload；无响应返回 null</returns>
        Task<byte[]?> SendRawCommandAsync(ProductCmd cmd, byte[]? data = null, int timeoutMs = 0, CancellationToken ct = default);
    }
}
