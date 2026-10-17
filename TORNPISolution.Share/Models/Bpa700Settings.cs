namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 鲸扬 BPA700 无创血压模拟仪连接与初始化设置。
    /// 在普通串口参数基础上增加 BPA700 专有的三项初始化配置：
    /// 患者类型（Category）、压力单位（PressureUnit）、袖带容量（AirReservoir），
    /// 血压仪初始化连接（A01）时随连接一起下发给仪器。
    /// </summary>
    public class Bpa700Settings : SerialLinkSettings
    {
        /// <summary>患者类型：Adult / Neonatal（默认 Adult）</summary>
        public string Category { get; set; } = "Adult";

        /// <summary>压力单位：mmHg / kPa（默认 mmHg）</summary>
        public string PressureUnit { get; set; } = "mmHg";

        /// <summary>袖带容量：500ml / 100ml（默认 500ml）</summary>
        public string AirReservoir { get; set; } = "500ml";

        /// <summary>静态压力充气超时时间（秒，默认 180s）。BPA700 充气到目标压力并等待 Stable 的最长时间，
        /// 超时后抛出 TimeoutException 并报告实际达到的压力值。设备气路异常时可适当增大。</summary>
        public int StaticPressureTimeoutSeconds { get; set; } = 180;

        /// <summary>血压仪测试参数配置（静态压力 / 动态血压 / 超压三类测试）</summary>
        public BpaTestParams TestParams { get; set; } = new();
    }
}
