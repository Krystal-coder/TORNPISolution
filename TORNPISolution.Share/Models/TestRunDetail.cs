namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 历史数据页的轮次明细（与 PDF 报告同一份数据、同一套口径，只是给界面用）：
    /// 轮次头信息 + 按模块分节的测试项列表；模块章节随方案动态变（一个模块就一节，多个就多节）。
    /// </summary>
    public class TestRunDetail
    {
        public string ReportNo { get; set; } = string.Empty;
        public string Station { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public string OverallResult { get; set; } = "NotTested";
        public string? ProductMAC { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int NotTestedCount { get; set; }

        /// <summary>总判定徽章文本（与界面/报表同口径）</summary>
        public string VerdictText => OverallResult switch
        {
            "Fail" => "FAIL",
            "Pass" => "PASS",
            _ => "未完成",
        };

        /// <summary>计数摘要（通过/失败/未测）</summary>
        public string CountsText => $"通过 {PassCount} · 失败 {FailCount} · 未测 {NotTestedCount}";

        public string TimeText => $"{StartedAt:MM-dd HH:mm} ~ {FinishedAt:HH:mm}";

        public List<ModuleDetail> Modules { get; set; } = new();
    }

    /// <summary>一个模块章节（判定徽章 + 计数 + 测试项）</summary>
    public class ModuleDetail
    {
        public string ModuleId { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string Verdict { get; set; } = "未完成";
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int NotTestedCount { get; set; }
        public List<ItemDetail> Items { get; set; } = new();

        /// <summary>计数摘要（通过/失败/未测）</summary>
        public string CountsText => $"通过 {PassCount} · 失败 {FailCount} · 未测 {NotTestedCount}";
    }

    /// <summary>一个测试项（名称/规格快照/测试值/判定 + 子表行文本）</summary>
    public class ItemDetail
    {
        public string ItemName { get; set; } = string.Empty;
        public string SpecText { get; set; } = string.Empty;
        public string ValueText { get; set; } = string.Empty;
        /// <summary>判定文本：PASS / FAIL / 未测 / 统计（只展示项）</summary>
        public string ResultText { get; set; } = "未测";
        /// <summary>子表行文本（Channel/Repeat/RepeatMatrix/Event/Slice 口径的项才有；缩进展示）</summary>
        public List<string> SubRows { get; set; } = new();
    }
}
