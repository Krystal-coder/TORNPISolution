namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 产品电池信息，由产品串口协议 0x6C（Func=0x07）解析得到。
    /// 响应 payload 共 8 字节（协议 fac_bat_req）：
    ///   [0..3] 电压 uint32 LE（mV）
    ///   [4]    百分比（0-100%）
    ///   [5..7] 保留字节 rese[3]（协议保留，不解析）
    /// </summary>
    /// <param name="VoltageMv">电池电压（mV）</param>
    /// <param name="Percentage">电量百分比（%）</param>
    /// <param name="Raw">原始 payload 字节（便于实机联调核对未解析字段）</param>
    public sealed record BatteryReading(
        uint VoltageMv,
        byte Percentage,
        byte[]? Raw = null);
}
