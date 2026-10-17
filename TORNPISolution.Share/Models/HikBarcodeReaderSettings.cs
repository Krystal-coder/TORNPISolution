namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 海康 MV-ID 系列读码器（智能读码相机）的链接参数。
    /// 相机侧开「TCP服务器端协议」当服务端，PC 作客户端连过去，两个端口各一条连接：
    /// 触发端口收软触发文本（默认 2001，文本默认 start），数据端口回传读码结果（默认 2002）。
    /// 结果按 &lt;回车&gt;&lt;换行&gt;&lt;条码内容&gt;; 推送，所以读一帧的边界是分号而不是换行。
    /// 触发口上可能还有一轮交互请求/回复（默认 hello / world），握手完相机才抓拍。
    /// </summary>
    public class HikBarcodeReaderSettings : LanLinkSettings
    {
        /// <summary>是否启用这台读码器；未接入的关掉，建链与触发都会跳过</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>位号，日志与界面用来区分多台（如「读码器1」）</summary>
        public string Name { get; set; } = "读码器";

        /// <summary>结果回传端口（继承自 Port），即相机「TCP服务器端口」，默认 2002</summary>
        // 复用 LanLinkSettings.Port

        /// <summary>软触发端口，即相机「TCP服务器触发端口」，默认 2001</summary>
        public int TriggerPort { get; set; } = 2001;

        /// <summary>触发文本，须与相机「TCP服务器触发文本」一致（默认 start）</summary>
        public string TriggerText { get; set; } = "start";

        /// <summary>
        /// 下发触发文本时要不要补 \r\n。相机拿整串与「触发文本」比，默认不补；
        /// 现场若确认相机把回车换行算进了比较，再勾上。
        /// </summary>
        public bool TriggerAppendNewLine { get; set; }

        /// <summary>
        /// 相机「TCP服务器交互请求文本」：收到这个就得回「交互回复文本」，相机才开始抓拍。
        /// 留空表示相机没启用交互，连上后不做握手。
        /// </summary>
        public string InteractionRequestText { get; set; } = "hello";

        /// <summary>相机「TCP服务器交互回复文本」，须与相机侧填的一致（默认 world）</summary>
        public string InteractionReplyText { get; set; } = "world";

        /// <summary>TCP 建链超时（毫秒）</summary>
        public int ConnectTimeoutMs { get; set; } = 5000;

        /// <summary>等待一条读码结果的超时（毫秒）；码在视野外或没对准就是等满</summary>
        public int ReadTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// 这台相机一次触发要收几条条码（视野里摆几个码）：收够就不再触发，收不够则重试几轮。
        /// 相机只能解一个码时填 1；视野里不止一个码就按实际个数填。
        /// </summary>
        public int SlotsPerReader { get; set; } = 2;
    }
}
