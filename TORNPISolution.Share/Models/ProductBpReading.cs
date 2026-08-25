namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 产品一次血压测量上报结果（收缩压/舒张压 mmHg + 心率 BPM）。
    /// BP-004/BP-005 动态压力精度测试用，由产品串口协议 0x7B 解析得到。
    /// </summary>
    public sealed record ProductBpReading(double Systolic, double Diastolic, double HeartRate);
}
