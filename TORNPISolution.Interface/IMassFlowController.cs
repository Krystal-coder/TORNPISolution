namespace TORNPISolution.Interface
{
    /// <summary>
    /// Alicat 质量流量控制器实时数据快照（对应统计寄存器 1203-1214）
    /// </summary>
    public class AlicatReading
    {
        /// <summary>压力（绝压/表压/差压，取决于设备配置）</summary>
        public float Pressure { get; set; }

        /// <summary>介质温度</summary>
        public float Temperature { get; set; }

        /// <summary>体积流量</summary>
        public float VolumetricFlow { get; set; }

        /// <summary>质量流量</summary>
        public float MassFlow { get; set; }

        /// <summary>质量流量设定点</summary>
        public float MassFlowSetpoint { get; set; }
    }

    /// <summary>
    /// 质量流量控制器设备接口（Alicat，Modbus RTU / RS485）。
    /// 485 通讯为慢速 IO
    /// </summary>
    public interface IMassFlowController : IDisposable
    {
        /// <summary>串口是否已打开</summary>
        bool IsOpen { get; }

        /// <summary>打开串口连接</summary>
        Task OpenAsync(CancellationToken ct = default);

        /// <summary>关闭串口连接</summary>
        Task CloseAsync();

        /// <summary>一次读取全部实时统计数据（压力/温度/体积流量/质量流量/设定点）</summary>
        Task<AlicatReading> ReadAllAsync(CancellationToken ct = default);

        /// <summary>读取质量流量</summary>
        Task<float> ReadMassFlowAsync(CancellationToken ct = default);

        /// <summary>读取压力</summary>
        Task<float> ReadPressureAsync(CancellationToken ct = default);

        /// <summary>读取设备状态位（bit 定义见 Alicat 手册 1.4 节）</summary>
        Task<uint> ReadDeviceStatusAsync(CancellationToken ct = default);

        /// <summary>写入流量设定点（工程单位，32 位浮点）</summary>
        Task WriteSetpointAsync(float setpoint, CancellationToken ct = default);

        /// <summary>切换气体（气体编号见 Alicat 手册气体表）</summary>
        Task ChangeGasAsync(ushort gasNumber, CancellationToken ct = default);

        /// <summary>清零（0=压力 1=绝压 2=体积流量）</summary>
        Task TareAsync(ushort tareType, CancellationToken ct = default);

        /// <summary>累计值清零</summary>
        Task ResetTotalizerAsync(CancellationToken ct = default);

        /// <summary>阀门控制（0=取消 1=保持关闭 2=保持当前 3=排气）</summary>
        Task SetValveAsync(ushort valveMode, CancellationToken ct = default);
    }
}
