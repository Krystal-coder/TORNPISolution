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
        /// <summary>测试值（产品读回来的值；1/0 类功能项为 1=成功 0=失败）</summary>
        public double Value { get; set; }

        /// <summary>本次运行的动态规格下限（null = 用 Spec 静态下限）</summary>
        public double? SpecMin { get; set; }

        /// <summary>本次运行的动态规格上限（null = 用 Spec 静态上限）</summary>
        public double? SpecMax { get; set; }

        /// <summary>普通项便捷构造：只有测试值，按 Spec 静态规格判定</summary>
        public static TestItemResult Of(double value) => new() { Value = value };

        /// <summary>动态规格项便捷构造：测试值 + 本次运行按仪器值现算的规格上下限</summary>
        public static TestItemResult WithDynamicSpec(double value, double specMin, double specMax)
            => new() { Value = value, SpecMin = specMin, SpecMax = specMax };
    }
}
