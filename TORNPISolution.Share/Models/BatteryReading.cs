namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 产品电池信息（电压 mV + 百分比 %），由产品串口协议 0x6C 解析得到。
    /// </summary>
    public sealed record BatteryReading(uint VoltageMv, byte Percentage);
}
