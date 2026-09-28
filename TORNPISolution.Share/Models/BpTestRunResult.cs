namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 一轮血压测量执行结果：状态序列（0=Idle 1=Wait 2=Busy 3=Stable 4=Finish）与时间参数，
    /// 供 BP-001/002 验证（状态依次经历 Busy→Stable→Finish、快速放气时间）。
    /// BPA700 由 SDK 回调状态机产生；康伟达由 AA 压力曲线判定等价映射
    /// （充气上升=Busy、脉搏波平台=Stable、泄压归零=Finish、充气中骤降=Idle）。
    /// </summary>
    public sealed record BpTestRunResult(
        int FinalStatus,
        IReadOnlyList<int> StatusSequence,
        double PeakPressure,
        double InflatableTime,
        double DeflationTime,
        double TotalTime)
    {
        /// <summary>是否依次经历 Busy(2)→Stable(3)→Finish(4)（产品确认启动并完成测量，BP-001 通过条件）</summary>
        public bool CompletedFullSequence
        {
            get
            {
                var need = new[] { 2, 3, 4 };
                var idx = 0;
                foreach (var s in StatusSequence)
                {
                    if (s == need[idx] && ++idx == need.Length)
                        return true;
                }
                return false;
            }
        }
    }
}
