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
    }
}
