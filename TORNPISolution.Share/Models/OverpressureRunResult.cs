namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 仪器过压/缓降测试一轮的结果：峰值压力与充气、泄气两段时长。
    /// 时长由仪器自己上报（BPA700 的过压测试回调直接回传），实现拿不到该值时留 NaN，由调用方按缺值处理。
    /// </summary>
    public class OverpressureRunResult
    {
        /// <summary>本轮峰值压力（mmHg）</summary>
        public double PeakPressure { get; init; }

        /// <summary>充气段时长（s，起压至峰值）</summary>
        public double InflateSeconds { get; init; } = double.NaN;

        /// <summary>泄气段时长（s，峰值降至泄压终点阈值）</summary>
        public double DeflateSeconds { get; init; } = double.NaN;
    }
}
