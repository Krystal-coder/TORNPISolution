using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// CRY5242/5243 四通道电流电压综测仪设备接口（网口 SCPI 文本指令）。
    /// 通道号一律 1~4，对应面板 A~D；每台仪器的四个通道各占一条 TCP 连接。
    /// </summary>
    public interface ICry5242Tester : IDisposable
    {
        /// <summary>位号（日志前缀）</summary>
        string Name { get; }

        /// <summary>四个通道的连接是否都已建立</summary>
        bool IsOpen { get; }

        /// <summary>逐通道建立 TCP 连接，并读一次 *IDN? 验证链路</summary>
        Task OpenAsync(CancellationToken ct = default);

        /// <summary>关闭全部通道连接（不断输出状态：要停机请先调 ResetAsync 或把各通道输出关掉）</summary>
        Task CloseAsync();

        /// <summary>读取固件版本（*IDN?，形如 SG2815，CRY5242,"V 0.1.8"）</summary>
        Task<string> GetIdentityAsync(int channel, CancellationToken ct = default);

        /// <summary>整机复位（*RST），关掉所有通道输出；过流保护动作后也只能靠它恢复，手册 4.6 建议连续测试每半小时复位一次</summary>
        Task ResetAsync(CancellationToken ct = default);

        /// <summary>只设输出电压与过流保护值，不打开通路（OUTPUT 不带线制参数）；5242 上第三个参数是过流保护值（超了就红灯、停输出），只有 5243 拿它当限流值</summary>
        /// <param name="voltageMilliVolts">0~6500 mV</param>
        /// <param name="currentMilliAmps">-2000~+2000 mA</param>
        Task ConfigureOutputAsync(int channel, int voltageMilliVolts, int currentMilliAmps, CancellationToken ct = default);

        /// <summary>设输出电压/保护电流并选二线或四线通路（OUTPUT 带线制参数，带线制即打开通路）</summary>
        /// <param name="fourWire">true=四线（发 4），false=二线（发 2）；留空取仪器配置里的 FourWire</param>
        Task SetOutputAsync(int channel, int voltageMilliVolts, int currentMilliAmps, bool? fourWire = null, CancellationToken ct = default);

        /// <summary>带上升时间的缓输出（OUTPUT:RAMP），斜率用于避开上电冲击电流</summary>
        /// <param name="rampMilliSeconds">输出时间 10~100 ms；手册与厂商模板都用 50，实测越设越长越接近设定时长</param>
        /// <param name="fourWire">留空取仪器配置里的 FourWire</param>
        Task SetRampOutputAsync(int channel, int voltageMilliVolts, int currentMilliAmps, int rampMilliSeconds, bool? fourWire = null, CancellationToken ct = default);

        /// <summary>单独开/关通道输出通路（OUTPUT:STA）：没先下发 OUTPUT 时只是空开通路、没有实际输出；OUTPUT / OUTPUT:RAMP 带了线制时通路已打开，不需要再发</summary>
        Task SetOutputEnabledAsync(int channel, bool enabled, CancellationToken ct = default);

        /// <summary>切换该通道的测量通路（FUNCtion:SWITch）；手册里两个电压测量（DVM/CH_PO）都要求先切</summary>
        Task SwitchFunctionAsync(int channel, Cry5242Function function, CancellationToken ct = default);

        /// <summary>测量 DVM 端电压（MEASure:DVM，量程 0~8V，需先切 dvm 通路）</summary>
        Task<Cry5242Reading> MeasureDvmVoltageAsync(int channel, CancellationToken ct = default);

        /// <summary>测量本通道输出电压（MEASure:CH_PO，需先切 po 通路）</summary>
        Task<Cry5242Reading> MeasureOutputVoltageAsync(int channel, CancellationToken ct = default);

        /// <summary>测量该通道电流（MEASure:CURRent，模拟电池模式）：量程直接带在参数里</summary>
        Task<Cry5242Reading> MeasureCurrentAsync(int channel, Cry5242CurrentRange range, CancellationToken ct = default);

        /// <summary>测量外部供电电流（MEASure:CURRent:METER，不输出时读，别超 2A）</summary>
        Task<Cry5242Reading> MeasureExternalCurrentAsync(int channel, Cry5242CurrentRange range, CancellationToken ct = default);

        /// <summary>读取该通道温度（GET_TEMP，℃）；回执格式手册没给</summary>
        Task<double> GetTemperatureAsync(int channel, CancellationToken ct = default);

        /// <summary>读取全部 IO 状态（IO:STA?，原始文本，不带通道参数）；固件 0.0.9 以上才支持</summary>
        Task<string> ReadIoStatusAsync(int channel, CancellationToken ct = default);
    }
}
