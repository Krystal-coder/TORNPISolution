namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 单台仪器的串口链接参数
    /// </summary>
    public class SerialLinkSettings
    {
        /// <summary>串口号，如 COM1</summary>
        public string PortName { get; set; } = "COM1";

        /// <summary>波特率</summary>
        public int BaudRate { get; set; } = 9600;

        /// <summary>校验位：None / Odd / Even</summary>
        public string Parity { get; set; } = "None";

        /// <summary>数据位</summary>
        public int DataBits { get; set; } = 8;

        /// <summary>停止位</summary>
        public int StopBits { get; set; } = 1;

        /// <summary>Modbus 从站地址</summary>
        public byte SlaveId { get; set; } = 1;
    }

    /// <summary>
    /// 仪器链接配置：汇总所有测试用仪器的串口参数，
    /// 由配置服务负责持久化，界面与设备层共享同一份定义。
    /// </summary>
    public class InstrumentConnectionConfig
    {
        /// <summary>被测产品（DUT，串口 232，默认 921600bps）</summary>
        public SerialLinkSettings Product { get; set; } = new() { BaudRate = 921600 };

        /// <summary>
        /// 无产品演示模式：产品串口未接时，血压相关模块（M04 压力控制 / M06 动态压力精度）
        /// 仅驱动模拟仪完成充放气流程验证仪器侧行为；依赖产品读回的结果项由收尾扫描按读回缺失判 FAIL；
        /// 接上产品后务必取消勾选恢复。
        /// </summary>
        public bool NoProductDemoMode { get; set; }

        /// <summary>
        /// 无仪器演示模式：只读产品串口数据（进入产测模式 → 0x72 → 0x74），跳过全部仪器控制
        /// （模拟仪/AECG/NFC/电源等）；各模块执行完产品通讯即返回，
        /// 其余未报告结果项由收尾扫描按读回缺失判 FAIL；正式测试务必取消勾选恢复。
        /// </summary>
        public bool NoInstrumentDemoMode { get; set; }

        /// <summary>美仪压力/差压变送器（默认 9600bps）</summary>
        public SerialLinkSettings PressureTransmitter { get; set; } = new();

        /// <summary>Alicat 质量流量控制器（默认 19200bps）</summary>
        public SerialLinkSettings MassFlowController { get; set; } = new() { BaudRate = 19200 };

        /// <summary>当前启用的血压模拟仪类型（None / Kangwenda / BPA700）</summary>
        public SimulatorType ActiveSimulator { get; set; } = SimulatorType.None;

        /// <summary>康伟达 NIBP 模拟仪串口参数（串口 232，实测 115200bps，含独立测试参数）</summary>
        public KangwendaSettings KangwendaSimulator { get; set; } = new() { BaudRate = 115200 };

        /// <summary>鲸扬 BPA700 模拟仪串口参数（USB VCP，默认 115200bps，含 Category/单位/袖带容量初始化设置）</summary>
        public Bpa700Settings BPA700Simulator { get; set; } = new() { BaudRate = 115200 };

        /// <summary>鲸扬 AECG100 多功能综合性能测试仪（USB VCP + 原生 SDK，COM 口留空为自动选择）</summary>
        public Aecg100Settings Aecg100Tester { get; set; } = new();

        /// <summary>THS_HF NFC 读卡器（岩柱 YZ806/YZ906，串口连接，默认 115200bps）</summary>
        public SerialLinkSettings NfcReader { get; set; } = new() { BaudRate = 115200 };

        /// <summary>艾德克斯 IT6411 双通道直流电源（网口 SCPI Raw Socket，默认端口 5025）</summary>
        public LanLinkSettings It6411PowerSupply { get; set; } = new() { IpAddress = "192.168.1.1", Port = 5025 };

        /// <summary>CRY6151B 电声测试仪（预留，默认禁用）</summary>
        public SerialLinkSettings ElectroacousticTester { get; set; } = new();

        /// <summary>电子负载（预留，默认禁用）</summary>
        public SerialLinkSettings ElectronicLoad { get; set; } = new();

        /// <summary>
        /// 当前启用血压模拟仪的测试参数：康伟达读康伟达节点、BPA700/未选择读 BPA700 节点。
        /// 测试模块（M03/M04/M05 等）统一从这里取参，不再硬编码 BPA700Simulator.TestParams，
        /// 避免“连的康伟达、读的却是 BPA700 参数”的误导。
        /// </summary>
        public BpaTestParams ActiveBpTestParams
            => ActiveSimulator == SimulatorType.Kangwenda ? KangwendaSimulator.TestParams : BPA700Simulator.TestParams;

        /// <summary>当前启用血压模拟仪的静态加压超时（秒）：随 ActiveSimulator 路由</summary>
        public int ActiveStaticPressureTimeoutSeconds
            => ActiveSimulator == SimulatorType.Kangwenda ? KangwendaSimulator.StaticPressureTimeoutSeconds : BPA700Simulator.StaticPressureTimeoutSeconds;

        /// <summary>当前启用血压模拟仪的压力单位：BPA700 可配 mmHg/kPa，康伟达协议固定 mmHg</summary>
        public string ActivePressureUnit
            => ActiveSimulator == SimulatorType.BPA700 ? BPA700Simulator.PressureUnit : "mmHg";
    }
}
