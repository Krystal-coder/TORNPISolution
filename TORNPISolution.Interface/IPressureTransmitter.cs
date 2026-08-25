namespace TORNPISolution.Interface
{
    /// <summary>
    /// 压力/差压变送器设备接口（美仪 P3000-D / 2051-D / 2051LT-D / 2051DP-D）。
    /// 485 通讯为慢速 IO，所有操作均为异步，避免阻塞 UI 线程。
    /// </summary>
    public interface IPressureTransmitter : IDisposable
    {
        /// <summary>串口是否已打开</summary>
        bool IsOpen { get; }

        /// <summary>打开串口连接</summary>
        Task OpenAsync(CancellationToken ct = default);

        /// <summary>关闭串口连接</summary>
        Task CloseAsync();

        /// <summary>读取显示压力（显示单位的压力值）</summary>
        Task<float> ReadPressureAsync(CancellationToken ct = default);

        /// <summary>读取输出电流（mA）</summary>
        Task<float> ReadOutputCurrentAsync(CancellationToken ct = default);

        /// <summary>读取输出百分比（%）</summary>
        Task<float> ReadOutputPercentAsync(CancellationToken ct = default);

        /// <summary>读取传感器温度（℃）</summary>
        Task<float> ReadTemperatureAsync(CancellationToken ct = default);

        /// <summary>读取系统压力（kPa）</summary>
        Task<float> ReadSystemPressureAsync(CancellationToken ct = default);

        /// <summary>读取当前故障号（0 = 无故障）</summary>
        Task<short> ReadFaultCodeAsync(CancellationToken ct = default);

        /// <summary>零点微调（现场清零，等效面板 Z 键清零）</summary>
        Task ZeroCalibrateAsync(CancellationToken ct = default);

        /// <summary>4mA / 20mA 有源迁移标定</summary>
        Task TrimCurrentAsync(bool is20mA, CancellationToken ct = default);

        /// <summary>固定电流输出（用于回路检查），单位 mA</summary>
        Task SetFixedCurrentAsync(float milliAmp, CancellationToken ct = default);

        /// <summary>复位表头</summary>
        Task ResetDeviceAsync(CancellationToken ct = default);
    }
}
