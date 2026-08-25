namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 血压模拟仪类型（同一时刻只启用一台）
    /// </summary>
    public enum SimulatorType
    {
        /// <summary>未启用</summary>
        None = 0,

        /// <summary>康伟达 NIBP 模拟仪（串口 232）</summary>
        Kangwenda = 1,

        /// <summary>鲸扬 BPA700（USB 虚拟串口 VCP）</summary>
        BPA700 = 2
    }
}
