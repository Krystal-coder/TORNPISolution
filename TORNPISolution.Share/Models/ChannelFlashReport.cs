namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 一路烧录的状态与结果快照：汇总页卡片与详情页都直接消费它。
    /// 八条通道在各自线程并发更新，每次状态变化发一份新的不可变记录，避免 UI 读到进度与结论不一致的中间态。
    /// </summary>
    public sealed record ChannelFlashReport(
        int Channel,
        string Name,
        string Port,
        FlashAction Action,
        FlashStage Stage,
        int Percent,
        string Detail,
        FlashResult? Result,
        IReadOnlyList<string> Trace,
        TimeSpan Elapsed)
    {
        /// <summary>这一路本轮是否已经落定（不再变化）</summary>
        public bool IsSettled => Stage is FlashStage.Succeeded or FlashStage.Failed or FlashStage.Skipped or FlashStage.Canceled;

        /// <summary>本轮结果：没落定时为 null</summary>
        public bool? Success => Result?.Success;

        /// <summary>界面用的百分比文本：设备没给可算比例时留空，不显示一个假的 0%</summary>
        public string PercentText => Percent >= 0 ? $"{Percent}%" : string.Empty;

        /// <summary>动作的中文叫法（记录与界面共用一份口径）</summary>
        public string ActionText => Action.ToDisplayText();
    }

    /// <summary>烧录动作的显示文案（界面上的三档选择见 <see cref="FlashJobExtensions"/>）</summary>
    public static class FlashActionExtensions
    {
        public static string ToDisplayText(this FlashAction action) => action switch
        {
            FlashAction.WizProFlash => "烧 Flash",
            FlashAction.FreqchipMcu => "烧富芮坤",
            _ => action.ToString(),
        };
    }

    /// <summary>阶段的中文说法：界面卡片、明细时间线、记录文件共用一份口径，不要各写一套</summary>
    public static class FlashStageExtensions
    {
        public static string ToDisplayText(this FlashStage stage) => stage switch
        {
            FlashStage.Idle => "等待开始",
            FlashStage.Downloading => "下载工程数据",
            FlashStage.SelfChecking => "烧录器自检",
            FlashStage.WaitingReset => "等芯片复位",
            FlashStage.Erasing => "擦除",
            FlashStage.Programming => "写入",
            FlashStage.Verifying => "校验",
            FlashStage.Succeeded => "成功",
            FlashStage.Failed => "未通过",
            FlashStage.Skipped => "跳过",
            FlashStage.Canceled => "已取消",
            _ => stage.ToString(),
        };
    }
}
