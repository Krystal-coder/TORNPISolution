namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 康伟达 NIBP 无创血压模拟仪连接与测试设置。
    /// 在普通串口参数基础上增加康伟达专属配置：
    /// 压力单位固定 mmHg（协议无单位切换），静态加压超时与测试参数独立于 BPA700 存放，
    /// 测试模块按仪器链接配置选择的模拟仪类型读取各自的参数（见 InstrumentConnectionConfig.ActiveBpTestParams）。
    /// </summary>
    public class KangwendaSettings : SerialLinkSettings
    {
        /// <summary>静态压力充气超时时间（秒，默认 180s）。康伟达逐点加压收敛（|回读-目标|&lt;1.5mmHg 保持 2.5s）
        /// 的兜底上限，超时后抛出 TimeoutException 并报告实际达到的压力值。</summary>
        public int StaticPressureTimeoutSeconds { get; set; } = 180;

        /// <summary>血压模拟仪测试参数（静态压力 / 动态血压 / 超压等，与 BPA700 同结构、独立存放）</summary>
        public BpaTestParams TestParams { get; set; } = new();
    }
}
