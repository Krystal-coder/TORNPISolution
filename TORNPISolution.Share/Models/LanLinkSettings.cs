namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 单台仪器的网口（TCP）链接参数
    /// </summary>
    public class LanLinkSettings
    {
        /// <summary>IP 地址</summary>
        public string IpAddress { get; set; } = "192.168.1.1";

        /// <summary>TCP 端口（SCPI Raw Socket 默认 5025）</summary>
        public int Port { get; set; } = 5025;
    }
}
