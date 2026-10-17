namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 烧录动作（一颗芯片）。一块板上两颗芯片，顺序固定：先 SPI Flash 再富芮坤主控
    /// （主控的 boot 程序要从 Flash 里加载，Flash 没烧好主控起不来）。
    /// 这一层只说“烧哪一颗”，界面上“本轮做什么”的三档选择见 <see cref="FlashJob"/>。
    /// </summary>
    public enum FlashAction
    {
        /// <summary>烧 SPI Flash（迈斯威志 WizPro 烧录器）</summary>
        WizProFlash,

        /// <summary>烧富芮坤主控 MCU（命令行工具）</summary>
        FreqchipMcu,
    }

    /// <summary>
    /// 界面上“本轮做什么”的三档：单独烧 Flash、单独烧富芮坤、或 All 两步连着走。
    /// 与 <see cref="FlashAction"/> 分开放是两个原因：
    ///   编排服务一轮只做一颗芯片（工程数据下载、预检、串口都按芯片分支），All 是它上面的一层调度，不该渗到驱动口径里去；
    ///   记录文件里每行要写清这一路当时烧的是哪一颗，拿 All 进去就没法对了。
    /// </summary>
    public enum FlashJob
    {
        /// <summary>只烧 SPI Flash</summary>
        WizProFlash,

        /// <summary>只烧富芮坤主控</summary>
        FreqchipMcu,

        /// <summary>先整批烧 Flash，整批落定后再整批烧富芮坤（Flash 没烧好的那一路不再进第二阶段）</summary>
        All,
    }

    /// <summary>
    /// 一路一个动作的过程阶段。界面进度条文案与颜色都吃它，
    /// 驱动只报到哪一步了，怎么显示由界面决定。
    /// </summary>
    public enum FlashStage
    {
        /// <summary>还没开始</summary>
        Idle,

        /// <summary>把工程数据下载进烧录器内部 Flash（调厂商上位机命令行，等它生成结果文件）</summary>
        Downloading,

        /// <summary>让烧录器复位自检，等它回数据正常可烧</summary>
        SelfChecking,

        /// <summary>已启动烧写，等芯片进入 boot 握手（主控那条路要人工按复位键，倒计时就挂在这一步）</summary>
        WaitingReset,

        /// <summary>擦除</summary>
        Erasing,

        /// <summary>写入</summary>
        Programming,

        /// <summary>校验</summary>
        Verifying,

        /// <summary>本动作成功</summary>
        Succeeded,

        /// <summary>本动作失败</summary>
        Failed,

        /// <summary>这一路本轮不烧（通道未启用或没被选中）</summary>
        Skipped,

        /// <summary>被取消</summary>
        Canceled,
    }

    /// <summary>
    /// 一次进度上报。Percent 为 -1 表示这一步设备没给可算的比例
    /// （厂商的状态帧里擦除/写入/校验带的是当前页加总页数，能算出来才算）。
    /// </summary>
    public readonly record struct FlashProgress(FlashStage Stage, int Percent, string Detail)
    {
        public override string ToString() => Percent >= 0 ? $"{Stage} {Percent}%" : $"{Stage}";
    }

    /// <summary>
    /// 一路一个动作的最终结果。
    /// Trace 留着与设备的原始往来（烧录器是帧的十六进制、命令行工具是 stdout 原文），
    /// 现场排查全靠它：错误码表厂商没给全，光看结论查不出真因。
    /// </summary>
    public sealed record FlashResult(
        bool Success,
        FlashStage Stage,
        string Message,
        int ErrorCode = 0,
        IReadOnlyList<string>? Trace = null)
    {
        public static FlashResult Ok(string message, IReadOnlyList<string>? trace = null)
            => new(true, FlashStage.Succeeded, message, 0, trace);

        public static FlashResult Fail(string message, int errorCode = 0,
            FlashStage stage = FlashStage.Failed, IReadOnlyList<string>? trace = null)
            => new(false, stage, message, errorCode, trace);

        public static FlashResult Skipped(string message) => new(false, FlashStage.Skipped, message);

        public static FlashResult Canceled(IReadOnlyList<string>? trace = null)
            => new(false, FlashStage.Canceled, "已取消", 0, trace);
    }

    /// <summary>
    /// 烧录器自报的身份（RDMC 指令回读）。
    /// 用途是把设备管理器里的 COM 号和第几通道对上：USB 枚举顺序不保证与接线一致，
    /// 靠这个自报值建表比人工猜靠谱。
    /// </summary>
    public sealed record WizProIdentity(int ChannelId, string ProductInfo, string RawFrame);

    /// <summary>本轮选择（<see cref="FlashJob"/>）的显示文案与展开出来的动作步骤</summary>
    public static class FlashJobExtensions
    {
        public static string ToDisplayText(this FlashJob job) => job switch
        {
            FlashJob.WizProFlash => "烧 Flash",
            FlashJob.FreqchipMcu => "烧富芮坤",
            FlashJob.All => "All",
            _ => job.ToString(),
        };

        /// <summary>
        /// 这一步跑完该进哪一步：All 就是“先 Flash 后富芮坤”两步，单独动作只一步。
        /// 顺序在这里硬写而不是存配置：主控的 boot 要从 Flash 里加载，这个先后是物理约束，给成开关只会让人配错。
        /// </summary>
        public static FlashAction[] ToPhases(this FlashJob job) => job switch
        {
            FlashJob.WizProFlash => new[] { FlashAction.WizProFlash },
            FlashJob.FreqchipMcu => new[] { FlashAction.FreqchipMcu },
            _ => new[] { FlashAction.WizProFlash, FlashAction.FreqchipMcu },
        };
    }
}
