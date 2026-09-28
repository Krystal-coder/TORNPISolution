namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 逐点测试子结果（BP-003 静态压力精度测试的子节点）：
    /// 每个压力点一条，记录目标压力、仪器稳压读回值、产品上报值、误差与判定，
    /// UI 在测试项行下方的子表逐点展示（哪个点 PASS / 哪个点 FAIL 一目了然）。
    /// </summary>
    public class TestPointResult
    {
        /// <summary>子节点名称，如 "测试点1"</summary>
        public string Name { get; set; } = string.Empty;

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
