namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 产品一次血压测量上报结果：收缩压/舒张压/平均压（mmHg，0x7B 三联 uint16 LE）+ 心率（BPM，0x7C 单独读回后填入）。
    /// BP-004/BP-005 动态压力精度测试用。
    /// </summary>
    public sealed record ProductBpReading(double Systolic, double Diastolic, double MeanPressure, double HeartRate = double.NaN);
}
