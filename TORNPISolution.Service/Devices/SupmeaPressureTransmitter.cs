using System.IO.Ports;
using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 美仪（Supmea）单晶硅压力/差压变送器控制类。适用型号：P3000-D / 2051-D / 2051LT-D / 2051DP-D。
    /// 通讯：RS485，ModBus RTU，从站地址 1~247，波特率 1200~57600，校验 无/奇/偶。
    /// 浮点数据为两寄存器、低字在前（手册示例：0000 4080 = 4.0）。
    /// </summary>
    public class SupmeaPressureTransmitter : IPressureTransmitter
    {
        #region 寄存器定义

        /// <summary>当前故障号（short，0 = 无故障）</summary>
        private const ushort RegFaultCode = 0x0007;
        /// <summary>显示压力（float，显示单位）</summary>
        private const ushort RegDisplayPressure = 0x0008;
        /// <summary>输出电流（float，mA）</summary>
        private const ushort RegOutputCurrent = 0x000A;
        /// <summary>输出百分比（float，%）</summary>
        private const ushort RegOutputPercent = 0x000C;
        /// <summary>传感器温度（float，℃）</summary>
        private const ushort RegTemperature = 0x0010;
        /// <summary>系统压力（float，kPa）</summary>
        private const ushort RegSystemPressure = 0x0012;

        /// <summary>系统命令区起始地址：0x1000 主命令字 / 0x1001 从命令字 / 0x1002~ 参数</summary>
        private const ushort RegCommand = 0x1000;

        /// <summary>主命令字（附录 A.4 命令列表）</summary>
        private const ushort MainCommand = 0x0055;
        /// <summary>从命令字：固定电流输出</summary>
        private const ushort SubCmdFixedCurrent = 0x0001;
        /// <summary>从命令字：有源迁移（4mA/20mA 标定）</summary>
        private const ushort SubCmdCurrentTrim = 0x0002;
        /// <summary>从命令字：压力微调（n=5 为零点微调）</summary>
        private const ushort SubCmdPressureTrim = 0x0003;
        /// <summary>从命令字：复位表头（参数固定 0x5AA5）</summary>
        private const ushort SubCmdReset = 0x0005;

        #endregion

        private readonly ModbusRtuClient _client;
        private readonly bool _ownsClient;
        private readonly byte _slaveId;
        private readonly ILogService _log;

        /// <summary>独占串口方式创建；说明书默认参数 9600bps、无校验，停止位发 2 检 1，主站按 1 位发送即可</summary>
        public SupmeaPressureTransmitter(
            string portName,
            byte slaveId = 1,
            int baudRate = 9600,
            Parity parity = Parity.None,
            ILogService? log = null)
            : this(new ModbusRtuClient(portName, baudRate, parity, StopBits.One, log: log), slaveId, log)
        {
            _ownsClient = true;
        }

        /// <summary>共享总线方式创建（多设备挂同一条 485 总线时传入同一个 client）</summary>
        public SupmeaPressureTransmitter(ModbusRtuClient client, byte slaveId = 1, ILogService? log = null)
        {
            _client = client;
            _slaveId = slaveId;
            _log = log ?? new LogService(nameof(SupmeaPressureTransmitter));
        }

        public bool IsOpen => _client.IsOpen;

        public Task OpenAsync(CancellationToken ct = default) => _client.OpenAsync(ct);

        public Task CloseAsync() => _client.CloseAsync();

        public Task<float> ReadPressureAsync(CancellationToken ct = default)
            => ReadFloatAsync(RegDisplayPressure, "显示压力", ct);

        public Task<float> ReadOutputCurrentAsync(CancellationToken ct = default)
            => ReadFloatAsync(RegOutputCurrent, "输出电流", ct);

        public Task<float> ReadOutputPercentAsync(CancellationToken ct = default)
            => ReadFloatAsync(RegOutputPercent, "输出百分比", ct);

        public Task<float> ReadTemperatureAsync(CancellationToken ct = default)
            => ReadFloatAsync(RegTemperature, "传感器温度", ct);

        public Task<float> ReadSystemPressureAsync(CancellationToken ct = default)
            => ReadFloatAsync(RegSystemPressure, "系统压力", ct);

        public async Task<short> ReadFaultCodeAsync(CancellationToken ct = default)
        {
            var regs = await _client.ReadHoldingRegistersAsync(_slaveId, RegFaultCode, 1, ct).ConfigureAwait(false);
            var code = (short)regs[0];
            if (code != 0)
            {
                _log.Warn($"变送器[{_slaveId}] 故障号：{code}");
            }
            return code;
        }

        /// <summary>零点微调（压力微调 n=5），需在无测量压力/通大气状态下执行</summary>
        public Task ZeroCalibrateAsync(CancellationToken ct = default)
            => SendCommandAsync(SubCmdPressureTrim, ModbusRtuClient.FromInt32LowWordFirst(5), "零点微调", ct);

        /// <summary>4mA / 20mA 有源迁移标定（n=1: 4mA，n=2: 20mA）</summary>
        public Task TrimCurrentAsync(bool is20mA, CancellationToken ct = default)
            => SendCommandAsync(SubCmdCurrentTrim,
                ModbusRtuClient.FromInt32LowWordFirst(is20mA ? 2 : 1),
                is20mA ? "20mA 迁移标定" : "4mA 迁移标定", ct);

        /// <summary>固定电流输出（回路检查用）</summary>
        public Task SetFixedCurrentAsync(float milliAmp, CancellationToken ct = default)
            => SendCommandAsync(SubCmdFixedCurrent,
                ModbusRtuClient.FromFloatLowWordFirst(milliAmp),
                $"固定电流输出 {milliAmp:0.###}mA", ct);

        /// <summary>复位表头（参数固定 0x5AA5）</summary>
        public Task ResetDeviceAsync(CancellationToken ct = default)
            => SendCommandAsync(SubCmdReset, ModbusRtuClient.FromInt32LowWordFirst(0x5AA5), "复位表头", ct);

        private async Task<float> ReadFloatAsync(ushort register, string what, CancellationToken ct)
        {
            var regs = await _client.ReadHoldingRegistersAsync(_slaveId, register, 2, ct).ConfigureAwait(false);
            var value = ModbusRtuClient.ToFloatLowWordFirst(regs[0], regs[1]);
            _log.Debug($"变送器[{_slaveId}] 读{what}：{value:0.####}");
            return value;
        }

        /// <summary>下发系统命令：0x10 一次写入 主命令字(0x1000) + 从命令字(0x1001) + 参数1(0x1002~0x1003)</summary>
        private async Task SendCommandAsync(ushort subCmd, ushort[] param1, string what, CancellationToken ct)
        {
            var values = new ushort[] { MainCommand, subCmd, param1[0], param1[1] };
            await _client.WriteMultipleRegistersAsync(_slaveId, RegCommand, values, ct).ConfigureAwait(false);
            _log.Info($"变送器[{_slaveId}] 命令下发成功：{what}");
        }

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
