namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 测试项的波形呈现模式
    /// </summary>
    public enum WaveformPresentation
    {
        None = 0,

        Inline = 1,

        Overview = 2,
    }

    /// <summary>
    /// 波形横轴倍率的配置来源：由 Spec 目录声明，UI 不按项名猜（项名只是配置条目的存储键，不携带语义）
    /// </summary>
    public enum WaveformConfigSource
    {
        /// <summary>不吃配置，横轴用界面默认倍率</summary>
        None = 0,

        /// <summary>读血压模拟仪测试配置里的波形显示项</summary>
        BloodPressureSimulator = 1,

        /// <summary>读 AECG100 配置里的波形显示项</summary>
        Ecg100 = 2,
    }

    /// <summary>
    /// ECG Signal窗口的显示参数
    /// </summary>
    public class WaveformOverviewProfile
    {
        /// <summary>窗口标题；留空则按模块名自动生成“{模块名} 波形（各档位ECG Signal）”</summary>
        public string Title { get; set; } = string.Empty;   

        /// <summary>纵轴单位，如 mV / mmHg（同时作为底栏 Y Min / Y Max 输入框后面的单位字样）</summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>横轴窗口宽度默认值（秒）：底栏 X Width 的初值，即“整个图幅显示多少秒”</summary>
        public double DefaultXWidthSeconds { get; set; } = 12;

        /// <summary>纵轴下限默认值：底栏 Y Min 的初值（与 <see cref="DefaultYMax"/> 一起定初始量程）</summary>
        public double DefaultYMin { get; set; } = -5;

        /// <summary>纵轴上限默认值：底栏 Y Max 的初值</summary>
        public double DefaultYMax { get; set; } = 5;

        /// <summary>曲线图例名：第 0 条 = 设定侧（仪器发出），第 1 条 = 回读侧（产品上报）；只有一条曲线时填一个</summary>
        public IReadOnlyList<string> SeriesLabels { get; set; } = new[] { "仪器输出", "产品回读" };

        /// <summary>
        /// 段标注截断标记
        /// </summary>
        public string SegmentLabelAfter { get; set; } = string.Empty;

        /// <summary>
        /// 默认是否把每段设定侧曲线按段中点平移回零（去直流）
        /// </summary>
        public bool StripDcByDefault { get; set; } = true;
    }
}
