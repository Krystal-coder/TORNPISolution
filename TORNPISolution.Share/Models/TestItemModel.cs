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
        /// 是否带波形显示（行内波形折叠按钮）：
        /// 该测试项执行时会上抛实时采样点（压力曲线/仪器输出波形）的项在 Spec 定义时置 true，
        /// 波形归属与测试项定义放在一起，避免 UI 维护项名白名单。
        /// </summary>
        public bool ShowWaveform { get; set; }

        /// <summary>
        /// 测试值来源标识（决定 UI 双列显示位置）：
        ///   Instrument（默认）= 测试值为仪器侧值，显示在"仪器测试值"列；
        ///   Product = 测试值为产品上报值，显示在"产品上报值"列（如产品信息获取/状态查询）；
        ///   Both = 仪器值与产品上报值双列显示（精度比对类测试项）；
        ///   Points = 逐点判定项（BP-003）：父行双值列只显示显式回传值（通常为空），逐点双值在行内子表，主值为最差点误差。
        /// </summary>
        public string ValueSource { get; set; } = "Instrument";

        /// <summary>
        /// 隐藏数值规格范围（产品信息获取/当前状态查询等通讯检查项）：
        /// 判定规则是“有响应即通过”（内部仍按 1/0 项判定），不存在数值范围，
        /// 规格列显示“有响应即通过”而非无意义的“1 ~ 1”。
        /// </summary>
        public bool HideSpecRange { get; set; }

        /// <summary>
        /// 字符串规格（非数值规格项用，如嵌软版本管控）：
        /// 非空时规格列直接显示该文本，判定由模块执行处按该文本比对；
        /// 为空时回落到 HideSpecRange/数值范围的显示规则。
        /// </summary>
        public string SpecText { get; set; } = string.Empty;
    }
}
