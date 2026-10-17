namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 烧录工位配置：独立于测试工位的 InstrumentConnectionConfig 另存一份文件。
    /// 两边不共用是有原因的：烧录是另一套治具、另一个页面，而且这里的"通道"是一路一台烧录器、
    /// 一路一个 COM（各通道自己收发指令与进度），跟测试工位那种"多台仪器共享槽位"的口径不是一回事。
    /// 清单里有几行就是几路：换成一拖四的设备就把清单删到 4 行，界面按清单行数排。
    /// </summary>
    public class FlashingConfig
    {
        /// <summary>烧录通道清单，顺序就是通道号（第一行是通道 1）</summary>
        public List<FlashChannelSettings> Channels { get; set; } =
            new(Enumerable.Range(1, 8).Select(i => new FlashChannelSettings { Name = $"通道{i}" }));

        /// <summary>SPI Flash 烧录（迈斯威志 WizPro 系列，USB 转 UART 的串口协议）</summary>
        public WizProFlashSettings WizPro { get; set; } = new();

        /// <summary>主控 MCU 烧录（富芮坤命令行工具，走以太网转 TTL 虚拟出的串口）</summary>
        public FreqchipFlashSettings Freqchip { get; set; } = new();
    }

    /// <summary>
    /// 一个烧录通道的参数。两路串口分开填：同一块板上的两颗芯片由两套设备烧，
    /// 各自的 COM 号互不相干，不要指望它们编号相邻。
    /// </summary>
    public class FlashChannelSettings
    {
        /// <summary>本通道是否参与批量烧录：没接好或暂时不用的路关掉，批量时整路跳过</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>位号（界面与日志显示）</summary>
        public string Name { get; set; } = "通道";

        /// <summary>
        /// 本通道 WizPro 烧录器的串口号。一路一台、各占一个 COM，
        /// 可用「自动识别」逐个口发 RDMC 读回设备自报的通道号来建表（USB 枚举顺序不保证与通道一致）。
        /// </summary>
        public string WizProPortName { get; set; } = string.Empty;

        /// <summary>
        /// 本通道主控 MCU 烧录用的串口号（以太网转 TTL 虚拟出来的 COM，工具命令行的 -p 参数就带它）。
        /// 这类虚拟口没有自证手段，只能人工配准；启动时会校验有没有重复或已经消失的口。
        /// </summary>
        public string FreqchipPortName { get; set; } = string.Empty;
    }

    /// <summary>
    /// WizPro 烧录器参数。
    /// 烧录本身走串口指令（复位自检 → 启动烧写 → 收过程状态），但"把工程数据下载进烧录器内部 Flash"
    /// 这一步不是串口指令，而是调用厂商上位机命令行、再靠它生成的 ResDW.Txt 判成败。
    /// </summary>
    public class WizProFlashSettings
    {
        /// <summary>厂商上位机 APP 完整路径（下载工程数据用；调用形式：APP路径 工程文件路径 OFF）</summary>
        public string AppPath { get; set; } = string.Empty;

        /// <summary>工程文件完整路径（由厂商上位机制作，扩展名随 APP 系列不同）</summary>
        public string ProjectPath { get; set; } = string.Empty;

        /// <summary>
        /// 下载完让 APP 自己退出（命令行末尾带 OFF）。
        /// 不带就要人工关窗口，而 APP 不退出就不会生成 ResDW.Txt，下载结果永远等不到。
        /// </summary>
        public bool CloseAppAfterDownload { get; set; } = true;

        /// <summary>
        /// 等 APP 生成 ResDW.Txt 的超时（毫秒）。文件落在 APP 所在目录，
        /// 出现后还要读它的内容判成败——失败时文件照样会出现，只看存在会把失败读成成功。
        /// </summary>
        public int DownloadTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// 烧录前先发 REST 让烧录器复位自检：回 WPKO 才继续，回 WPGN 说明它内部的数据有问题、
        /// 要重新下载工程（这时候直接开烧只会整批失败，而且报错看不出真因）。
        /// </summary>
        public bool SelfCheckBeforeProgram { get; set; } = true;

        /// <summary>波特率：厂商规定 1M。注意 USB 转串口芯片要支持这个非标速率，不支持会一直收不到帧</summary>
        public int BaudRate { get; set; } = 1_000_000;

        /// <summary>数据位：厂商规定 8</summary>
        public int DataBits { get; set; } = 8;

        /// <summary>停止位：厂商规定 2</summary>
        public int StopBits { get; set; } = 2;

        /// <summary>单条指令等应答的超时（毫秒）</summary>
        public int ResponseTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 一轮烧录的总超时（毫秒）：从发出启动烧写指令到收到结束帧。
        /// 烧写时长跟芯片容量和是否空片有关（厂商给的参考是 128K 脱机烧约 10s），留足余量。
        /// </summary>
        public int ProgramTimeoutMs { get; set; } = 120_000;
    }

    /// <summary>
    /// 富芮坤命令行烧录参数。
    /// 工具的握手原理决定了流程顺序：芯片只在"一上电那一刻"用 boot 程序找工具，
    /// 所以必须先把进程起起来（输出 Ready = 串口已开、等芯片复位），再让芯片复位，
    /// 握手超时就是这个等待窗口，要留给操作员按复位键的时间。
    /// </summary>
    public class FreqchipFlashSettings
    {
        /// <summary>烧录工具完整路径（-download 那个 exe）</summary>
        public string ToolPath { get; set; } = string.Empty;

        /// <summary>待烧固件完整路径（-file）</summary>
        public string FirmwarePath { get; set; } = string.Empty;

        /// <summary>芯片系列（-chip）：这个参数必须给，不带时工具会按 801xH 处理</summary>
        public FreqchipChip Chip { get; set; } = FreqchipChip.Series802x;

        /// <summary>
        /// 握手超时（-t，工具允许 1~30 秒）：从进程进入 Ready 到芯片复位握手成功的等待窗口，
        /// 也就是留给"按下这一路复位键"的时长。逐路手按时别设太小，否则表现为随机握手超时。
        /// </summary>
        public int HandshakeTimeoutSeconds { get; set; } = 20;

        /// <summary>波特率（-b）：30xx 系列最高只能 460800，再高会握手不上</summary>
        public int BaudRate { get; set; } = 460800;

        /// <summary>擦除范围（-size，十六进制页数）：可选 200000=2M、100000=1M、80000=512K、40000=256K</summary>
        public string EraseSize { get; set; } = "200000";

        /// <summary>单路整轮超时（毫秒）：工具卡住不退出时的兜底，到点杀进程并按失败记</summary>
        public int ProcessTimeoutMs { get; set; } = 180_000;
    }

    /// <summary>富芮坤工具 -chip 参数的取值（对应工具支持的芯片系列）</summary>
    public enum FreqchipChip
    {
        /// <summary>800x 系列</summary>
        Series800x = 2,

        /// <summary>303x 系列</summary>
        Series303x = 3,

        /// <summary>802x 系列</summary>
        Series802x = 4,

        /// <summary>30xx 系列（波特率上限 460800）</summary>
        Series30xx = 5,

        /// <summary>508x 系列</summary>
        Series508x = 6,
    }
}
