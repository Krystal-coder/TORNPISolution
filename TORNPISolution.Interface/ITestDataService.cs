using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 测试数据服务：整机（未来单板/烧录）测试结果的入库与查询。保存走单事务写 3 张表，失败整体回滚。
    /// </summary>
    public interface ITestDataService
    {
        /// <summary>建库 + 预热 EF 模型：进程内只真正执行一次，读写路径调它只是等预热完成</summary>
        Task PrepareStorageAsync(CancellationToken ct = default);

        /// <summary>保存一轮测试结果并生成报告编号（中止、全部项未测试的轮次不入库）</summary>
        /// <exception cref="InvalidOperationException">编号撞唯一索引等入库异常，由 UI 捕获后弹「重试保存」</exception>
        Task<TestRunSaveResult> SaveRunAsync(TestRunSnapshot snapshot, CancellationToken ct = default);

        /// <summary>历史轮次列表（收口时间倒序，默认最近 300 条）</summary>
        Task<List<TestRunSummary>> QueryRunsAsync(int maxCount = 300, CancellationToken ct = default);

        /// <summary>区间统计（历史页统计 tab 与统计 PDF 共用）：计数只算参与判定的项</summary>
        Task<RangeStatResult> QueryRangeStatsAsync(RangeStatQuery query, CancellationToken ct = default);
    }
}
