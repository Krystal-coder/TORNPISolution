using System.IO.Ports;
using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// Alicat 质量流量控制器控制类（Modbus RTU / RS485）。
    /// 协议要点（见 Alicat Modbus 通讯协议手册）：
    /// - 采用 PLC 编号约定，手册寄存器号从 1 开始，报文地址需减 1；
    /// - 32 位值大端字序：高字在低地址寄存器（与美仪相反）；
    /// - 设定点必须用 0x10 写多寄存器一次写入两个寄存器；
    /// - 命令通过寄存器 1000（命令 ID）+ 1001（命令参数）下发，
    ///   写入完成后读回 1001 可获取执行状态（0x8001~0x8006 为错误）。
    /// </summary>
    public class AlicatMassFlowController : IMassFlowController
    {
        #region 寄存器定义（手册第 1 节，PLC 编号，使用时 -1）

        /// <summary>命令 ID 寄存器（1000）</summary>
        private const ushort RegCommandId = 1000 - 1;
        /// <summary>命令参数 / 命令状态寄存器（1001）</summary>
        private const ushort RegCommandArg = 1001 - 1;
        /// <summary>设定点寄存器（1010-1011，float）</summary>
        private const ushort RegSetpoint = 1010 - 1;
        /// <summary>设备状态寄存器（1201-1202，32 位）</summary>
        private const ushort RegDeviceStatus = 1201 - 1;
        /// <summary>统计数据区起始：压力（1203-04）</summary>
        private const ushort RegStatsBase = 1203 - 1;

        /// <summary>命令 ID（手册 1.1 节）</summary>
        private const ushort CmdChangeGas = 1;
        private const ushort CmdTare = 4;
        private const ushort CmdResetTotalizer = 5;
        private const ushort CmdValve = 6;

        #endregion

        private readonly ModbusRtuClient _client;
        private readonly bool _ownsClient;
        private readonly byte _slaveId;
        private readonly ILogService _log;

        /// <summary>   
        /// 独占串口方式创建（Alicat 出厂默认 19200bps、无校验）
        /// </summary>
        public AlicatMassFlowController(
            string portName,
            byte slaveId = 1,
            int baudRate = 19200,
            Parity parity = Parity.None,
            ILogService? log = null)
            : this(new ModbusRtuClient(portName, baudRate, parity, StopBits.One, log: log), slaveId, log)
        {
            _ownsClient = true;
        }

        /// <summary>
        /// 共享总线方式创建（多设备挂同一条 485 总线时传入同一个 client）
        /// </summary>
        public AlicatMassFlowController(ModbusRtuClient client, byte slaveId = 1, ILogService? log = null)
        {
            _client = client;
            _slaveId = slaveId;
            _log = log ?? new LogService(nameof(AlicatMassFlowController));
        }

        public bool IsOpen => _client.IsOpen;

        public Task OpenAsync(CancellationToken ct = default) => _client.OpenAsync(ct);

        public Task CloseAsync() => _client.CloseAsync();

        /// <summary>
        /// 一次读取质量流量控制器全部统计数据（手册 1.5.1 节）：
        /// 1203-04 压力 / 1205-06 温度 / 1207-08 体积流量 / 1209-10 质量流量 / 1211-12 设定点
        /// </summary>
        public async Task<AlicatReading> ReadAllAsync(CancellationToken ct = default)
        {
            var regs = await _client.ReadHoldingRegistersAsync(_slaveId, RegStatsBase, 10, ct).ConfigureAwait(false);
            var reading = new AlicatReading
            {
                Pressure = ModbusRtuClient.ToFloatHighWordFirst(regs[0], regs[1]),
                Temperature = ModbusRtuClient.ToFloatHighWordFirst(regs[2], regs[3]),
                VolumetricFlow = ModbusRtuClient.ToFloatHighWordFirst(regs[4], regs[5]),
                MassFlow = ModbusRtuClient.ToFloatHighWordFirst(regs[6], regs[7]),
                MassFlowSetpoint = ModbusRtuClient.ToFloatHighWordFirst(regs[8], regs[9])
            };
            _log.Debug($"Alicat[{_slaveId}] 读数：P={reading.Pressure:0.###} T={reading.Temperature:0.##} " +
                       $"Qv={reading.VolumetricFlow:0.###} Qm={reading.MassFlow:0.###} SP={reading.MassFlowSetpoint:0.###}");
            return reading;
        }

        public async Task<float> ReadMassFlowAsync(CancellationToken ct = default)
        {
            // 质量流量：寄存器 1209-10
            var regs = await _client.ReadHoldingRegistersAsync(_slaveId, (ushort)(RegStatsBase + 6), 2, ct).ConfigureAwait(false);
            return ModbusRtuClient.ToFloatHighWordFirst(regs[0], regs[1]);
        }

        public async Task<float> ReadPressureAsync(CancellationToken ct = default)
        {
            // 压力：寄存器 1203-04
            var regs = await _client.ReadHoldingRegistersAsync(_slaveId, RegStatsBase, 2, ct).ConfigureAwait(false);
            return ModbusRtuClient.ToFloatHighWordFirst(regs[0], regs[1]);
        }

        public async Task<uint> ReadDeviceStatusAsync(CancellationToken ct = default)
        {
            var regs = await _client.ReadHoldingRegistersAsync(_slaveId, RegDeviceStatus, 2, ct).ConfigureAwait(false);
            var status = ((uint)regs[0] << 16) | regs[1];
            if (status != 0)
            {
                _log.Warn($"Alicat[{_slaveId}] 设备状态异常位：0x{status:X8}");
            }
            return status;
        }

        /// <summary>写入流量设定点（float 必须一次写入两个寄存器，手册 1.2 节）</summary>
        public async Task WriteSetpointAsync(float setpoint, CancellationToken ct = default)
        {
            var regs = ModbusRtuClient.FromFloatHighWordFirst(setpoint);
            await _client.WriteMultipleRegistersAsync(_slaveId, RegSetpoint, regs, ct).ConfigureAwait(false);
            _log.Info($"Alicat[{_slaveId}] 设定点写入：{setpoint:0.###}");
        }

        public Task ChangeGasAsync(ushort gasNumber, CancellationToken ct = default)
            => SendCommandAsync(CmdChangeGas, gasNumber, $"切换气体 -> {gasNumber}", ct);

        public Task TareAsync(ushort tareType, CancellationToken ct = default)
            => SendCommandAsync(CmdTare, tareType, $"清零（类型 {tareType}）", ct);

        public Task ResetTotalizerAsync(CancellationToken ct = default)
            => SendCommandAsync(CmdResetTotalizer, 0, "累计值清零", ct);

        public Task SetValveAsync(ushort valveMode, CancellationToken ct = default)
            => SendCommandAsync(CmdValve, valveMode, $"阀门控制（模式 {valveMode}）", ct);

        /// <summary>
        /// 下发设备命令：0x10 同时写入命令 ID（1000）与参数（1001），
        /// 完成后读回 1001 校验执行状态（手册 1.1 节）。
        /// </summary>
        private async Task SendCommandAsync(ushort commandId, ushort argument, string what, CancellationToken ct)
        {
            await _client.WriteMultipleRegistersAsync(
                _slaveId, RegCommandId, new[] { commandId, argument }, ct).ConfigureAwait(false);

            // 读回命令状态：0 成功；0x8001~0x8006 为错误码
            var status = (await _client.ReadHoldingRegistersAsync(_slaveId, RegCommandArg, 1, ct).ConfigureAwait(false))[0];
            if (status >= 0x8001 && status <= 0x8006)
            {
                var message = $"Alicat[{_slaveId}] 命令失败：{what}，状态码 0x{status:X4}（{DescribeStatus(status)}）";
                _log.Error(message);
                throw new IOException(message);
            }

            _log.Info($"Alicat[{_slaveId}] 命令执行成功：{what}");
        }

        private static string DescribeStatus(ushort status) => status switch
        {
            0x8001 => "无效命令 ID",
            0x8002 => "无效设置",
            0x8003 => "请求特性不受支持",
            0x8004 => "无效混合气体编号",
            0x8005 => "无效混合气体成分",
            0x8006 => "无效混合气体百分比",
            _ => "未知状态"
        };

        public void Dispose()
        {
            if (_ownsClient)
            {
                _client.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}
