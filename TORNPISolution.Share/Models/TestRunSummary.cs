namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 历史数据页的轮次列表行：一轮一条（编号/工位/方案/模块/判定/计数/MAC）。
    /// 数据来自 TestRun 表；ModulesText 由 ModulesJson 快照解析——历史列表不 join 就能显示测了哪些模块。
    /// </summary>
    public class TestRunSummary
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

        /// <summary>本轮模块 ID 快照（如 A07/A17/A04），由 ModulesJson 解析</summary>
        public List<string> ModuleIds { get; set; } = new();

        public string ModulesText => ModuleIds.Count > 0 ? string.Join(" / ", ModuleIds) : "—";

        public string TimeText => $"{FinishedAt:MM-dd HH:mm}（历时 {ElapsedText}）";

        /// <summary>无 MAC 的轮次返回空串，界面按它隐藏整行</summary>
        public string MacText => string.IsNullOrEmpty(ProductMAC) ? string.Empty : $"MAC：{ProductMAC}";

        private string ElapsedText
        {
            get
            {
                var span = FinishedAt - StartedAt;
                return span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}分{span.Seconds}秒" : $"{(int)span.TotalSeconds}秒";
            }
        }

        /// <summary>判定徽章文本（与报表/界面同口径：有失败即 FAIL，全过才 PASS）</summary>
        public string VerdictText => OverallResult switch
        {
            "Fail" => "FAIL",
            "Pass" => "PASS",
            _ => "未完成",
        };

        /// <summary>计数摘要（通过/失败/未测）</summary>
        public string CountsText => $"通过 {PassCount} · 失败 {FailCount} · 未测 {NotTestedCount}";
    }
}
