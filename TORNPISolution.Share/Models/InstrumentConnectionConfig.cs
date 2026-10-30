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
    /// 仪器链接配置：汇总所有测试仪器的串口/网口参数，由配置服务持久化，界面与设备层共享同一份定义。
    /// </summary>
    public class InstrumentConnectionConfig
    {
        /// <summary>被测产品（DUT，串口 232，默认 921600bps）</summary>
        public SerialLinkSettings Product { get; set; } = new() { BaudRate = 921600 };

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

        /// <summary>鲸扬 AECG100 多功能综合性能测试仪（COM 口留空为自动选择）</summary>
        public Aecg100Settings Aecg100Tester { get; set; } = new();

        /// <summary>THS_HF NFC 读卡器（岩柱 YZ806/YZ906，串口连接，默认 115200bps）</summary>
        public SerialLinkSettings NfcReader { get; set; } = new() { BaudRate = 115200 };

        /// <summary>艾德克斯 IT6411 双通道直流电源（网口 SCPI Raw Socket，默认端口 5025）</summary>
        public LanLinkSettings It6411PowerSupply { get; set; } = new() { IpAddress = "192.168.1.1", Port = 5025 };

        /// <summary>CRY6151B 电声测试仪（预留，默认禁用）</summary>
        public SerialLinkSettings ElectroacousticTester { get; set; } = new();

        /// <summary>电子负载（预留，默认禁用）</summary>
        public SerialLinkSettings ElectronicLoad { get; set; } = new();

        // ==================== 单板工位仪器（都是网口，台数写在清单里，用不到的把 Enabled 关掉） ====================

        /// <summary>
        /// CRY5242/5243 四通道电流电压综测仪清单：Port 填通道 A 的端口，B/C/D 依次 +1（5025~5028）。
        /// 清单顺序即供电槽位顺序（仅 Enabled 行参与编号），每台按硬件固定的四个通道连着占四项。
        /// </summary>
        public List<Cry5242Settings> Cry5242Testers { get; set; } = new()
        {
            new() { Name = "综测仪1", IpAddress = "169.254.1.32", Port = 5025 },
            new() { Name = "综测仪2", IpAddress = "169.254.1.33", Port = 5025 },
        };

        /// <summary>
        /// 研为 EW-DTU09 蓝牙网关清单：每台一个 IP:端口，一条 AT 指令连接；端口为网关自身的 TCP 端口，不固定，以网关上位配置为准。
        /// 清单顺序即槽位顺序（仅 Enabled 行参与编号），一项对应一个地址与一个信号强度。
        /// </summary>
        public List<BluetoothGatewaySettings> BluetoothGateways { get; set; } = new()
        {
            new() { Name = "网关1", IpAddress = "192.168.1.101", Port = 8080 },
            new() { Name = "网关2", IpAddress = "192.168.1.102", Port = 8080 },
            new() { Name = "网关3", IpAddress = "192.168.1.103", Port = 8080 },
            new() { Name = "网关4", IpAddress = "192.168.1.104", Port = 8080 },
            new() { Name = "网关5", IpAddress = "192.168.1.105", Port = 8080 },
            new() { Name = "网关6", IpAddress = "192.168.1.106", Port = 8080 },
            new() { Name = "网关7", IpAddress = "192.168.1.107", Port = 8080 },
            new() { Name = "网关8", IpAddress = "192.168.1.108", Port = 8080 },
        };

        /// <summary>海康 MV-ID 读码器清单：触发端口与结果端口各一条连接，参数须与相机侧一致；清单顺序即槽位顺序（仅 Enabled 行参与编号），每台按 SlotsPerReader 连着占几个槽位</summary>
        public List<HikBarcodeReaderSettings> BarcodeReaders { get; set; } = new()
        {
            new() { Name = "读码器1", IpAddress = "192.168.1.16", Port = 2002 },
            new() { Name = "读码器2", IpAddress = "192.168.1.17", Port = 2002 },
            new() { Name = "读码器3", IpAddress = "192.168.1.18", Port = 2002 },
            new() { Name = "读码器4", IpAddress = "192.168.1.19", Port = 2002 },
        };

        /// <summary>当前启用血压模拟仪的测试参数：康伟达读康伟达节点，BPA700/未选择读 BPA700 节点；测试模块（A03/A04/A05 等）统一从这里取参</summary>
        public BpaTestParams ActiveBpTestParams
            => ActiveSimulator == SimulatorType.Kangwenda ? KangwendaSimulator.TestParams : BPA700Simulator.TestParams;

        /// <summary>当前启用血压模拟仪的静态加压超时（秒）</summary>
        public int ActiveStaticPressureTimeoutSeconds
            => ActiveSimulator == SimulatorType.Kangwenda ? KangwendaSimulator.StaticPressureTimeoutSeconds : BPA700Simulator.StaticPressureTimeoutSeconds;

        /// <summary>当前启用血压模拟仪的压力单位：BPA700 可配 mmHg/kPa，康伟达协议固定 mmHg</summary>
        public string ActivePressureUnit
            => ActiveSimulator == SimulatorType.BPA700 ? BPA700Simulator.PressureUnit : "mmHg";
    }
}
