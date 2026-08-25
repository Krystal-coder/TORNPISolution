using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 康伟达 NIBP 无创血压模拟仪控制类。
    /// 协议：16 字节固定帧，串口 232，字节 0 为功能 ID。
    ///
    /// 功能 ID 定义：
    ///   0x01 = 初始化界面 / 复位
    ///   0x02 = 血压模拟
    ///   0x03 = 连接验证（设备主动询问）
    ///   0x04 = 压力测试
    ///   0x05 = 标准检测
    ///
    /// 心跳机制：
    ///   设备长时间无指令时发送 byte[0]=0x03 询问，
    ///   主机须在 7 秒内回复 byte[0]=0x09，否则设备自动断开。
    /// </summary>
    public class KangwendaNIBPSimulator : IBloodPressureSimulator
    {
        #region 协议常量

        /// <summary>帧固定长度（字节）</summary>
        public const int FrameLength = 16;

        // 功能 ID（Byte 0）
        private const byte FuncReset = 0x01;          // 初始化界面 / 复位
        private const byte FuncBPSimulation = 0x02;   // 血压模拟
        private const byte FuncHeartbeatQuery = 0x03; // 连接验证（设备主动发送）
        private const byte FuncHeartbeatReply = 0x09; // 连接验证回复（主机发送）
        private const byte FuncPressureTest = 0x04;   // 压力测试
        private const byte FuncStandardTest = 0x05;   // 标准检测

        // 血压模拟模式（Byte 1）
        /// <summary>标准血压模式 + 参数列表 0~7</summary>
        public const byte ModeStandardBP = 0x00;
        /// <summary>自定义模式 + 参数列表 0~7</summary>
        public const byte ModeCustom = 0x08;
        /// <summary>胃式人状态 + 参数列表 0~7</summary>
        public const byte ModeGastricState = 0x0D;
        /// <summary>胃式心律失常 + 参数列表 0~7</summary>
        public const byte ModeArrhythmia = 0x0E;
        /// <summary>胃式呼吸干扰 + 参数列表 0~7</summary>
        public const byte ModeRespirationInterference = 0x70;

        #endregion

        private readonly SerialClient _serial;
        private readonly ILogService _log;

        /// <summary>
        /// 创建康伟达模拟仪控制器。
        /// 默认 9600bps、8N1（根据设备实际参数调整）。
        /// </summary>
        public KangwendaNIBPSimulator(
            string portName,
            int baudRate = 9600,
            ILogService? log = null)
        {
            _log = log ?? new LogService(nameof(KangwendaNIBPSimulator));
            _serial = new SerialClient(portName, baudRate, System.IO.Ports.Parity.None, 8,
                System.IO.Ports.StopBits.One, responseTimeoutMs: 2000, log: _log);
        }

        public bool IsOpen => _serial.IsOpen;

        public Task OpenAsync(CancellationToken ct = default) => _serial.OpenAsync(ct);

        public Task CloseAsync() => _serial.CloseAsync();

        // ==================== 核心指令 ====================

        /// <summary>
        /// 初始化 / 复位模拟仪（功能 ID 0x01）。
        /// 参数：总测量时间、灌气率（暂用默认值，后续按需扩展）。
        /// </summary>
        public async Task ResetAsync(CancellationToken ct = default)
        {
            var frame = new byte[FrameLength];
            frame[0] = FuncReset;
            // Byte 1~3: 总测量时间高8位、低8位（默认 120 秒 = 2 分钟）
            var totalTime = (ushort)120;
            frame[2] = (byte)(totalTime >> 8);
            frame[3] = (byte)totalTime;
            // Byte 4~5: 灌气率高8位、低8位（默认值）
            var inflationRate = (ushort)30;
            frame[4] = (byte)(inflationRate >> 8);
            frame[5] = (byte)inflationRate;

            await _serial.SendFrameAsync(frame, 0, ct: ct).ConfigureAwait(false);
            _log.Info("康伟达模拟仪：已发送复位指令");
        }

        /// <summary>
        /// 开始血压模拟（功能 ID 0x02，默认标准血压模式 0x00）。
        /// 帧结构（16 字节）：
        ///   [0] = 0x02
        ///   [1] = 模式（默认 0x00 标准血压）
        ///   [2-3] = 动态血压当前测量压力(+10) 高/低8位
        ///   [4-5] = 设置压力值（收缩压）高/低8位
        ///   [6-7] = 稳定压力值（舒张压）高/低8位
        ///   [8]   = 测量时间（分钟）
        ///   [9]   = 测压灵敏度
        ///   [10-11] = 开始气压高/低8位
        ///   [12-13] = 停止气压高/低8位
        ///   [14]  = 是否跳过当前压差（0=不跳过）
        ///   [15]  = 保留
        /// </summary>
        /// <param name="systolic">目标收缩压 mmHg（如 120）</param>
        /// <param name="diastolic">目标舒张压 mmHg（如 80）</param>
        /// <param name="heartRate">心率 bpm（如 72），当前帧结构未直接使用，保留扩展</param>
        public async Task StartSimulationAsync(int systolic, int diastolic, int heartRate, CancellationToken ct = default)
        {
            var frame = new byte[FrameLength];
            frame[0] = FuncBPSimulation;
            frame[1] = ModeStandardBP;

            // 当前测量压力（+10 偏移，初始设为收缩压）
            var currentPressure = (ushort)(systolic + 10);
            frame[2] = (byte)(currentPressure >> 8);
            frame[3] = (byte)currentPressure;

            // 设置压力值（收缩压）
            var sys = (ushort)systolic;
            frame[4] = (byte)(sys >> 8);
            frame[5] = (byte)sys;

            // 稳定压力值（舒张压）
            var dia = (ushort)diastolic;
            frame[6] = (byte)(dia >> 8);
            frame[7] = (byte)dia;

            // 测量时间（分钟），默认 3 分钟
            frame[8] = 3;
            // 测压灵敏度，默认 1
            frame[9] = 1;

            // 开始气压（收缩压附近）
            var startPressure = (ushort)(systolic + 20);
            frame[10] = (byte)(startPressure >> 8);
            frame[11] = (byte)startPressure;

            // 停止气压（舒张压附近）
            var stopPressure = (ushort)(diastolic - 10);
            frame[12] = (byte)(stopPressure >> 8);
            frame[13] = (byte)stopPressure;

            // 不跳过当前压差
            frame[14] = 0;
            // 保留
            frame[15] = 0;

            await _serial.SendFrameAsync(frame, 0, ct: ct).ConfigureAwait(false);
            _log.Info($"康伟达模拟仪：开始血压模拟 SYS={systolic} DIA={diastolic} HR={heartRate}");
        }

        /// <summary>停止模拟（发送复位指令）</summary>
        public Task StopSimulationAsync(CancellationToken ct = default)
        {
            _log.Info("康伟达模拟仪：停止模拟");
            return ResetAsync(ct);
        }

        /// <summary>
        /// 静态压力测试（功能 ID 0x04 压力测试帧）。
        /// 帧结构（16 字节）：
        ///   [0] = 0x04
        ///   [1-2] = 目标压力高/低8位
        ///   [3]   = 稳压保持时间（秒）
        ///   其余保留
        /// </summary>
        /// <param name="pressure">标准压力值 mmHg（康伟达固定 mmHg，小数四舍五入）</param>
        /// <param name="stableSeconds">稳压保持时间（秒）</param>
        public async Task SetStaticPressureAsync(double pressure, int stableSeconds = 5, CancellationToken ct = default)
        {
            var frame = new byte[FrameLength];
            frame[0] = FuncPressureTest;
            var pressureInt = (ushort)Math.Round(pressure);
            frame[1] = (byte)(pressureInt >> 8);
            frame[2] = (byte)pressureInt;
            frame[3] = (byte)stableSeconds;

            await _serial.SendFrameAsync(frame, 0, ct: ct).ConfigureAwait(false);
            _log.Info($"康伟达模拟仪：设置静态压力 {pressureInt}mmHg，稳压 {stableSeconds}s");
        }

        /// <summary>
        /// 压力采样数据事件（康伟达不支持采样，订阅后不会收到数据，
        /// 仅为满足 IBloodPressureSimulator 接口定义，后续波形需求不在此实现）
        /// </summary>
#pragma warning disable CS0067 // 事件从不使用（接口占位，康伟达不支持采样）
        public event Action<double>? PressureSampled;
#pragma warning restore CS0067

        /// <summary>康伟达不支持压力采样（波形），仅记录提示</summary>
        public Task StartSamplingAsync(CancellationToken ct = default)
        {
            _log.Warn("康伟达模拟仪：不支持压力采样（波形），请使用 BPA700");
            return Task.CompletedTask;
        }

        /// <summary>康伟达不支持压力采样，空实现</summary>
        public Task StopSamplingAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 响应连接验证（心跳）。
        /// 设备发送 byte[0]=0x03 询问时，主机回复 byte[0]=0x09。
        /// 若 7 秒内未回复，设备自动断开连接。
        /// </summary>
        public async Task RespondToHeartbeatAsync(CancellationToken ct = default)
        {
            var reply = new byte[FrameLength];
            reply[0] = FuncHeartbeatReply;
            await _serial.SendFrameAsync(reply, 0, ct: ct).ConfigureAwait(false);
            _log.Debug("康伟达模拟仪：已回复心跳验证（0x09）");
        }

        /// <summary>
        /// 监听设备主动发送的心跳询问帧（byte[0]=0x03）。
        /// 收到后自动调用 RespondToHeartbeatAsync 回复。
        /// 建议在后台任务中循环调用此方法以保持连接。
        /// </summary>
        /// <param name="listenTimeoutMs">每次监听超时（毫秒），默认 5000</param>
        /// <returns>本次监听是否收到并处理了心跳</returns>
        public async Task<bool> ListenAndRespondHeartbeatAsync(int listenTimeoutMs = 5000, CancellationToken ct = default)
        {
            var data = await _serial.ListenAsync(listenTimeoutMs, ct).ConfigureAwait(false);
            if (data != null && data.Length > 0 && data[0] == FuncHeartbeatQuery)
            {
                _log.Debug("康伟达模拟仪：收到心跳询问（0x03），准备回复");
                await RespondToHeartbeatAsync(ct).ConfigureAwait(false);
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            _serial.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
