namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 单个测试项的一次执行结果（TestItem 测量后回传给 UI）。
    /// 与 Spec 的关系：
    ///   Spec 定义测试项清单与静态规格（项名/说明/单位/默认上下限）；
    ///   实测值与"本次运行的动态规格"由 TestItem 运行时产生、装在这个类里回传。
    /// 动态规格：部分测试项的规格要按仪器读回的标准值现算
    ///   （如血压：SpecMin/SpecMax = 仪器读回值 ± 容差），
    ///   此时给 SpecMin/SpecMax 赋值，UI 用动态规格覆盖显示并判定；
    ///   不赋值（null）的项按 Spec 里的静态规格判定。
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

        /// <summary>普通项便捷构造：只有测试值，按 Spec 静态规格判定</summary>
        public static TestItemResult Of(double value) => new() { Value = value };

        /// <summary>文本展示项便捷构造：主值参与 1/0 判定，仪器列显示读回的文本（如设备信息）</summary>
        public static TestItemResult OfText(double value, string text)
            => new() { Value = value, InstrumentText = text };

        /// <summary>动态规格项便捷构造：测试值 + 本次运行按仪器值现算的规格上下限</summary>
        public static TestItemResult WithDynamicSpec(double value, double specMin, double specMax)
            => new() { Value = value, SpecMin = specMin, SpecMax = specMax };

        /// <summary>
        /// 双值比对项便捷构造：主判定值 = 产品上报值，动态规格 = 仪器值 ± 容差
        /// （如 ±2%/±2BPM 类精度项：产品上报值落在 [仪器值−容差, 仪器值+容差] 内为 PASS）。
        /// </summary>
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
