using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 测试报告导出服务：从入库数据生成 PDF，只读库不写库。
    /// 单轮报告与区间统计报告同存 <c>TestData\Reports\</c>，重复导出覆盖同名文件（产线补打场景）。
    /// </summary>
    public interface ITestReportService
    {
        /// <summary>导出单轮报告（页头 + 环比对照 + 按模块分节 + 重复性分析），文件名 = 报告编号.pdf，返回全路径</summary>
        /// <exception cref="InvalidOperationException">报告编号不存在等</exception>
        Task<string> ExportRunReportAsync(string reportNo, CancellationToken ct = default);

        /// <summary>加载某一轮的明细：与 PDF 同一份数据、同一套分组与显示口径，供历史页展示</summary>
        /// <exception cref="InvalidOperationException">报告编号不存在</exception>
        Task<TestRunDetail> LoadRunDetailAsync(string reportNo, CancellationToken ct = default);

        /// <summary>导出区间统计报告，文件名 = 区间统计_起-止[_工位][_方案].pdf；空区间也出一页明示无数据，不抛错</summary>
        Task<string> ExportRangeReportAsync(RangeStatQuery query, CancellationToken ct = default);
    }
}
