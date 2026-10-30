namespace TORNPISolution.Data
{
    /// <summary>
    /// 测试项结果表：一行一项。项名与行数随方案动态变（A05 按压力点、A06 按压力组×轮次、A21 按心率点×轮次）
    /// </summary>
    public class TestItemResultEntity
    {
        public int Id { get; set; }

        public int RunId { get; set; }

        /// <summary>模块 ID（如 A06）</summary>
        public string ModuleId { get; set; } = string.Empty;

        public string ModuleName { get; set; } = string.Empty;

        /// <summary>模块在方案内的执行序号（从 1 开始）</summary>
        public int Sequence { get; set; }

        /// <summary>测试项名（动态项名原样存文本）</summary>
        public string ItemName { get; set; } = string.Empty;

        public string Unit { get; set; } = string.Empty;

        /// <summary>值来源口径：Instrument / Product / Both / Error / Points（见 TestItemModel.ValueSource）</summary>
        public string ValueSource { get; set; } = "Instrument";

        /// <summary>用于判定的主值；1/0 功能项为 1/0；执行端没取到有效值（NaN）时存 null</summary>
        public double? Value { get; set; }

        /// <summary>仪器侧值（设定/读回值）</summary>
        public double? InstrumentValue { get; set; }

        /// <summary>产品上报值</summary>
        public double? ProductValue { get; set; }

        /// <summary>仪器侧文本（设备型号/固件版本/MAC 等读回的字符串）</summary>
        public string? InstrumentText { get; set; }

        /// <summary>产品侧文本（嵌软版本/MAC、测试模式与状态等）</summary>
        public string? ProductText { get; set; }

        /// <summary>本次运行规格下限快照（null = 用 Spec 静态下限）</summary>
        public double? SpecMin { get; set; }

        /// <summary>本次运行规格上限快照</summary>
        public double? SpecMax { get; set; }

        /// <summary>规格显示文本快照（含"有响应即通过"等字符串规格口径）</summary>
        public string SpecText { get; set; } = string.Empty;

        /// <summary>判定：Pass / Fail / NotTested</summary>
        public string Result { get; set; } = "NotTested";

        /// <summary>只展示不判定（如 BP-005 平均值项）：值与统计量可见，不进模块判定</summary>
        public bool DisplayOnly { get; set; }

        /// <summary>不判定（预留项/未配置判定区间）：保持未测试</summary>
        public bool NotJudged { get; set; }

        /// <summary>本项耗时（秒）：上一项上报（模块首项取派发时刻）到本项上报的墙钟；null = 该轮未采集</summary>
        public double? DurationSeconds { get; set; }

        /// <summary>模块内排序：报表按它还原章节顺序</summary>
        public int SortOrder { get; set; }

        public TestRunEntity? Run { get; set; }

        /// <summary>逐点子表（Channel/Repeat/RepeatMatrix/Event/Slice 口径的项才有行）</summary>
        public List<TestPointResultEntity> Points { get; set; } = new();
    }
}
