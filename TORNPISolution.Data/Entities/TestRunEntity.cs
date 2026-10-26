namespace TORNPISolution.Data
{
    /// <summary>
    /// 测试轮次表：一整轮方案跑完 = 一条记录。不按工位建表，未来单板/烧录复用本表用 <see cref="Station"/> 区分；
    /// 产品 MAC/SN/工号是"这一轮测的是哪台产品"的属性，放本表而不是测试项表。
    /// </summary>
    public class TestRunEntity
    {
        public int Id { get; set; }

        /// <summary>报告编号：日期+当天序号（如 20261022-0001），toast/历史列表/PDF 文件名共用同一个号</summary>
        public string ReportNo { get; set; } = string.Empty;

        /// <summary>工位：当前只写"整机"</summary>
        public string Station { get; set; } = "整机";

        /// <summary>方案名（未保存/未加载方案时存"未命名方案"）</summary>
        public string PlanName { get; set; } = string.Empty;

        /// <summary>本轮模块 ID 快照（JSON 数组，如 ["A07","A17","A04"]）：方案内模块可增删，快照保证老轮次能还原当时的组合</summary>
        public string ModulesJson { get; set; } = string.Empty;

        public DateTime StartedAt { get; set; }

        /// <summary>收口时刻：列表排序、区间统计筛选、报告编号都按它算（<see cref="CreatedAt"/> 是写库时刻，差几秒但语义不同）</summary>
        public DateTime FinishedAt { get; set; }

        /// <summary>整体判定：Pass / Fail / NotTested（任一小项失败即 Fail）</summary>
        public string OverallResult { get; set; } = "NotTested";

        /// <summary>本轮测试项计数（通过/失败/未测试）</summary>
        public int PassCount { get; set; }

        public int FailCount { get; set; }

        public int NotTestedCount { get; set; }

        /// <summary>产品 MAC：保存时从 A16「产品信息获取(MAC地址)」结果自动带入，测试员零操作</summary>
        public string? ProductMAC { get; set; }

        /// <summary>产品 SN：列预留、值留空（未来单板/烧录接入时启用）</summary>
        public string? ProductSN { get; set; }

        /// <summary>测试员工号：列预留、值留空（同上）</summary>
        public string? Operator { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<TestItemResultEntity> Items { get; set; } = new();
    }
}
