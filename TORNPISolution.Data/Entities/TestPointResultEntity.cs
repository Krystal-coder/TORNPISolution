namespace TORNPISolution.Data
{
    /// <summary>
    /// 逐点子表：只有带子表的测试项才有行，用 <see cref="Schema"/> 区分口径（一轮一项的 Channel 行、
    /// BP-004 的 RepeatMatrix 行、BP-009 的 Event 行等）。统计量列是重复性图表（折线+统计卡）的数据源——
    /// BP-005 直接来自 Repeat 行，BP-004 由执行端补数值统计行提供。波形采样点不建列，要追溯曲线另加表。
    /// </summary>
    public class TestPointResultEntity
    {
        public int Id { get; set; }

        public int ItemResultId { get; set; }

        /// <summary>子节点名称，如 "收缩压"、"第2轮"、"均值"、"极差"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>列口径：Point / Channel / Group / Repeat / RepeatMatrix / Event / Slice</summary>
        public string Schema { get; set; } = "Point";

        /// <summary>本行量纲：混合量纲的通道口径靠它区分</summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>目标值（配置的压力点/心率点/设定值）</summary>
        public double TargetPressure { get; set; }

        /// <summary>仪器测试值（稳压读回均值；null = 仪器未到位）</summary>
        public double? InstrumentValue { get; set; }

        /// <summary>产品上报值（null = 产品未连接/无响应）</summary>
        public double? ProductValue { get; set; }

        /// <summary>误差 |产品−仪器|（任一值缺失为 null）</summary>
        public double? Error { get; set; }

        public bool Pass { get; set; }

        /// <summary>失败原因（PASS 时为空）</summary>
        public string Remark { get; set; } = string.Empty;

        /// <summary>多轮统计（非统计行为 null）；<see cref="Range"/> 是重复性项的判定量</summary>
        public double? Average { get; set; }

        public double? Max { get; set; }

        public double? Min { get; set; }

        public double? Range { get; set; }

        /// <summary>自定义单元格文本（JSON 数组）：Event/Slice 等执行端直给文本的口径用</summary>
        public string? CellsJson { get; set; }

        /// <summary>末列是否显示判定：统计中间行（均值/极大/极小）为 false，免得空值被误读成 PASS</summary>
        public bool ShowVerdict { get; set; } = true;

        public int SortOrder { get; set; }

        public TestItemResultEntity? ItemResult { get; set; }
    }
}
