namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// CRY5242/5243 四通道电流电压综测仪的链接参数：网口 Raw Socket 上的 SCPI 文本指令，一问一答，帧边界 \r\n。
    /// 一台仪器四个通道，通道 A~D 各占一个 TCP 端口（默认 5025~5028 连号），指令里仍带通道号；要同时驱动四个通道就得四个端口各建一条连接。
    /// 仪器 IP 由背板拨码决定：169.254.1.(32 + 左档×16 + 右档×1)，改完必须重启仪器才生效；同时接两台时必须拨成不同值。
    /// </summary>
    public class Cry5242Settings : LanLinkSettings
    {
        /// <summary>通道数（A~D，硬件固定四通道）</summary>
        public const int ChannelCount = 4;

        /// <summary>是否启用这台仪器；未接入的关掉，初始化与测量都会跳过</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>位号，日志与界面用来区分同类多台（如「综测仪1」）</summary>
        public string Name { get; set; } = "综测仪";

        /// <summary>通道 A 的端口（继承自 Port），B/C/D 依次 +1</summary>

        /// <summary>现场接的是四线还是二线（指令里发 4 还是 2）：四线要把 PO+ 与 PO_S+、PO- 与 PO_S- 各自短接，二线时 Sense 两脚可不接；输出指令未专门指定线制时按本值下发</summary>
        public bool FourWire { get; set; } = true;

        /// <summary>本台四个通道统一输出的电压（mV，量程 0~6500）：改完要刷新模块库，上电项的合格区间由它算出；填 0 或负数视为没配好，本台不上电并全部判 FAIL（这一路直接给主板供电）</summary>
        public int OutputMilliVolts { get; set; } = 5000;

        /// <summary>过流保护值（mA，量程 0~2000）：5242 上它是保护阈值而不是恒流点，通道电流超了就红灯、停输出，发 *RST 才恢复（指令表口径，当限流值用是高配 5243 的行为）；按产品工作电流留余量填</summary>
        public int ProtectionMilliAmps { get; set; } = 500;

        /// <summary>缓输出的斜率时间（ms，量程 10~100）：电压用这么久爬到位，避开板子上大电容的上电冲击电流；手册与厂商模板都用 50</summary>
        public int RampMilliSeconds { get; set; } = 50;

        /// <summary>回读电压的合格窗口（±mV）：按设定值上下浮动判定，收得太窄会被仪器自己的分辨率误判</summary>
        public int VoltageToleranceMilliVolts { get; set; } = 50;

        /// <summary>上电前先发一次 *RST：关掉本台所有通道输出，把上一轮残留的输出状态和已动作的过流保护一起清掉（过流保护动作后只有 *RST 能恢复）；代价是本台多等一行回执</summary>
        public bool ResetBeforePowerOn { get; set; } = true;

        /// <summary>TCP 建链超时（毫秒）</summary>
        public int ConnectTimeoutMs { get; set; } = 5000;

        /// <summary>单条指令的响应等待超时（毫秒）；测量类指令现场等一行回读</summary>
        public int ResponseTimeoutMs { get; set; } = 3000;

        /// <summary>取某个通道（1~4）的 TCP 端口：基址端口 + 通道号 - 1</summary>
        public int PortOfChannel(int channel)
            => channel is >= 1 and <= ChannelCount
                ? Port + channel - 1
                : throw new ArgumentOutOfRangeException(nameof(channel),
                    $"{Name} 只有 1~{ChannelCount} 通道，收到：{channel}");
    }

    /// <summary>电流测量档位（对应指令里的 "mA" / "uA" / "nA" / "auto"）</summary>
    public enum Cry5242CurrentRange
    {
        /// <summary>自动量程</summary>
        Auto,
        /// <summary>毫安档</summary>
        Milliamps,
        /// <summary>微安档</summary>
        Microamps,
        /// <summary>纳安档</summary>
        Nanoamps,
    }

    /// <summary>测量通路（FUNCtion:SWITch 的通道名称，先切通路再测量）</summary>
    public enum Cry5242Function
    {
        /// <summary>毫安测量通路（模拟电池模式）</summary>
        Ma,
        /// <summary>微安测量通路</summary>
        Ua,
        /// <summary>纳安测量通路</summary>
        Na,
        /// <summary>外部供电毫安通路</summary>
        MaOut,
        /// <summary>外部供电微安通路</summary>
        UaOut,
        /// <summary>外部供电纳安通路</summary>
        NaOut,
        /// <summary>DVM 电压表通路（0~8V）</summary>
        Dvm,
        /// <summary>通道输出电压通路</summary>
        Po,
    }

    /// <summary>一次测量回读：仪器回执为 CHx: [数值, '单位'] 三段，单位由仪器按档位给（V/mA/uA/nA）；数值原样保留、不做单位换算，换算交给调用方</summary>
    public readonly record struct Cry5242Reading(int Channel, double Value, string Unit)
    {
        public override string ToString() => $"CH{Channel} {Value} {Unit}";
    }
}
