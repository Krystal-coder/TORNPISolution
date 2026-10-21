namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 研为 EW-DTU09 蓝牙网关的链接参数（网口 TCP 上的 AT 文本指令，帧格式见 BluetoothGateway）。
    /// 网关侧的「数据协议」必须配成 AT 指令：这台也支持 JSON / ModbusTCP / websocket，
    /// 配成那几种时下发的 AT 指令不会被当指令解析。
    /// </summary>
    public class BluetoothGatewaySettings : LanLinkSettings
    {
        /// <summary>是否启用这台网关；未接入的关掉，建链与扫描都会跳过</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>位号，日志与界面用来区分多台（如「网关1」）</summary>
        public string Name { get; set; } = "网关";

        /// <summary>TCP 建链超时（毫秒）</summary>
        public int ConnectTimeoutMs { get; set; } = 5000;

        /// <summary>单条 AT 指令等待 OK/ERROR 的超时（毫秒）</summary>
        public int ResponseTimeoutMs { get; set; } = 5000;

        /// <summary>采集信号强度时等待的广播条数，攒够就收（条数越多越接近均值，耗时也越长）</summary>
        public int RssiSampleCount { get; set; } = 20;

        /// <summary>采集信号强度的总超时（毫秒），条数没攒够就到时的按已采到的部分返回</summary>
        public int RssiTimeoutMs { get; set; } = 5000;

        /// <summary>预期广播地址：留空则不做指定，取识别窗口里信号强度最高的那条广播；填了走网关侧过滤（AT+FLT），扫不到这个地址直接判失败、不拿别人的广播凑数，地址里的分隔符会被去掉且支持只填前几个字节做部分匹配</summary>
        public string ExpectedMac { get; set; } = string.Empty;

        /// <summary>识别阶段的不带过滤扫描窗口（毫秒）：产品开机到开始广播有间隔，这个窗口短了会扫空</summary>
        public int IdentifyWindowMs { get; set; } = 3000;

        /// <summary>信号强度合格下限（dBm）：本行采到的均值不低于它判 PASS（取负值，越接近 0 越强）</summary>
        public int RssiMinDbm { get; set; } = -75;
    }

    /// <summary>
    /// 一条扫描上报。基本格式十段：mac,mac_id,mac_type,data_type,rssi,tx_power,ch_index,scan_response,data_len,data；
    /// 请求了扫描回应包且数据段走 ASCII 时，末尾还会多出 scan_data_len,scan_data 两段。构造参数只留判定用得上的字段。
    /// </summary>
    public readonly record struct BluetoothScanInfo(
        string Mac,
        int Rssi,
        int TxPower,
        int ChannelIndex,
        string Data,
        string ScanResponseData)
    {
        public override string ToString() => $"{Mac} RSSI={Rssi}";
    }
}
