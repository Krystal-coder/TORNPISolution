namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 单条测试项定义（测试内容 + 规格）
    /// </summary>
    public class TestItemModel
    {
        /// <summary>测试内容名称，如 "输出电压"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>测试内容说明</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>规格下限</summary>
        public double LowerLimit { get; set; }

        /// <summary>规格上限</summary>
        public double UpperLimit { get; set; }

        /// <summary>单位，如 V / A / Ω</summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>
        /// 波形呈现模式（无 / 行内折叠 / ECG Signal）：
        /// </summary>
        public WaveformPresentation Waveform { get; set; } = WaveformPresentation.None;

        /// <summary>
        /// 曲线纵轴单位（压力类为 mmHg，电位类为微伏/毫伏）：与规格单位 Unit 不同源，
        /// 放气速率项规格是 mmHg/s、心率项规格是 BPM，曲线画的仍是压力；空 = 沿用 Unit
        /// </summary>
        public string WaveformUnit { get; set; } = string.Empty;

        /// <summary>曲线横轴倍率的配置来源（由目录声明，None = 用界面默认倍率）</summary>
        public WaveformConfigSource WaveformConfigSource { get; set; } = WaveformConfigSource.None;

        /// <summary>
        /// 明细表分节标题：同一模块内这个值相同的相邻项归到同一节（节头可折叠），由 Spec 目录声明
        /// </summary>
        public string GroupLabel { get; set; } = string.Empty;

        /// <summary>
        /// 测试值来源标识
        ///   Instrument（默认）= 测试值为仪器侧值，显示在"仪器测试值"列；
        ///   Product = 测试值为产品上报值，显示在"产品上报值"列（如产品信息获取/状态查询）；
        ///   Both = 仪器值与产品上报值双列显示（精度比对类测试项）；
        ///   Error = 误差口径项（BP-003 一点一项）：主值为 |产品−仪器| 误差按静态规格判定，仪器列=稳压值、产品列=上报值（缺值显示 --，不回退主值）；
        ///   Points = 逐点判定项：父行双值列只显示显式回传值（通常为空），逐点双值在行内子表，主值为最差点误差。
        /// </summary>
        public string ValueSource { get; set; } = "Instrument";

        /// <summary>
        /// 隐藏数值规格范围（产品信息获取/当前状态查询等通讯检查项）：
        /// 判定规则是“有响应即通过”（内部仍按 1/0 项判定），不存在数值范围，
        /// 规格列显示“有响应即通过”而非无意义的“1 ~ 1”。
        /// </summary>
        public bool HideSpecRange { get; set; }

        /// <summary>
        /// 字符串规格
        /// </summary>
        public string SpecText { get; set; } = string.Empty;
    }
}
