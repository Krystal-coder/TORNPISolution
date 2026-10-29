namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 单个测试项的一次执行结果。Spec 定义项清单与静态规格（项名/说明/单位/默认上下限）。
    /// 动态规格经本结果回传 UI：按仪器读回的标准值现算（如血压 SpecMin/SpecMax = 仪器读回值 ± 容差）。
    /// </summary>
    public class TestItemResult
    {
        /// <summary>测试值（用于判定的主值；1/0 类功能项为 1=成功 0=失败）</summary>
        public double Value { get; set; }

        /// <summary>仪器侧测试值（仪器设定/读回值；null = 该项无仪器侧值）</summary>
        public double? InstrumentValue { get; set; }

        /// <summary>产品上报值（产品串口回读值；null = 该项无产品上报值）</summary>
        public double? ProductValue { get; set; }

        /// <summary>本次运行的动态规格下限（null = 用 Spec 静态下限）</summary>
        public double? SpecMin { get; set; }

        /// <summary>本次运行的动态规格上限（null = 用 Spec 静态上限）</summary>
        public double? SpecMax { get; set; }

        /// <summary>仪器侧文本结果（如设备型号/固件版本/序列号等读回的字符串；设置后仪器列显示文本而非数值）</summary>
        public string? InstrumentText { get; set; }

        /// <summary>产品侧文本结果（产品串口读回的原始内容：嵌软版本/MAC、测试模式与状态等；设置后产品列显示文本而非数值）</summary>
        public string? ProductText { get; set; }

        /// <summary>逐点子结果（BP-003 等逐点测试项用）：UI 在测试项行下方子表逐点展示判定</summary>
        public List<TestPointResult>? Points { get; set; }

        /// <summary>演示/跳过标记：该项仅展示、不参与合格判定（结果保持“未测试”），用于无产品演示模式等场景</summary>
        public bool NotJudged { get; set; }

        /// <summary>
        /// 只展示不判定（如 BP-005 的平均值项）：测试值与行详情子表照常回填，结果列显示“—”。
        /// 与 <see cref="NotJudged"/> 的区别：后者连双值列与子表一起清空，本标记保留统计量供查看。
        /// </summary>
        public bool DisplayOnly { get; set; }

        /// <summary>
        /// 本项实测用时（秒）：先采完全部轮次、再批量上报结果的模块必须填这里，否则 UI 只能按上报时刻差算耗时。
        /// null = 由上报时刻差推导（逐项即时上报的模块不用填）。
        /// </summary>
        public double? MeasuredSeconds { get; set; }

        /// <summary>普通项便捷构造：只有测试值，按 Spec 静态规格判定</summary>
        public static TestItemResult Of(double value) => new() { Value = value };

        /// <summary>文本展示项便捷构造：主值参与 1/0 判定，仪器列显示读回的文本（如设备信息）</summary>
        public static TestItemResult OfText(double value, string text)
            => new() { Value = value, InstrumentText = text };

        /// <summary>产品文本展示项便捷构造：主值参与 1/0 判定，产品列显示读回的文本（如产品信息获取/状态查询）</summary>
        public static TestItemResult OfProductText(double value, string? productText)
            => new() { Value = value, ProductText = productText };

        /// <summary>演示不判定项便捷构造：仪器列显示演示文本，不参与判定（保持未测试）</summary>
        public static TestItemResult Demo(string text = "演示（不判定）")
            => new() { NotJudged = true, InstrumentText = text };

        /// <summary>动态规格项便捷构造：测试值 + 本次运行按仪器值现算的规格上下限</summary>
        public static TestItemResult WithDynamicSpec(double value, double specMin, double specMax)
            => new() { Value = value, SpecMin = specMin, SpecMax = specMax };

        /// <summary>双值比对项便捷构造：主判定值 = 产品上报值，动态规格 = 仪器值 ± 容差（±2%/±2BPM 类精度项：产品上报值落在 [仪器值−容差, 仪器值+容差] 内为 PASS）</summary>
        public static TestItemResult WithDualValues(double instrumentValue, double productValue, double specMin, double specMax)
            => new()
            {
                Value = productValue,
                InstrumentValue = instrumentValue,
                ProductValue = productValue,
                SpecMin = specMin,
                SpecMax = specMax
            };
    }
}
