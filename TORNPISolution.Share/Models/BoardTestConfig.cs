namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 单板工位的可跑测试项：四项对应现有单板目录（B04 综测仪上电、B02 产品条码获取、B03 蓝牙功能测试），
    /// 结果里一个槽位一项，项名带"-槽位号"后缀。只定位到"哪一项"，不定位到第几路：整批八条通道一起跑，单条跑靠点名通道而非点名项。
    /// </summary>
    public enum BoardTestItemKind
    {
        /// <summary>供电电压（B04：综测仪该通道缓输出到设定值并回读）</summary>
        PowerVoltage,

        /// <summary>产品条码（B02：触发这一路对应的读码器取码）</summary>
        Barcode,

        /// <summary>蓝牙MAC地址（B03：这一路对应网关识别到的广播地址）</summary>
        BluetoothMac,

        /// <summary>蓝牙信号强度（B03：这一路对应网关采到的均值 dBm）</summary>
        BluetoothRssi,
    }

    /// <summary>测试项的中文叫法、属于哪个模块、以及结果项名的前缀口径</summary>
    public static class BoardTestItemKindExtensions
    {
        public static string ToDisplay(this BoardTestItemKind kind) => kind switch
        {
            BoardTestItemKind.PowerVoltage => "供电电压",
            BoardTestItemKind.Barcode => "产品条码",
            BoardTestItemKind.BluetoothMac => "蓝牙MAC地址",
            BoardTestItemKind.BluetoothRssi => "蓝牙信号强度",
            _ => kind.ToString(),
        };

        /// <summary>这一项由哪个测试模块执行（与单板目录同一份 Id）</summary>
        public static string ModuleId(this BoardTestItemKind kind) => kind switch
        {
            BoardTestItemKind.PowerVoltage => TestModuleIds.BoardPowerOn,
            BoardTestItemKind.Barcode => TestModuleIds.BoardBarcodeRead,
            _ => TestModuleIds.BoardBluetoothTest,
        };

        /// <summary>
        /// 结果项名前缀：执行端上报"供电电压-3"这类带槽位后缀的名，界面按前缀认项、按后缀取槽位号，必须与 SpecItems 一字不差。
        /// </summary>
        public static string ItemPrefix(this BoardTestItemKind kind) => kind switch
        {
            BoardTestItemKind.PowerVoltage => "供电电压",
            BoardTestItemKind.Barcode => "产品条码",
            BoardTestItemKind.BluetoothMac => "蓝牙MAC地址",
            BoardTestItemKind.BluetoothRssi => "蓝牙信号强度",
            _ => kind.ToString(),
        };
    }

    /// <summary>单板工位运行配置：只记"跑什么、跑哪几路"；仪器 IP/端口/阈值仍在 instruments.json</summary>
    public class BoardTestConfig
    {
        /// <summary>本轮要跑的测试项（八条通道共用这一组勾选）；默认先给供电与条码这两项</summary>
        public List<BoardTestItemKind> SelectedItems { get; set; } =
            new() { BoardTestItemKind.PowerVoltage, BoardTestItemKind.Barcode };

        /// <summary>通道清单，顺序就是通道号（第一行是通道 1）</summary>
        public List<BoardChannelSettings> Channels { get; set; } =
            new(Enumerable.Range(1, 8).Select(i => new BoardChannelSettings { Name = $"通道{i}" }));
    }

    /// <summary>一条测试通道：位号与是否参与整批。设备参数不在这里，那些仍归仪器链接配置管</summary>
    public class BoardChannelSettings
    {
        /// <summary>本通道是否参与整批：没接板子的路关掉，整批时跳过它</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>位号（界面与记录显示）</summary>
        public string Name { get; set; } = "通道";
    }
}
