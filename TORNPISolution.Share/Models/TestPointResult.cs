namespace TORNPISolution.Share.Models
{
    /// <summary>行详情子表的列口径键（执行端指定，UI 按此生成列头与单元格取值）：同一测试项的 Points 必须同一口径；Event/Slice/RepeatMatrix 的单元格文本由执行端直给 <see cref="TestPointResult.Cells"/></summary>
    public static class TestPointSchemas
    {
        /// <summary>逐点口径：目标/仪器测试值/产品上报值/误差（默认，血压静态类项目用）</summary>
        public const string Point = "Point";

        /// <summary>通道口径：设定值/回读值/偏差（BP-004 轮次项的收缩压/舒张压/心率三行）</summary>
        public const string Channel = "Channel";

        /// <summary>分组口径：与 <see cref="Channel"/> 同列，首列写“分组”（BP-005 心率精度按点位分组）</summary>
        public const string Group = "Group";

        /// <summary>单路多轮统计口径：设定值/均值/与设定偏差/极大极小/极差（BP-005 平均值项一行，只展示不判定）</summary>
        public const string Repeat = "Repeat";

        /// <summary>多路重复性口径：收缩压/舒张压两列，每轮一行 + 统计行</summary>
        public const string RepeatMatrix = "RepeatMatrix";

        /// <summary>数值统计口径（BP-004 重复性）：Average/Max/Min/Range/Error 落在数值列，供入库与报表图表直读，不在界面子表显示</summary>
        public const string RepeatStats = "RepeatStats";

        /// <summary>事件时刻口径：时刻(s)/压力(mmHg)/说明（BP-009 各关键时刻，只记录不判定）</summary>
        public const string Event = "Event";

        /// <summary>分段口径：速率/时长/压力区间（BP-007 匀速段切段速率，只展示不判定）</summary>
        public const string Slice = "Slice";
    }

    /// <summary>
    /// 记录目标值、仪器读回值、产品上报值、误差与判定，多轮统计另带 均值/极大/极小/极差。
    /// 显示文本一律缺失出 "--"，数值固定 2 位小数（压力类显示精度规范）。
    /// </summary>
    public class TestPointResult
    {
        /// <summary>子节点名称，如 "测试点1"、"收缩压"、"第2轮"、"极差"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>列口径（见 <see cref="TestPointSchemas"/>），决定本行走哪套动态列头</summary>
        public string Schema { get; set; } = TestPointSchemas.Point;

        /// <summary>本行的量纲（随行走，混合量纲的通道口径靠它区分）</summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>目标压力（配置勾选的压力点）</summary>
        public double TargetPressure { get; set; }

        /// <summary>仪器测试值（模拟仪稳压读回均值；null = 仪器未到位）</summary>
        public double? InstrumentValue { get; set; }

        /// <summary>产品上报值（null = 产品未连接/无响应）</summary>
        public double? ProductValue { get; set; }

        /// <summary>误差 |产品上报值 − 仪器测试值|（任一值缺失为 null）</summary>
        public double? Error { get; set; }

        /// <summary>该点判定（阈值随口径：BP-003 逐点 ±0.5、BP-004 通道 ±2、BP-005 逐轮 max(5, 设定×5%)）</summary>
        public bool Pass { get; set; }

        /// <summary>失败原因（PASS 时为空），如 "无产品上报值（产品未连接或无响应）"</summary>
        public string Remark { get; set; } = string.Empty;

        /// <summary>多轮统计（非统计行为 null）；<see cref="Range"/> 是重复性项的判定量</summary>
        public double? Average { get; set; }

        public double? Max { get; set; }

        public double? Min { get; set; }

        public double? Range { get; set; }

        /// <summary>自定义单元格文本</summary>
        public List<string>? Cells { get; set; }

        /// <summary>末列是否显示判定：统计中间行（均值/极大/极小）不判定</summary>
        public bool ShowVerdict { get; set; } = true;

        public string TargetText => $"{TargetPressure:0.#}";

        public string InstrumentText => InstrumentValue.HasValue ? $"{InstrumentValue.Value:0.00}" : "--";

        public string ProductText => ProductValue.HasValue ? $"{ProductValue.Value:0.00}" : "--";

        public string ErrorText => Error.HasValue ? $"{Error.Value:0.00}" : "--";

        public string ResultDisplay => Pass ? "PASS" : $"FAIL（{Remark}）";
    }
}
