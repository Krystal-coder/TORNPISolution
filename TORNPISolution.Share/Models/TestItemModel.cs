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
        /// 测试项执行状态标记：
        ///   true (DET，默认) = 正常执行测试逻辑，按代码写的跑；
        ///   false (TBD) = 跳过该测试小项，结果显示"未测试"。
        /// 调试时可将暂不具备条件的项改为 TBD，方便跑通整体流程。
        /// </summary>
        public bool Det { get; set; } = true;

        /// <summary>
        /// 是否带波形显示（行内波形折叠按钮）：
        /// 该测试项执行时会上抛实时采样点（压力曲线/仪器输出波形）的项在 Spec 定义时置 true，
        /// 波形归属与测试项定义放在一起，避免 UI 维护项名白名单。
        /// </summary>
        public bool ShowWaveform { get; set; }

        /// <summary>
        /// 测试值来源标识（决定 UI 双列显示位置）：
        ///   Instrument（默认）= 测试值为仪器侧值，显示在"仪器测试值"列；
        ///   Product = 测试值为产品上报值，显示在"产品上报值"列（如产品问询/状态查询）；
        ///   Both = 仪器值与产品上报值双列显示（精度比对类测试项）。
        /// </summary>
        public string ValueSource { get; set; } = "Instrument";
    }
}
