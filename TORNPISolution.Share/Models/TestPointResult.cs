namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 行详情子表的列口径键（执行端指定，UI 按此生成列头与单元格取值）。
    /// 同一测试项的 Points 必须同一口径，列数随口径变化，因此列头不再写死在界面里。
    /// </summary>
    public static class TestPointSchemas
    {
        /// <summary>逐点口径：目标/仪器测试值/产品上报值/误差（默认，血压静态类项目用）</summary>
        public const string Point = "Point";

        /// <summary>通道口径：设定值/回读值/偏差（BP-004 轮次项的收缩压/舒张压/心率三行）</summary>
        public const string Channel = "Channel";

        /// <summary>分组口径：与 <see cref="Channel"/> 同列（设定值/回读值/偏差），首列写“分组”（BP-005 心率精度按点位分组）</summary>
        public const string Group = "Group";

        /// <summary>单路多轮统计口径：设定值/均值/与设定偏差/极大极小/极差（BP-005 心率平均值按点位一行，只展示不判定）</summary>
        public const string Repeat = "Repeat";

        /// <summary>多路重复性口径：收缩压/舒张压两列，每轮一行 + 统计行（单元格文本由执行端直给 Cells）</summary>
        public const string RepeatMatrix = "RepeatMatrix";

        /// <summary>事件时刻口径：时刻(s)/压力(mmHg)/说明（BP-009 过压保护的各关键时刻与触发点，只记录不判定；单元格文本由执行端直给 Cells）</summary>
        public const string Event = "Event";

        /// <summary>分段口径：速率/时长/压力区间（BP-007 匀速段切段速率，文档未给“稳定”量化上限，只展示不判定；单元格文本由执行端直给 Cells）</summary>
        public const string Slice = "Slice";
    }

    /// <summary>
    /// 记录目标值、仪器读回值、产品上报值、误差与判定，多轮统计另带 均值/极大/极小/极差
    /// </summary>
    public class TestPointResult
    {
        /// <summary>子节点名称，如 "测试点1"、"收缩压"、"第2轮"、"极差"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>列口径（见 <see cref="TestPointSchemas"/>），决定本行走哪套动态列头</summary>
        public string Schema { get; set; } = TestPointSchemas.Point;

        /// <summary>本行的量纲（如 mmHg/BPM，随行走，混合量纲的通道口径靠它区分）</summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>目标压力（配置勾选的压力点，mmHg）</summary>
        public double TargetPressure { get; set; }

        /// <summary>仪器测试值（模拟仪稳压读回均值，至少稳定 2~3s；null = 仪器未到位）</summary>
        public double? InstrumentValue { get; set; }

        /// <summary>产品上报值（稳压保持窗内读回；null = 产品未连接/无响应）</summary>
        public double? ProductValue { get; set; }

        /// <summary>误差 |产品上报值 − 仪器测试值|（mmHg；任一值缺失为 null）</summary>
        public double? Error { get; set; }

        /// <summary>该点判定（误差 ≤2mmHg 且双值齐全为 PASS）</summary>
        public bool Pass { get; set; }

        /// <summary>失败原因（PASS 时为空），如 "无产品上报值（产品未连接或无响应）"</summary>
        public string Remark { get; set; } = string.Empty;

        /// <summary>多轮统计：均值（非统计行为 null）</summary>
        public double? Average { get; set; }

        /// <summary>多轮统计：极大值</summary>
        public double? Max { get; set; }

        /// <summary>多轮统计：极小值</summary>
        public double? Min { get; set; }

        /// <summary>多轮统计：极差 |最大−最小|（重复性项的判定量）</summary>
        public double? Range { get; set; }

        /// <summary>自定义单元格文本</summary>
        public List<string>? Cells { get; set; }

        /// <summary>末列是否显示判定（统计中间行如 均值/极大/极小 不判定，留空免得被误读成 PASS）</summary>
        public bool ShowVerdict { get; set; } = true;

        /// <summary>目标压力显示文本</summary>
        public string TargetText => $"{TargetPressure:0.#}";

        /// <summary>仪器测试值显示文本（压力固定 2 位小数，如 59.99）</summary>
        public string InstrumentText => InstrumentValue.HasValue ? $"{InstrumentValue.Value:0.00}" : "--";

        /// <summary>产品上报值显示文本（压力固定 2 位小数）</summary>
        public string ProductText => ProductValue.HasValue ? $"{ProductValue.Value:0.00}" : "--";

        /// <summary>误差显示文本（固定 2 位小数）</summary>
        public string ErrorText => Error.HasValue ? $"{Error.Value:0.00}" : "--";

        /// <summary>判定显示文本（FAIL 附带原因）</summary>
        public string ResultDisplay => Pass ? "PASS" : $"FAIL（{Remark}）";
    }
}
