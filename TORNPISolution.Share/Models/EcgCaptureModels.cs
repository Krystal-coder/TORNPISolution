namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 产品 0x8D 成对帧式的一个采样点：ADC 为原始码值，Millivolts 为电压码值 /100（1 码值 = 10μV）。
    /// </summary>
    public readonly record struct EcgWavePoint(int Adc, double Millivolts);
}
