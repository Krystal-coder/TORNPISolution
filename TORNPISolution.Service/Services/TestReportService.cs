using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TORNPISolution.Data;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>一个通道（收缩压/舒张压/心率）的重复性统计量（折线图数据 + 统计卡）</summary>
    public sealed class ChannelStats
    {
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public double Target { get; set; }
        public double Tolerance { get; set; }
        public double? Average { get; set; }
        public double? Max { get; set; }
        public double? Min { get; set; }
        public double? Range { get; set; }
        /// <summary>均值对设定偏差（统计行 Error 列口径）</summary>
        public double? ErrorToTarget { get; set; }
        /// <summary>逐轮平均偏差（各轮|偏差|的算术平均，与"均值对设定偏差"双口径并列展示）</summary>
        public double? RoundAvgError { get; set; }
        public int RoundCount { get; set; }
        /// <summary>极差是否参与判定（BP-004 判定；BP-005 仅统计）</summary>
        public bool RangeJudged { get; set; }
    }

    /// <summary>BP-004 一个压力组的重复性分析面板数据</summary>
    public sealed class Bp004Group
    {
        public int Systolic { get; set; }
        public int Diastolic { get; set; }
        public List<string> XLabels { get; set; } = new();
        public List<RepeatSeries> Series { get; set; } = new();
        public int RoundPass { get; set; }
        public int RoundFail { get; set; }
        public List<ChannelStats> Channels { get; set; } = new();
    }

    /// <summary>BP-005 一个心率点的重复性分析面板数据</summary>
    public sealed class Bp005Point
    {
        public int HeartRate { get; set; }
        public double Tolerance { get; set; }
        public List<string> XLabels { get; set; } = new();
        public List<RepeatSeries> Series { get; set; } = new();
        public int RoundPass { get; set; }
        public int RoundFail { get; set; }
        public ChannelStats Stats { get; set; } = new();
    }

    /// <summary>环比对照一行（本轮 vs 同方案上一轮）</summary>
    public sealed class CompareRow
    {
        public string Label { get; set; } = string.Empty;
        public string CurrentText { get; set; } = string.Empty;
        public string? PrevText { get; set; }
        public string ChangeText { get; set; } = "—";
        public string ChangeColor { get; set; } = "#666666";
    }

    /// <summary>报表里的一个模块章节</summary>
    public sealed class ModuleSection
    {
        public string ModuleId { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string Verdict { get; set; } = "未完成";
        /// <summary>大项用时（秒）：本轮该模块派发到收尾的实测墙钟；null = 该轮未采集</summary>
        public double? DurationSeconds { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int NotTestedCount { get; set; }
        public List<TestItemResultEntity> Items { get; set; } = new();
        public List<Bp004Group> Bp004Groups { get; set; } = new();
        public List<Bp005Point> Bp005Points { get; set; } = new();
    }

    /// <summary>单轮报告模型（页头 + 环比 + 模块章节）</summary>
    public sealed class ReportModel
    {
        public TestRunEntity Run { get; set; } = null!;
        public List<TestItemResultEntity> Items { get; set; } = new();
        public List<ModuleSection> Modules { get; set; } = new();
        public TestRunEntity? PreviousRun { get; set; }
        public List<CompareRow> CompareRows { get; set; } = new();
    }

    /// <summary>
    /// 测试报告导出服务（QuestPDF + SVG 矢量图表）：单轮报告 = 页头信息 + 环比对照 + 按模块分节明细，
    /// A06/A21 章节内嵌重复性分析；区间统计另出一份文档。数据查询收口在 <see cref="ITestDataService"/>，本服务只画不查。
    /// </summary>
    public class TestReportService : ITestReportService
    {
        // QuestPDF 社区版（MIT 免费，商用无需付费）：生成 PDF 前必须声明一次，否则抛许可异常
        static TestReportService() => QuestPDF.Settings.License = LicenseType.Community;

        // 静态：取数段（BuildModel/Extract*）为静态方法，正则失配这类静默降级只能在里面留话
        private static readonly ILogService _log = new LogService(nameof(TestReportService));

        // 区间统计的数据查询收口在 TestDataService（统计口径与入库同源）；报表服务只负责画
        private readonly ITestDataService _dataService;

        public TestReportService(ITestDataService dataService) => _dataService = dataService;

        // 动态项名 → 图表数据的桥（项名格式见 SpecItems：配置改点/轮只改行数，正则按同一格式归组）
        private static readonly System.Text.RegularExpressions.Regex Bp004RoundRe =
            new(@"^BP-004精度测试 (\d+)/(\d+) 第(\d+)轮$", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex Bp004RepeatRe =
            new(@"^BP-004重复性测试 (\d+)/(\d+)$", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex Bp005RoundRe =
            new(@"^BP-005心率精度测试 (\d+) 第(\d+)轮$", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex Bp005AvgRe =
            new(@"^BP-005心率精度测试 (\d+) 平均值$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private const double Bp004Tolerance = 2.0;   // mmHg，与执行端 BpTolerance 一致

        /// <inheritdoc/>
        public async Task<string> ExportRunReportAsync(string reportNo, CancellationToken ct = default)
        {
            TestDataPaths.EnsureDirectories();
            await _dataService.PrepareStorageAsync(ct);   // 只等预热：导出是读路径，建表收口在 TestDataService

            await using var db = new TornpiDbContext();
            var run = await LoadRunAsync(db, reportNo, ct);
            var items = run.Items.OrderBy(i => i.Sequence).ThenBy(i => i.SortOrder).ToList();

            // 环比：同工位 + 同方案 + 更早完成的最近一轮（没有就跳过对照块，不报错）
            var prevRun = await db.TestRuns
                .Where(r => r.Station == run.Station && r.PlanName == run.PlanName && r.FinishedAt < run.FinishedAt)
                .OrderByDescending(r => r.FinishedAt)
                .FirstOrDefaultAsync(ct);
            var prevItems = new List<TestItemResultEntity>();
            if (prevRun != null)
            {
                // 必须带上 Points：环比的极差/偏差统计量全在子表行里，漏了 Include 会让这四行恒为"—"
                prevItems = await db.TestItemResults
                    .Where(i => i.RunId == prevRun.Id)
                    .Include(i => i.Points)
                    .OrderBy(i => i.Sequence).ThenBy(i => i.SortOrder)
                    .ToListAsync(ct);
            }

            var model = BuildModel(run, items, prevRun, prevItems);
            var path = Path.Combine(TestDataPaths.ReportsDirectory, $"{run.ReportNo}.pdf");
            // 排版与写盘整段是同步 CPU 活（QuestPDF 这一版没有异步生成口），不挪出去导出期间整个窗口冻住
            await Task.Run(() => BuildDocument(model).GeneratePdf(path), ct);
            _log.Info($"测试报告已导出：{path}");
            return path;
        }

        /// <inheritdoc/>
        public async Task<TestRunDetail> LoadRunDetailAsync(string reportNo, CancellationToken ct = default)
        {
            await _dataService.PrepareStorageAsync(ct);   // 同导出：读路径只等预热
            await using var db = new TornpiDbContext();
            var run = await LoadRunAsync(db, reportNo, ct);
            var items = run.Items.OrderBy(i => i.Sequence).ThenBy(i => i.SortOrder).ToList();
            return BuildDetail(run, items);
        }

        /// <inheritdoc/>
        public async Task<string> ExportRangeReportAsync(RangeStatQuery query, CancellationToken ct = default)
        {
            TestDataPaths.EnsureDirectories();

            var stat = await _dataService.QueryRangeStatsAsync(query, ct);
            // 文件名带区间 + 筛选条件：同一区间"全部"与"某方案"是两份不同的统计，不能互相覆盖
            var name = $"区间统计_{query.From:yyyyMMdd}-{query.To:yyyyMMdd}";
            var filters = new[] { query.Station, query.PlanName }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(SanitizeFileName)
                .ToArray();
            if (filters.Length > 0) name += "_" + string.Join("_", filters);
            var path = Path.Combine(TestDataPaths.ReportsDirectory, name + ".pdf");
            await Task.Run(() => BuildRangeDocument(stat).GeneratePdf(path), ct);
            _log.Info($"区间统计报告已导出：{path}（{stat.RunCount} 轮）");
            return path;
        }

        /// <summary>文件名净化：去掉路径非法字符并限长（筛选值来自手输，不能原样进文件名）</summary>
        private static string SanitizeFileName(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(s.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            return cleaned.Length > 20 ? cleaned[..20] : cleaned;
        }

        /// <summary>按编号捞一轮（含测试项与逐点）；编号不存在直接抛，由 UI 提示</summary>
        private static async Task<TestRunEntity> LoadRunAsync(TornpiDbContext db, string reportNo, CancellationToken ct)
            => await db.TestRuns
                .Include(r => r.Items).ThenInclude(i => i.Points)
                .FirstOrDefaultAsync(r => r.ReportNo == reportNo, ct)
                ?? throw new InvalidOperationException($"找不到报告编号 {reportNo} 的测试记录");

        // ==================== 数据提取 ====================

        private static ReportModel BuildModel(TestRunEntity run, List<TestItemResultEntity> items,
            TestRunEntity? prevRun, List<TestItemResultEntity> prevItems)
        {
            var model = new ReportModel { Run = run, Items = items, PreviousRun = prevRun };

            // 模块章节：按执行序号归组（方案里可能只有一个模块，也可能多个——章节数随方案变）
            var timings = ParseModuleTimings(run.ModuleTimingsJson);
            model.Modules = items
                .GroupBy(i => (i.Sequence, i.ModuleId, i.ModuleName))
                .OrderBy(g => g.Key.Sequence)
                .Select(g =>
                {
                    var section = new ModuleSection
                    {
                        ModuleId = g.Key.ModuleId,
                        ModuleName = g.Key.ModuleName,
                        DurationSeconds = timings.TryGetValue(g.Key.Sequence, out var sec) ? sec : null,
                        Items = g.ToList(),
                    };
                    var judged = section.Items.Where(i => !i.DisplayOnly && !i.NotJudged).ToList();
                    section.FailCount = judged.Count(i => i.Result == "Fail");
                    section.PassCount = judged.Count(i => i.Result == "Pass");
                    section.NotTestedCount = judged.Count - section.PassCount - section.FailCount;
                    section.Verdict = section.FailCount > 0 ? "FAIL"
                        : judged.Count > 0 && section.PassCount == judged.Count ? "PASS" : "未完成";
                    return section;
                }).ToList();

            // 重复性面板挂到 A06/A21 章节（按项名正则提取，与模块解耦：项名格式全局唯一）
            var bp004 = ExtractBp004Groups(items);
            var bp005 = ExtractBp005Points(items);
            foreach (var m in model.Modules)
            {
                if (m.ModuleId == TestModuleIds.BpDynamicAccuracyTest) m.Bp004Groups = bp004;
                if (m.ModuleId == TestModuleIds.BpHeartRateAccuracyTest) m.Bp005Points = bp005;
                // 面板是靠项名正则归组的：SpecItems 的命名模板一改就全不匹配，图会一声不响地消失，这里必须留话
                if (m.ModuleId == TestModuleIds.BpDynamicAccuracyTest && bp004.Count == 0)
                    _log.Warn($"编号 {run.ReportNo}：A06 章节 {m.Items.Count} 个测试项未提取到重复性面板" +
                              "（项名与报表正则不匹配，或本轮压力组/轮数没配置）");
                if (m.ModuleId == TestModuleIds.BpHeartRateAccuracyTest && bp005.Count == 0)
                    _log.Warn($"编号 {run.ReportNo}：A21 章节 {m.Items.Count} 个测试项未提取到重复性面板" +
                              "（项名与报表正则不匹配，或本轮心率点/轮数没配置）");
            }

            if (prevRun != null) model.CompareRows = BuildCompareRows(run, items, prevRun, prevItems);
            return model;
        }

        /// <summary>解析模块用时快照（执行序号 → 秒）：老轮次没这一列，空表即报表显示"—"</summary>
        private static Dictionary<int, double> ParseModuleTimings(string? json)
        {
            if (string.IsNullOrEmpty(json)) return new Dictionary<int, double>();
            try
            {
                return JsonSerializer.Deserialize<List<TestModuleTiming>>(json)?.ToDictionary(t => t.Sequence, t => t.Seconds)
                       ?? new Dictionary<int, double>();
            }
            catch (Exception ex)
            {
                _log.Warn($"模块用时快照解析失败（章节头用时显示—）：{ex.Message}");
                return new Dictionary<int, double>();
            }
        }

        /// <summary>历史页明细：与 PDF 同一份数据、同一套分组与口径（规格快照/测试值/子表行），映射成界面 DTO</summary>
        private static TestRunDetail BuildDetail(TestRunEntity run, List<TestItemResultEntity> items)
        {
            // 复用 BuildModel 的模块分组与判定（不传上一轮，明细页不看环比）
            var model = BuildModel(run, items, null, new List<TestItemResultEntity>());
            var detail = new TestRunDetail
            {
                ReportNo = run.ReportNo,
                Station = run.Station,
                PlanName = run.PlanName,
                OverallResult = run.OverallResult,
                ProductMAC = run.ProductMAC,
                StartedAt = run.StartedAt,
                FinishedAt = run.FinishedAt,
                PassCount = run.PassCount,
                FailCount = run.FailCount,
                NotTestedCount = run.NotTestedCount,
            };
            foreach (var m in model.Modules)
            {
                var section = new ModuleDetail
                {
                    ModuleId = m.ModuleId,
                    ModuleName = m.ModuleName,
                    Verdict = m.Verdict,
                    PassCount = m.PassCount,
                    FailCount = m.FailCount,
                    NotTestedCount = m.NotTestedCount,
                };
                foreach (var item in m.Items)
                {
                    var row = new ItemDetail
                    {
                        ItemName = item.ItemName,
                        SpecText = SpecText(item),
                        ValueText = ValueText(item),
                        DurationText = item.DurationSeconds is double dur ? $"{dur:0.0}s" : "—",
                        ResultText = item.DisplayOnly ? "统计"
                            : item.Result == "Pass" ? "PASS"
                            : item.Result == "Fail" ? "FAIL" : "未测",
                    };
                    foreach (var p in item.Points.Where(p => p.Schema != "RepeatStats").OrderBy(p => p.SortOrder))
                    {
                        var text = PointRowText(p);
                        if (text != null) row.SubRows.Add(text);
                    }
                    section.Items.Add(row);
                }
                detail.Modules.Add(section);
            }
            return detail;
        }

        /// <summary>BP-004：按压力组归组——轮次项取逐轮回读（折线），重复性项取数值统计行（统计卡）</summary>
        private static List<Bp004Group> ExtractBp004Groups(List<TestItemResultEntity> items)
        {
            var groups = new Dictionary<(int Sys, int Dia), Bp004Group>();
            foreach (var item in items)
            {
                var m = Bp004RoundRe.Match(item.ItemName);
                if (m.Success)
                {
                    var key = (Sys: int.Parse(m.Groups[1].Value), Dia: int.Parse(m.Groups[2].Value));
                    var round = int.Parse(m.Groups[3].Value);
                    if (!groups.TryGetValue(key, out var g))
                    {
                        g = new Bp004Group { Systolic = key.Sys, Diastolic = key.Dia };
                        groups[key] = g;
                    }
                    while (g.XLabels.Count < round) g.XLabels.Add(string.Empty);
                    g.XLabels[round - 1] = RoundLabel(round);
                    foreach (var ch in new[] { "收缩压", "舒张压" })
                    {
                        var p = item.Points.FirstOrDefault(x => x.Schema == "Channel" && x.Name == ch);
                        var series = GetOrAddSeries(g.Series, ch, ch == "收缩压" ? "#1E88E5" : "#FB8C00", "mmHg");
                        while (series.Values.Count < round) series.Values.Add(null);
                        while (series.Pass.Count < round) series.Pass.Add(false);
                        series.Values[round - 1] = p?.ProductValue;
                        series.Pass[round - 1] = p?.Pass ?? false;
                    }
                    if (item.Result == "Pass") g.RoundPass++;
                    else if (item.Result == "Fail") g.RoundFail++;
                }
                else
                {
                    var rm = Bp004RepeatRe.Match(item.ItemName);
                    if (!rm.Success) continue;
                    var key = (Sys: int.Parse(rm.Groups[1].Value), Dia: int.Parse(rm.Groups[2].Value));
                    if (!groups.TryGetValue(key, out var g))
                    {
                        g = new Bp004Group { Systolic = key.Sys, Diastolic = key.Dia };
                        groups[key] = g;
                    }
                    // 数值统计行（RepeatStats：执行端补的，直接读列不解析文本）
                    foreach (var st in item.Points.Where(x => x.Schema == "RepeatStats"))
                    {
                        var chName = st.Name.Replace("统计", string.Empty);   // 收缩压统计 → 收缩压
                        var channel = chName == "收缩压"
                            ? GetOrAddChannel(g.Channels, "收缩压")
                            : GetOrAddChannel(g.Channels, "舒张压");
                        channel.Unit = st.Unit;
                        channel.Target = st.TargetPressure;
                        channel.Average = st.Average;
                        channel.Max = st.Max;
                        channel.Min = st.Min;
                        channel.Range = st.Range;
                        channel.ErrorToTarget = st.Error;
                        channel.RangeJudged = true;
                    }
                }
            }

            foreach (var g in groups.Values)
            {
                // 执行端没出统计行（有效轮<2）时补一个空壳，面板照样能画折线。设定值按通道各取自己的：
                // 舒张压那条壳填收缩压值就是假数据，统计卡与折线的设定线迟早对不上
                foreach (var (name, target) in new[] { ("收缩压", g.Systolic), ("舒张压", g.Diastolic) })
                {
                    var ch = g.Channels.FirstOrDefault(c => c.Name == name);
                    if (ch == null)
                        g.Channels.Add(new ChannelStats
                        {
                            Name = name, Unit = "mmHg", Tolerance = Bp004Tolerance,
                            Target = target, RoundCount = g.XLabels.Count, RangeJudged = true,
                        });
                }
                // 逐轮平均偏差：从轮次项的 Channel 行 Error 列算（双口径之一，与"均值对设定偏差"并列）；
                // 必须排在补壳之后——有效轮<2 的组没有统计行，先遍历空 Channels 会让这组只剩"—"，
                // 而那几轮的偏差其实已经在库里
                foreach (var ch in g.Channels)
                {
                    var errs = items
                        .Where(i => Bp004RoundRe.Match(i.ItemName) is { Success: true } rm
                                    && int.Parse(rm.Groups[1].Value) == g.Systolic
                                    && int.Parse(rm.Groups[2].Value) == g.Diastolic)
                        .SelectMany(i => i.Points.Where(p => p.Schema == "Channel" && p.Name == ch.Name && p.Error.HasValue)
                                                .Select(p => p.Error!.Value))
                        .ToList();
                    ch.RoundAvgError = errs.Count > 0 ? Math.Round(errs.Average(), 2) : null;
                    ch.RoundCount = g.XLabels.Count;
                    ch.Tolerance = Bp004Tolerance;
                }
                g.Channels = g.Channels.OrderBy(c => c.Name == "收缩压" ? 0 : 1).ToList();
            }
            return groups.Values.OrderBy(g => g.Systolic).ThenBy(g => g.Diastolic).ToList();
        }

        /// <summary>BP-005：按心率点归组——轮次项取逐轮心率（折线），平均值项取统计行（统计卡）</summary>
        private static List<Bp005Point> ExtractBp005Points(List<TestItemResultEntity> items)
        {
            var points = new Dictionary<int, Bp005Point>();
            foreach (var item in items)
            {
                var m = Bp005RoundRe.Match(item.ItemName);
                if (m.Success)
                {
                    var hr = int.Parse(m.Groups[1].Value);
                    var round = int.Parse(m.Groups[2].Value);
                    if (!points.TryGetValue(hr, out var p))
                    {
                        p = new Bp005Point { HeartRate = hr, Tolerance = Math.Max(5, hr * 0.05) };
                        points[hr] = p;
                    }
                    while (p.XLabels.Count < round) p.XLabels.Add(string.Empty);
                    p.XLabels[round - 1] = RoundLabel(round);
                    var point = item.Points.FirstOrDefault(x => x.Schema == "Channel" && x.Name == "心率");
                    var series = GetOrAddSeries(p.Series, "心率", "#8E24AA", "BPM");
                    while (series.Values.Count < round) series.Values.Add(null);
                    while (series.Pass.Count < round) series.Pass.Add(false);
                    series.Values[round - 1] = point?.ProductValue;
                    series.Pass[round - 1] = point?.Pass ?? false;
                    if (item.Result == "Pass") p.RoundPass++;
                    else if (item.Result == "Fail") p.RoundFail++;
                }
                else
                {
                    var am = Bp005AvgRe.Match(item.ItemName);
                    if (!am.Success) continue;
                    var hr = int.Parse(am.Groups[1].Value);
                    if (!points.TryGetValue(hr, out var p))
                    {
                        p = new Bp005Point { HeartRate = hr, Tolerance = Math.Max(5, hr * 0.05) };
                        points[hr] = p;
                    }
                    var st = item.Points.FirstOrDefault(x => x.Schema == "Repeat");
                    if (st != null)
                        p.Stats = new ChannelStats
                        {
                            Name = "心率", Unit = st.Unit, Target = st.TargetPressure,
                            Tolerance = p.Tolerance, Average = st.Average, Max = st.Max, Min = st.Min,
                            Range = st.Range, ErrorToTarget = st.Error, RangeJudged = false,
                        };
                }
            }

            foreach (var p in points.Values)
            {
                if (p.Stats.Name.Length == 0)
                    p.Stats = new ChannelStats
                    {
                        Name = "心率", Unit = "BPM", Target = p.HeartRate,
                        Tolerance = p.Tolerance, RangeJudged = false,
                    };
                var errs = items
                    .Where(i => Bp005RoundRe.Match(i.ItemName) is { Success: true } rm && int.Parse(rm.Groups[1].Value) == p.HeartRate)
                    .SelectMany(i => i.Points.Where(x => x.Schema == "Channel" && x.Name == "心率" && x.Error.HasValue)
                                            .Select(x => x.Error!.Value))
                    .ToList();
                p.Stats.RoundAvgError = errs.Count > 0 ? Math.Round(errs.Average(), 2) : null;
                p.Stats.RoundCount = p.XLabels.Count;
            }
            return points.Values.OrderBy(p => p.HeartRate).ToList();
        }

        // 轴标签只写轮次：超限轮次在图上已是红点，再打记号反而引入雅黑缺字形（豆腐块）与折行
        private static string RoundLabel(int round) => $"第{round}轮";

        private static RepeatSeries GetOrAddSeries(List<RepeatSeries> list, string name, string color, string unit)
        {
            var s = list.FirstOrDefault(x => x.Name == name);
            if (s == null)
            {
                s = new RepeatSeries { Name = name, Color = color, Unit = unit };
                list.Add(s);
            }
            return s;
        }

        private static ChannelStats GetOrAddChannel(List<ChannelStats> list, string name)
        {
            var c = list.FirstOrDefault(x => x.Name == name);
            if (c == null)
            {
                c = new ChannelStats { Name = name };
                list.Add(c);
            }
            return c;
        }

        // ==================== 环比 ====================

        private static List<CompareRow> BuildCompareRows(TestRunEntity run, List<TestItemResultEntity> items,
            TestRunEntity prev, List<TestItemResultEntity> prevItems)
        {
            var (vText, vColor) = CompareVerdictChange(run.OverallResult, prev.OverallResult);
            var rows = new List<CompareRow>
            {
                new()
                {
                    Label = "总判定",
                    CurrentText = VerdictText(run.OverallResult),
                    PrevText = VerdictText(prev.OverallResult),
                    ChangeText = vText,
                    ChangeColor = vColor,
                },
                NumericRow("通过项（个）", run.PassCount, prev.PassCount, higherBetter: true),
                NumericRow("失败项（个）", run.FailCount, prev.FailCount, higherBetter: false),
                NumericRow("未测项（个）", run.NotTestedCount, prev.NotTestedCount, higherBetter: false),
            };

            // 重复性关键指标：只在当前轮有时才列（拿本轮数据与上轮同口径比）
            var cur004 = ExtractBp004Groups(items);
            var pre004 = ExtractBp004Groups(prevItems);
            var cur005 = ExtractBp005Points(items);
            var pre005 = ExtractBp005Points(prevItems);

            AddMetric(rows, "BP-004 最大极差（mmHg）",
                MaxOf(cur004.SelectMany(g => g.Channels).Select(c => c.Range)),
                MaxOf(pre004.SelectMany(g => g.Channels).Select(c => c.Range)));
            AddMetric(rows, "BP-004 最大均值偏差（mmHg）",
                MaxOf(cur004.SelectMany(g => g.Channels).Select(c => c.ErrorToTarget)),
                MaxOf(pre004.SelectMany(g => g.Channels).Select(c => c.ErrorToTarget)));
            AddMetric(rows, "BP-005 最大极差（BPM）",
                MaxOf(cur005.Select(p => (double?)p.Stats.Range)),
                MaxOf(pre005.Select(p => (double?)p.Stats.Range)));
            AddMetric(rows, "BP-005 最大均值偏差（BPM）",
                MaxOf(cur005.Select(p => (double?)p.Stats.ErrorToTarget)),
                MaxOf(pre005.Select(p => (double?)p.Stats.ErrorToTarget)));
            return rows;

            static double? MaxOf(IEnumerable<double?> values)
            {
                var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
                return list.Count > 0 ? list.Max() : null;
            }
        }

        private static void AddMetric(List<CompareRow> rows, string label, double? cur, double? prev)
        {
            if (!cur.HasValue) return;   // 本轮没这项指标就不列（上轮有、本轮没有也不是对比重点）
            var row = new CompareRow
            {
                Label = label,
                CurrentText = cur.Value.ToString("0.0", CultureInfo.InvariantCulture),
                PrevText = prev.HasValue ? prev.Value.ToString("0.0", CultureInfo.InvariantCulture) : "—",
            };
            if (prev.HasValue)
            {
                var diff = cur.Value - prev.Value;
                (row.ChangeText, row.ChangeColor) = Math.Abs(diff) < 0.05 ? ("持平", "#666666")
                    : diff < 0 ? ($"↓ {diff:0.0} 变好", "#2E7D32")
                    : ($"↑ +{diff:0.0} 变差", "#C62828");
            }
            rows.Add(row);
        }

        private static CompareRow NumericRow(string label, int cur, int prev, bool higherBetter)
        {
            var diff = cur - prev;
            var better = (diff > 0) == higherBetter;
            var arrow = diff > 0 ? $"↑ +{diff}" : $"↓ {diff}";
            var (text, color) = diff == 0 ? ("持平", "#666666")
                : ($"{arrow} {(better ? "变好" : "变差")}", better ? "#2E7D32" : "#C62828");
            return new CompareRow { Label = label, CurrentText = cur.ToString(), PrevText = prev.ToString(), ChangeText = text, ChangeColor = color };
        }

        private static string VerdictText(string result) => result switch
        {
            "Pass" or "PASS" => "PASS",
            "Fail" or "FAIL" => "FAIL",
            _ => "未完成",
        };

        private static (string text, string color) CompareVerdictChange(string cur, string prev)
        {
            if (cur == prev) return ("持平", "#666666");
            if (cur == "Fail" && prev == "Pass") return ("变差", "#C62828");
            if (cur == "Pass" && prev == "Fail") return ("变好", "#2E7D32");
            return ("—", "#666666");
        }

        // ==================== 文档组装 ====================

        private static Action<IContainer> ComposeHeader(ReportModel model)
        {
            var run = model.Run;
            return container =>
            {
                container.Column(col =>
                {
                    // 标题行：左标题 + 右判定徽章
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            PutText(c.Item(), "整机测试报告", 17f, "#212121", bold: true);
                            PutText(c.Item(), $"报告编号 {run.ReportNo}", 9f, "#757575");
                        });
                        var chipCell = row.ConstantItem(150).AlignRight().AlignMiddle();
                        VerdictChip(chipCell, run.OverallResult, big: true);
                    });

                    // 信息表：工位/方案/MAC/起止时间/计数（四列两行，无框）
                    col.Item().PaddingTop(6).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1);
                            c.RelativeColumn(1.2f);
                            c.RelativeColumn(1.1f);
                            c.RelativeColumn(1.3f);
                        });
                        void Cell2(string label, string value)
                        {
                            table.Cell().Padding(2).Column(c =>
                            {
                                PutText(c.Item(), label, 7.5f, "#9E9E9E");
                                PutText(c.Item(), value, 9f, "#37474F");
                            });
                        }
                        Cell2("工位", run.Station);
                        Cell2("测试方案", run.PlanName);
                        Cell2("产品 MAC", string.IsNullOrEmpty(run.ProductMAC) ? "—" : run.ProductMAC);
                        Cell2("起止时间", $"{run.StartedAt:MM-dd HH:mm:ss} ~ {run.FinishedAt:HH:mm:ss}");
                        Cell2("总用时", ElapsedText(run.StartedAt, run.FinishedAt));
                        Cell2("量测时间", run.MeasureSeconds is int s ? $"每点 {s}s" : "—");
                        Cell2("通过项", run.PassCount.ToString());
                        Cell2("失败项", run.FailCount.ToString());
                        Cell2("未测项", run.NotTestedCount.ToString());
                        Cell2("入库时间", run.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                    });

                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#E0E0E0");
                });
            };
        }

        private static Action<IContainer> ComposeCompare(ReportModel model)
        {
            var prev = model.PreviousRun!;
            return container =>
            {
                container.Column(col =>
                {
                    col.Item().Background("#F5F7FA").Padding(6).Text(text =>
                    {
                        text.Span("与上一轮对照").Bold().FontSize(10f).FontColor("#37474F");
                        text.Span($"（{prev.ReportNo} · {prev.FinishedAt:MM-dd HH:mm} · {prev.PlanName}）")
                            .FontSize(8f).FontColor("#9E9E9E");
                    });

                    col.Item().PaddingTop(2).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2.2f);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1.2f);
                        });
                        table.Header(h =>
                        {
                            HeadCell(h, "指标");
                            HeadCell(h, "本轮", right: true);
                            HeadCell(h, "上一轮", right: true);
                            HeadCell(h, "变化", right: true);
                        });
                        foreach (var r in model.CompareRows)
                        {
                            PutText(NewCell(table), r.Label, 8.5f);
                            PutText(NewCell(table, right: true), r.CurrentText, 8.5f, "#37474F", bold: true);
                            PutText(NewCell(table, right: true), r.PrevText ?? "—", 8.5f, "#757575");
                            PutText(NewCell(table, right: true), r.ChangeText, 8.5f, r.ChangeColor);
                        }
                    });
                });
            };
        }

        private static Action<IContainer> ComposeModule(ModuleSection module, int index)
        {
            return container =>
            {
                container.Column(col =>
                {
                    // 章节头：序号 + 模块名 + 判定徽章 + 大项用时 + 计数
                    col.Item().PaddingTop(10).PaddingBottom(4).Row(row =>
                    {
                        row.Spacing(4);
                        PutText(row.ConstantItem(24).AlignMiddle(), $"#{index + 1}", 11f, "#90A4AE", bold: true);
                        PutText(row.RelativeItem().AlignMiddle(), module.ModuleName, 11f, "#263238", bold: true);
                        row.ConstantItem(250).AlignRight().AlignMiddle().Row(r =>
                        {
                            PutText(r.AutoItem().PaddingRight(8).AlignMiddle(), $"用时 {DurationText(module.DurationSeconds)}",
                                8f, "#90A4AE");
                            var countsCell = r.AutoItem().PaddingRight(8).AlignMiddle();
                            PutText(countsCell, $"通过 {module.PassCount} · 失败 {module.FailCount} · 未测 {module.NotTestedCount}",
                                8f, "#90A4AE");
                            VerdictChip(r.AutoItem(), module.Verdict);
                        });
                    });
                    col.Item().LineHorizontal(1).LineColor("#CFD8DC");

                    // 测试项表
                    col.Item().PaddingTop(2).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(26);
                            c.RelativeColumn(3f);
                            c.RelativeColumn(1.5f);
                            c.RelativeColumn(2.1f);
                            c.ConstantColumn(42);
                            c.ConstantColumn(44);
                        });
                        table.Header(h =>
                        {
                            HeadCell(h, "序号");
                            HeadCell(h, "测试项");
                            HeadCell(h, "规格（本次快照）");
                            HeadCell(h, "测试值");
                            HeadCell(h, "耗时(s)", right: true);
                            HeadCell(h, "结果", right: true);
                        });

                        var no = 0;
                        foreach (var item in module.Items)
                        {
                            no++;
                            PutText(NewCell(table, middle: true), no.ToString(), 8.5f, "#90A4AE");
                            PutText(NewCell(table, middle: true), item.ItemName, 8.5f);
                            PutText(NewCell(table, middle: true), SpecText(item), 8f, "#757575");
                            PutText(NewCell(table, middle: true), ValueText(item), 8.5f);
                            PutText(NewCell(table, right: true, middle: true),
                                item.DurationSeconds is double dur ? $"{dur:0.0}" : "—", 8f, "#757575");
                            ItemVerdictChip(table.Cell(), item);

                            // 子表行：跨列缩进展示（RepeatStats 数值行只进图表，不在这里重复）
                            foreach (var p in item.Points.Where(p => p.Schema != "RepeatStats").OrderBy(p => p.SortOrder))
                            {
                                var text = PointRowText(p);
                                if (text == null) continue;
                                PutText(NewCell(table, sub: true, span: 6), text, 7.5f, "#555555");
                            }
                        }
                    });

                    // 重复性分析面板（A06 / A21）
                    foreach (var g in module.Bp004Groups)
                        col.Item().PaddingTop(8).Element(ComposeBp004Panel(g));
                    foreach (var p in module.Bp005Points)
                        col.Item().PaddingTop(8).Element(ComposeBp005Panel(p));
                });
            };
        }

        private static Action<IContainer> ComposeBp004Panel(Bp004Group g)
        {
            var model = new RepeatChartModel
            {
                XLabels = g.XLabels,
                Unit = "mmHg",
                // 设定值按通道各给（与执行端 BpChannelPoint 的 target 同源：收缩压看 g.Systolic、舒张压看 g.Diastolic）
                Series = new List<RepeatSeries>
                {
                    new() { Name = "收缩压", Color = "#1E88E5", Unit = "mmHg",
                        Target = g.Systolic, Tolerance = Bp004Tolerance,
                        Values = SeriesValues(g.Series, "收缩压", g.XLabels.Count),
                        Pass = SeriesPass(g.Series, "收缩压", g.XLabels.Count) },
                    new() { Name = "舒张压", Color = "#FB8C00", Unit = "mmHg",
                        Target = g.Diastolic, Tolerance = Bp004Tolerance,
                        Values = SeriesValues(g.Series, "舒张压", g.XLabels.Count),
                        Pass = SeriesPass(g.Series, "舒张压", g.XLabels.Count) },
                },
            };
            return container =>
            {
                container.Column(col =>
                {
                    col.Item().Background("#ECEFF1").Padding(4).Text(text =>
                    {
                        text.Span($"重复性分析 · {g.Systolic}/{g.Diastolic} mmHg").Bold().FontSize(9.5f).FontColor("#37474F");
                        text.Span($"　{g.XLabels.Count} 轮 · 判定：极差 ≤ ±{Bp004Tolerance:0.#} mmHg（收缩/舒张取大者）")
                            .FontSize(8f).FontColor("#78909C");
                    });
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.Spacing(8);
                        row.ConstantItem(302).Column(c =>
                        {
                            c.Item().Svg(ReportCharts.BuildRepeatLineChart(model));
                            PutText(c.Item(), "绿带=容差 ±2 mmHg，虚线=设定值，红点=超限轮次，断线=该轮无有效回读",
                                7f, "#9E9E9E");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().AlignCenter().Svg(ReportCharts.BuildVerdictDoughnut(g.RoundPass, g.RoundFail));
                            c.Item().PaddingTop(2).Table(table =>
                            {
                                table.ColumnsDefinition(cd =>
                                {
                                    cd.ConstantColumn(76);
                                    cd.RelativeColumn(1);
                                });
                                foreach (var ch in g.Channels)
                                {
                                    PutText(NewCell(table, sub: true, span: 2).PaddingTop(3),
                                        $"{ch.Name}（{ch.Unit}）", 8f, "#455A64", bold: true);
                                    StatRow(table, "均值", N(ch.Average));
                                    StatRow(table, "极大 / 极小", $"{N(ch.Max)} / {N(ch.Min)}");
                                    StatRow(table, "极差", N(ch.Range));
                                    StatRow(table, "均值对设定偏差", N(ch.ErrorToTarget));
                                    StatRow(table, "逐轮平均偏差", N(ch.RoundAvgError));
                                    var ok = ch.Range.HasValue && ch.Range.Value <= Bp004Tolerance + 1e-9;
                                    PutText(NewCell(table, sub: true), "判定", 8f, "#757575");
                                    // 没有统计行（有效轮 <2）就不能说"超差"：零次测量不出 NG 结论，与 BP-005 分支同口径
                                    PutText(NewCell(table, sub: true, right: true),
                                        ch.Range.HasValue ? (ok ? "合格" : "超差") : "—",
                                        8f, !ch.Range.HasValue ? "#9E9E9E" : ok ? "#2E7D32" : "#C62828", bold: true);
                                }
                            });
                        });
                    });
                });
            };
        }

        private static Action<IContainer> ComposeBp005Panel(Bp005Point p)
        {
            var model = new RepeatChartModel
            {
                XLabels = p.XLabels,
                Unit = "BPM",
                Series = new List<RepeatSeries>
                {
                    new() { Name = "心率", Color = "#8E24AA", Unit = "BPM",
                        Target = p.HeartRate, Tolerance = p.Tolerance,
                        Values = SeriesValues(p.Series, "心率", p.XLabels.Count),
                        Pass = SeriesPass(p.Series, "心率", p.XLabels.Count) },
                },
            };
            var ch = p.Stats;
            var ok = ch.Range.HasValue && ch.Range.Value <= p.Tolerance + 1e-9;
            return container =>
            {
                container.Column(col =>
                {
                    col.Item().Background("#ECEFF1").Padding(4).Text(text =>
                    {
                        text.Span($"重复性分析 · 心率 {p.HeartRate} BPM").Bold().FontSize(9.5f).FontColor("#37474F");
                        text.Span($"　{p.XLabels.Count} 轮 · 逐轮判定 ≤ ±{p.Tolerance:0.#} BPM，极差仅统计")
                            .FontSize(8f).FontColor("#78909C");
                    });
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.Spacing(8);
                        row.ConstantItem(302).Column(c =>
                        {
                            c.Item().Svg(ReportCharts.BuildRepeatLineChart(model));
                            PutText(c.Item(), "绿带=容差，虚线=设定值，红点=超限轮次，断线=该轮无有效回读",
                                7f, "#9E9E9E");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().AlignCenter().Svg(ReportCharts.BuildVerdictDoughnut(p.RoundPass, p.RoundFail));
                            c.Item().PaddingTop(2).Table(table =>
                            {
                                table.ColumnsDefinition(cd =>
                                {
                                    cd.ConstantColumn(76);
                                    cd.RelativeColumn(1);
                                });
                                // 补导旧轮次时这条统计行的 Unit 是空的，空单位就别留一对空括号
                                PutText(NewCell(table, sub: true, span: 2).PaddingTop(3),
                                    string.IsNullOrEmpty(ch.Unit) ? "心率" : $"心率（{ch.Unit}）", 8f, "#455A64", bold: true);
                                StatRow(table, "均值", N(ch.Average));
                                StatRow(table, "极大 / 极小", $"{N(ch.Max)} / {N(ch.Min)}");
                                StatRow(table, "极差", N(ch.Range));
                                StatRow(table, "均值对设定偏差", N(ch.ErrorToTarget));
                                StatRow(table, "逐轮平均偏差", N(ch.RoundAvgError));
                                PutText(NewCell(table, sub: true), "判定", 8f, "#757575");
                                PutText(NewCell(table, sub: true, right: true),
                                    ch.Range.HasValue ? (ok ? "极差合格（仅统计）" : "极差超差（仅统计）") : "—",
                                    8f, !ch.Range.HasValue ? "#9E9E9E" : ok ? "#2E7D32" : "#C62828", bold: true);
                            });
                        });
                    });
                });
            };
        }

        private static void StatRow(TableDescriptor table, string label, string value)
        {
            PutText(NewCell(table, sub: true), label, 8f, "#757575");
            PutText(NewCell(table, sub: true, right: true), value, 8f, "#37474F");
        }

        private static List<double?> SeriesValues(List<RepeatSeries> series, string name, int count)
        {
            var s = series.FirstOrDefault(x => x.Name == name);
            var result = new List<double?>();
            for (var i = 0; i < count; i++)
                result.Add(s != null && i < s.Values.Count ? s.Values[i] : null);
            return result;
        }

        private static List<bool> SeriesPass(List<RepeatSeries> series, string name, int count)
        {
            var s = series.FirstOrDefault(x => x.Name == name);
            var result = new List<bool>();
            for (var i = 0; i < count; i++)
                result.Add(s != null && i < s.Pass.Count && s.Pass[i]);
            return result;
        }

        private static IDocument BuildDocument(ReportModel model)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(x => x.FontSize(9f).FontColor("#333333"));
                    page.Header().Element(ComposeHeader(model));
                    page.Content().PaddingTop(6).Column(col =>
                    {
                        col.Spacing(10);
                        if (model.PreviousRun != null)
                            col.Item().Element(ComposeCompare(model));
                        for (var i = 0; i < model.Modules.Count; i++)
                            col.Item().Element(ComposeModule(model.Modules[i], i));
                    });
                    page.Footer().Height(22).AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(7.5f).FontColor("#9E9E9E"));
                        text.Span($"{model.Run.ReportNo} · {model.Run.PlanName} · 生成于 {DateTime.Now:yyyy-MM-dd HH:mm} · 第 ");
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                        text.Span(" 页");
                    });
                });
            });
        }

        // ==================== 区间统计文档（第 2 页） ====================

        private static IDocument BuildRangeDocument(RangeStatResult stat)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(x => x.FontSize(9f).FontColor("#333333"));
                    page.Header().Element(ComposeRangeHeader(stat));
                    page.Content().PaddingTop(6).Column(col =>
                    {
                        col.Spacing(10);
                        if (!stat.HasData)
                        {
                            // 空区间也出一页：统计导出是例行动作，不能因为没数据就导不出东西
                            PutText(col.Item().PaddingTop(48).AlignCenter(),
                                "区间内没有测试轮次——调整统计区间，或先跑几轮整机测试", 10f, "#9E9E9E");
                            return;
                        }
                        col.Item().Element(ComposeRangeSummary(stat));
                        col.Item().Element(ComposeModuleRateSection(stat));
                        col.Item().Element(ComposeNgSection(stat));
                        col.Item().Element(ComposeTrendSection(stat));
                    });
                    page.Footer().Height(22).AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(7.5f).FontColor("#9E9E9E"));
                        text.Span($"区间统计 {stat.RangeText} · 生成于 {DateTime.Now:yyyy-MM-dd HH:mm} · 第 ");
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                        text.Span(" 页");
                    });
                });
            });
        }

        private static Action<IContainer> ComposeRangeHeader(RangeStatResult stat)
        {
            return container =>
            {
                container.Column(col =>
                {
                    // 标题行：左标题 + 右通过率徽章
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            PutText(c.Item(), "整机测试报告 · 区间统计", 17f, "#212121", bold: true);
                            PutText(c.Item(), $"统计区间 {stat.RangeText}", 9f, "#757575");
                        });
                        // 槽宽要放得下徽章的 MinWidth + 内距
                        RateChip(row.ConstantItem(130).AlignRight().AlignMiddle(), stat.PassRate, stat.PassRateText);
                    });

                    col.Item().PaddingTop(6).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });
                        void Cell2(string label, string value)
                        {
                            table.Cell().Padding(2).Column(c =>
                            {
                                PutText(c.Item(), label, 7.5f, "#9E9E9E");
                                PutText(c.Item(), value, 9f, "#37474F");
                            });
                        }
                        Cell2("统计口径", stat.HasData ? $"{stat.RunCount} 轮" : "无数据");
                        Cell2("工位", string.IsNullOrEmpty(stat.Station) ? "全部" : stat.Station);
                        Cell2("测试方案", string.IsNullOrEmpty(stat.PlanName) ? "全部" : stat.PlanName);
                        Cell2("生成时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    });

                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#E0E0E0");
                });
            };
        }

        /// <summary>区间通过率徽章：≥95% 绿、≥80% 黄、其余红（产线管理常用三档）</summary>
        private static void RateChip(IContainer cell, double rate, string text)
        {
            var (bg, fg) = rate >= 0.95 ? ("#E8F5E9", "#2E7D32")
                : rate >= 0.80 ? ("#FFF8E1", "#F9A825")
                : ("#FFEBEE", "#C62828");
            var styled = cell.Background(bg).PaddingHorizontal(14).PaddingVertical(5).AlignCenter().MinWidth(90);
            PutText(styled, text, 15f, fg, bold: true);
        }

        /// <summary>汇总卡：一行五格（轮次/通过/失败/未测/通过率）</summary>
        private static Action<IContainer> ComposeRangeSummary(RangeStatResult stat)
        {
            return container =>
            {
                container.Row(row =>
                {
                    row.Spacing(6);
                    void Card(string label, string value, string color)
                    {
                        row.RelativeItem().Background("#F5F7FA").PaddingVertical(8).Column(c =>
                        {
                            PutText(c.Item().AlignCenter(), label, 8f, "#9E9E9E");
                            PutText(c.Item().AlignCenter(), value, 15f, color, bold: true);
                        });
                    }
                    Card("测试轮次", stat.RunCount.ToString(), "#37474F");
                    Card("通过项", stat.TotalPass.ToString(), "#2E7D32");
                    Card("失败项", stat.TotalFail.ToString(), "#C62828");
                    Card("未测项", stat.TotalNotTested.ToString(), "#90A4AE");
                    Card("整体通过率", stat.PassRateText, RateColor(stat.PassRate));
                });
            };
        }

        private static string RateColor(double rate)
            => rate >= 0.95 ? "#2E7D32" : rate >= 0.80 ? "#F9A825" : "#C62828";

        /// <summary>章节标题条：左标题右说明（QuestPDF 单元格是单子容器，两段文本套 Row）</summary>
        private static void SectionTitle(IContainer cell, string title, string subtitle)
        {
            cell.Background("#ECEFF1").PaddingHorizontal(6).PaddingVertical(3).Row(row =>
            {
                PutText(row.RelativeItem().AlignMiddle(), title, 9.5f, "#37474F", bold: true);
                PutText(row.AutoItem().AlignMiddle(), subtitle, 7.5f, "#90A4AE");
            });
        }

        /// <summary>各模块通过率：堆叠横条（看构成）+ 明细表（看精确数）</summary>
        private static Action<IContainer> ComposeModuleRateSection(RangeStatResult stat)
        {
            return container =>
            {
                container.Column(col =>
                {
                    SectionTitle(col.Item(), "各模块通过率", "通过率 = 通过 /（通过 + 失败），未测不计入");
                    col.Item().PaddingTop(4).Svg(ReportCharts.BuildModuleRateBars(stat.Modules));
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2.6f);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1.1f);
                        });
                        table.Header(h =>
                        {
                            HeadCell(h, "模块");
                            HeadCell(h, "通过", right: true);
                            HeadCell(h, "失败", right: true);
                            HeadCell(h, "未测", right: true);
                            HeadCell(h, "通过率", right: true);
                        });
                        foreach (var m in stat.Modules)
                        {
                            PutText(NewCell(table), $"{m.ModuleId} {m.ModuleName}", 8.5f);
                            PutText(NewCell(table, right: true), m.PassCount.ToString(), 8.5f, "#2E7D32");
                            PutText(NewCell(table, right: true), m.FailCount.ToString(), 8.5f,
                                m.FailCount > 0 ? "#C62828" : "#757575", bold: m.FailCount > 0);
                            PutText(NewCell(table, right: true), m.NotTestedCount.ToString(), 8.5f, "#757575");
                            PutText(NewCell(table, right: true), m.PassRateText, 8.5f, "#37474F", bold: true);
                        }
                    });
                });
            };
        }

        /// <summary>NG 原因汇总：失败测试项按项名聚合，备注取最近一次失败的失败点备注</summary>
        private static Action<IContainer> ComposeNgSection(RangeStatResult stat)
        {
            const int maxRows = 60;
            return container =>
            {
                container.Column(col =>
                {
                    SectionTitle(col.Item(), "NG 原因汇总", "区间内判定失败的测试项（按失败次数降序）");
                    if (stat.NgReasons.Count == 0)
                    {
                        PutText(col.Item().PaddingTop(6).AlignCenter(), "区间内没有失败项", 9f, "#90A4AE");
                        return;
                    }
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1.1f);
                            c.RelativeColumn(2.4f);
                            c.RelativeColumn(0.8f);
                            c.RelativeColumn(2.4f);
                        });
                        table.Header(h =>
                        {
                            HeadCell(h, "模块");
                            HeadCell(h, "测试项");
                            HeadCell(h, "失败次数", right: true);
                            HeadCell(h, "最近备注");
                        });
                        foreach (var n in stat.NgReasons.Take(maxRows))
                        {
                            PutText(NewCell(table), n.ModuleId, 8.5f);
                            PutText(NewCell(table), n.ItemName, 8.5f);
                            PutText(NewCell(table, right: true), n.FailCount.ToString(), 8.5f, "#C62828", bold: true);
                            PutText(NewCell(table), n.RemarkText, 8f, "#757575");
                        }
                    });
                    if (stat.NgReasons.Count > maxRows)
                        PutText(col.Item().PaddingTop(2), $"仅显示失败次数最多的前 {maxRows} 项（共 {stat.NgReasons.Count} 项）", 7.5f, "#9E9E9E");
                });
            };
        }

        /// <summary>按天趋势：堆叠柱（每日通过/失败轮次）+ 通过率折线；明细表只列最近 40 天</summary>
        private static Action<IContainer> ComposeTrendSection(RangeStatResult stat)
        {
            const int maxRows = 40;
            return container =>
            {
                container.Column(col =>
                {
                    SectionTitle(col.Item(), "按天趋势", "柱=每日通过/失败项数（左轴），折线=通过率（右轴）");
                    col.Item().PaddingTop(4).Svg(ReportCharts.BuildTrendChart(stat.Trend));

                    var rows = stat.Trend.Count > maxRows ? stat.Trend.TakeLast(maxRows).ToList() : stat.Trend;
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            HeadCell(h, "日期");
                            HeadCell(h, "轮次", right: true);
                            HeadCell(h, "通过", right: true);
                            HeadCell(h, "失败", right: true);
                            HeadCell(h, "通过率", right: true);
                        });
                        foreach (var t in rows)
                        {
                            PutText(NewCell(table), t.DateText, 8.5f);
                            PutText(NewCell(table, right: true), t.RunCount.ToString(), 8.5f);
                            PutText(NewCell(table, right: true), t.PassCount.ToString(), 8.5f, "#2E7D32");
                            PutText(NewCell(table, right: true), t.FailCount.ToString(), 8.5f,
                                t.FailCount > 0 ? "#C62828" : "#757575");
                            PutText(NewCell(table, right: true), t.PassRateText, 8.5f, "#37474F", bold: true);
                        }
                    });
                    if (stat.Trend.Count > maxRows)
                        PutText(col.Item().PaddingTop(2), $"明细仅列最近 {maxRows} 天，完整趋势见上图", 7.5f, "#9E9E9E");
                });
            };
        }

        // ==================== 单元格样式与文本 ====================

        /// <summary>落文本：先定默认样式再 Text（QuestPDF 的 Text() 返回 void，链式写在它后面会编译不过）</summary>
        private static void PutText(IContainer cell, string text, float size = 8.5f, string color = "#37474F",
            bool bold = false, bool right = false)
        {
            var target = right ? cell.AlignRight() : cell;
            target.DefaultTextStyle(x =>
            {
                x.FontSize(size).FontColor(color);
                if (bold) x.Bold();
                return x;
            }).Text(text);
        }

        /// <summary>表格一格：建格 + 对齐 + 体样式（背景/边框/内距是容器属性，不是子元素——之后只能再挂一个 Text）</summary>
        private static IContainer NewCell(TableDescriptor table, bool sub = false,
            bool right = false, bool middle = false, uint span = 0)
        {
            var cell = table.Cell();
            if (span > 0) cell = cell.ColumnSpan(span);
            var c = (IContainer)cell;
            c = sub ? SubCellStyle(c) : BodyCellStyle(c);
            if (right) c = c.AlignRight();
            if (middle) c = c.AlignMiddle();
            return c;
        }

        /// <summary>大项用时文本：秒保留一位、满 1 分钟转"X分Y秒"；null（老轮次未采集）显"—"</summary>
        private static string DurationText(double? seconds)
            => seconds is not double s ? "—"
                : s >= 60 ? $"{(int)(s / 60)}分{s % 60:0.0}秒" : $"{s:0.0}秒";

        /// <summary>轮次总用时（与历史列表 TestRunSummary.ElapsedText 同口径）</summary>
        private static string ElapsedText(DateTime startedAt, DateTime finishedAt)
        {
            var span = finishedAt - startedAt;
            return span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}分{span.Seconds}秒" : $"{(int)span.TotalSeconds}秒";
        }

        /// <summary>总判定/模块判定徽章</summary>
        private static void VerdictChip(IContainer cell, string verdict, bool big = false)
        {
            var (bg, fg) = verdict switch
            {
                "Pass" or "PASS" => ("#E8F5E9", "#2E7D32"),
                "Fail" or "FAIL" => ("#FFEBEE", "#C62828"),
                _ => ("#ECEFF1", "#607D8B"),
            };
            var styled = cell.Background(bg)
                .PaddingHorizontal(big ? 14 : 8)
                .PaddingVertical(big ? 5 : 2)
                .AlignCenter()
                .MinWidth(big ? 90 : 46);
            PutText(styled, VerdictText(verdict), big ? 15f : 8.5f, fg, bold: true);
        }

        /// <summary>测试项结果徽章（只展示项显"统计"，不参与判定的项显"未测"）</summary>
        private static void ItemVerdictChip(IContainer cell, TestItemResultEntity item)
        {
            var (bg, fg, text) = item.DisplayOnly ? ("#F5F5F5", "#9E9E9E", "统计")
                : item.Result == "Pass" ? ("#E8F5E9", "#2E7D32", "PASS")
                : item.Result == "Fail" ? ("#FFEBEE", "#C62828", "FAIL")
                : ("#F5F5F5", "#9E9E9E", "未测");
            var styled = cell.Background(bg).PaddingHorizontal(6).PaddingVertical(1).AlignCenter();
            PutText(styled, text, 8f, fg, bold: true);
        }

        private static void HeadCell(TableCellDescriptor table, string text, bool right = false)
        {
            var cell = (IContainer)table.Cell();
            if (right) cell = cell.AlignRight();
            // 注意：QuestPDF 容器扩展是包裹式的，返回值必须接住，丢了就会在单元格上留两个子元素
            cell = cell.Background("#ECEFF1").Padding(4);
            PutText(cell, text, 8f, "#455A64", bold: true);
        }

        /// <summary>体单元格样式：容器属性（边框/内距），之后只能挂一个 Text 子元素</summary>
        private static IContainer BodyCellStyle(IContainer c) => c
            .BorderBottom(0.5f).BorderColor("#EEEEEE")
            .PaddingVertical(3).PaddingHorizontal(4);

        /// <summary>子表行样式：容器属性（浅底/细边框/紧内距）</summary>
        private static IContainer SubCellStyle(IContainer c) => c
            .Background("#FAFAFA")
            .BorderBottom(0.5f).BorderColor("#F0F0F0")
            .PaddingVertical(1.5f).PaddingHorizontal(4);

        private static string N(double? v) => v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) : "--";

        /// <summary>规格列：本次运行快照（SpecText 优先；对称区间显示 ±，单侧显示 ≥/≤）</summary>
        private static string SpecText(TestItemResultEntity item)
        {
            if (!string.IsNullOrWhiteSpace(item.SpecText)) return item.SpecText;
            if (item.SpecMin.HasValue && item.SpecMax.HasValue)
            {
                return Math.Abs(item.SpecMin.Value + item.SpecMax.Value) < 1e-9
                    ? $"±{item.SpecMax.Value.ToString("0.##", CultureInfo.InvariantCulture)}"
                    : $"[{item.SpecMin.Value:0.##}, {item.SpecMax.Value:0.##}]";
            }
            if (item.SpecMax.HasValue) return $"≤ {item.SpecMax.Value:0.##}";
            if (item.SpecMin.HasValue) return $"≥ {item.SpecMin.Value:0.##}";
            return "--";
        }

        /// <summary>测试值列：文本优先（与界面同口径），无数值再退主值</summary>
        private static string ValueText(TestItemResultEntity item)
        {
            if (!string.IsNullOrWhiteSpace(item.ProductText)) return item.ProductText;
            if (!string.IsNullOrWhiteSpace(item.InstrumentText)) return item.InstrumentText;
            if (item.Value.HasValue)
                return item.Unit.Length > 0 ? $"{item.Value.Value:0.##} {item.Unit}" : item.Value.Value.ToString("0.##");
            return "--";
        }

        /// <summary>子表行文本（按口径渲染；RepeatStats 数值行返回 null 跳过——只进图表）</summary>
        private static string? PointRowText(TestPointResultEntity p)
        {
            var verdict = p.ShowVerdict ? (p.Pass ? "｜PASS" : "｜FAIL") : string.Empty;
            var remark = string.IsNullOrEmpty(p.Remark) ? string.Empty : $"｜{p.Remark}";
            switch (p.Schema)
            {
                case "Channel" or "Group" or "Point":
                {
                    var target = p.InstrumentValue ?? p.TargetPressure;
                    var parts = new List<string> { p.Name };
                    parts.Add($"设定 {N2(target)}");
                    parts.Add($"实测 {N2(p.ProductValue)}");
                    if (p.Error.HasValue) parts.Add($"偏差 {N2(p.Error)}");
                    return "├ " + string.Join("｜", parts) + verdict + remark;
                }
                case "Repeat":
                    return "├ 统计｜均值 " + N2(p.Average) + "｜与设定偏差 " + N2(p.Error) +
                           "｜极大 " + N2(p.Max) + "｜极小 " + N2(p.Min) + "｜极差 " + N2(p.Range) +
                           (p.ShowVerdict ? verdict : "（仅统计）") + remark;
                case "RepeatMatrix":
                {
                    var cells = DecodeCells(p.CellsJson);
                    return cells.Count > 0 ? "├ " + p.Name + "｜" + string.Join("｜", cells) + verdict + remark : null;
                }
                case "Event" or "Slice":
                {
                    var cells = DecodeCells(p.CellsJson);
                    return cells.Count > 0 ? "├ " + p.Name + "｜" + string.Join("；", cells) + verdict + remark : null;
                }
                default:
                {
                    var cells = DecodeCells(p.CellsJson);
                    return cells.Count > 0 ? "├ " + p.Name + "｜" + string.Join("；", cells) : null;
                }
            }
        }

        private static string N2(double? v) => v.HasValue ? v.Value.ToString("0.##", CultureInfo.InvariantCulture) : "--";

        private static List<string> DecodeCells(string? json)
        {
            if (string.IsNullOrEmpty(json)) return new List<string>();
            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                return new List<string> { json };
            }
        }
    }
}
