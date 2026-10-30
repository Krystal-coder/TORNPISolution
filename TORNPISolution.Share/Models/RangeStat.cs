namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 区间统计查询条件：按完成时间闭区间（To 含当天全天）+ 工位/方案可选筛选。
    /// 历史页统计 tab 与统计 PDF 用同一个查询、同一份结果——界面只看数，PDF 多画图。
    /// </summary>
    public class RangeStatQuery
    {
        public DateTime From { get; set; }

        public DateTime To { get; set; }

        /// <summary>工位筛选（null = 不限；当前只有"整机"，给未来单板/烧录混库预留）</summary>
        public string? Station { get; set; }

        /// <summary>方案名筛选（null = 不限）</summary>
        public string? PlanName { get; set; }
    }

    /// <summary>
    /// 区间统计结果：汇总 + 模块通过率 + NG 原因汇总 + 按天趋势。
    /// 计数口径与单轮报表一致：只统计参与判定的项（只展示/不判定项不计）。
    /// </summary>
    public class RangeStatResult
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string? Station { get; set; }
        public string? PlanName { get; set; }

        public int RunCount { get; set; }

        public int TotalPass { get; set; }
        public int TotalFail { get; set; }
        public int TotalNotTested { get; set; }

        /// <summary>整体通过率（通过 / (通过+失败)；无数据为 0）</summary>
        public double PassRate => TotalPass + TotalFail > 0 ? (double)TotalPass / (TotalPass + TotalFail) : 0;

        public string PassRateText => $"{(PassRate * 100):0.0}%";

        /// <summary>页标题与文件名用的区间文本</summary>
        public string RangeText => $"{From:yyyy-MM-dd} ~ {To:yyyy-MM-dd}";

        public bool HasData => RunCount > 0;

        /// <summary>各模块通过率（按 ModuleId 排序）</summary>
        public List<ModuleStat> Modules { get; set; } = new();

        /// <summary>NG 原因汇总：失败测试项按项名聚合，备注取最近一次失败的失败点备注</summary>
        public List<NgReason> NgReasons { get; set; } = new();

        /// <summary>按天的趋势（日期升序）</summary>
        public List<TrendPoint> Trend { get; set; } = new();
    }

    /// <summary>一个模块在区间内的通过/失败/未测计数与通过率</summary>
    public class ModuleStat
    {
        public string ModuleId { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int NotTestedCount { get; set; }

        public int JudgedCount => PassCount + FailCount;

        /// <summary>界面比例条配色用：有失败即红</summary>
        public bool HasFail => FailCount > 0;

        public double PassRate => JudgedCount > 0 ? (double)PassCount / JudgedCount : 0;

        public string PassRateText => $"{(PassRate * 100):0.0}%";

        public string CountsText => $"通过 {PassCount} · 失败 {FailCount} · 未测 {NotTestedCount}";
    }

    /// <summary>NG 原因汇总行：一个测试项一条（区间内失败几次）</summary>
    public class NgReason
    {
        public string ModuleId { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;

        public int FailCount { get; set; }

        /// <summary>最近一次失败留下的备注（失败点 Remark；没有就 null）</summary>
        public string? LastRemark { get; set; }

        public string RemarkText => string.IsNullOrEmpty(LastRemark) ? "—" : LastRemark;
    }

    /// <summary>按天的趋势点：当天轮次数与通过/失败计数</summary>
    public class TrendPoint
    {
        public DateTime Date { get; set; }
        public string DateText => Date.ToString("MM-dd");
        public int RunCount { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }

        public string CountsText => $"{RunCount} 轮 · 通过 {PassCount} · 失败 {FailCount}";

        public double PassRate => PassCount + FailCount > 0 ? (double)PassCount / (PassCount + FailCount) : 0;

        public string PassRateText => $"{(PassRate * 100):0.0}%";
    }
}
