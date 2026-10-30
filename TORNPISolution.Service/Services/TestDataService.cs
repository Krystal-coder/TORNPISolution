using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TORNPISolution.Data;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 测试数据服务：一轮测试结果的入库与区间/轮次查询。写库走单事务（TestRun/TestItemResult/TestPointResult），失败整体回滚。
    /// 报告编号 = 日期+当天序号（唯一索引防撞）；未测项照常入库，规格逐行存本次运行快照——配置改动不影响历史数据对账。
    /// </summary>
    public class TestDataService : ITestDataService
    {
        private readonly ILogService _log = new LogService(nameof(TestDataService));

        // 预热闸门：SQLite 没有真异步，首碰数据库那段（建模型 + 加载 e_sqlite3 + 建表）会同步冻住调用线程。
        // 成本只付一次，所以收口在一个闸门里由启动后台发起，读写路径只等它、不再各自跑 DDL
        private static readonly SemaphoreSlim WarmGate = new(1, 1);
        private static bool _warmed;

        // 老库需要补的列（表, 列, 类型）：加字段时在这里登记一条，预热时缺哪列补哪列
        private static readonly (string Table, string Column, string Type)[] AddedColumns =
        {
            ("TestRun", "ModuleTimingsJson", "TEXT"),
            ("TestRun", "MeasureSeconds", "INTEGER"),
            ("TestItemResult", "DurationSeconds", "REAL"),
        };

        /// <inheritdoc/>
        public Task PrepareStorageAsync(CancellationToken ct = default) => WarmAsync(ct);

        /// <summary>预热：建库并让 EF 编译一次查询（进程内一次；失败不记账，下次调用重试）</summary>
        private static async Task WarmAsync(CancellationToken ct)
        {
            if (_warmed) return;
            await WarmGate.WaitAsync(ct);
            try
            {
                if (_warmed) return;
                await using var db = new TornpiDbContext();
                await db.Database.EnsureCreatedAsync(ct);
                await EnsureAddedColumnsAsync(db, ct);
                await db.TestRuns.AnyAsync(ct);
                _warmed = true;
            }
            finally
            {
                WarmGate.Release();
            }
        }

        /// <summary>
        /// EnsureCreated 只建绿场库、不给已存在的库补列，老库缺列时插入会直接失败：这里按 <see cref="AddedColumns"/> 逐列一次性 ALTER（新库由 EnsureCreated 自带，本方法空转）。
        /// </summary>
        private static async Task EnsureAddedColumnsAsync(TornpiDbContext db, CancellationToken ct)
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync(ct);
            try
            {
                foreach (var (table, column, type) in AddedColumns)
                {
                    await using var check = conn.CreateCommand();
                    check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
                    if (Convert.ToInt64(await check.ExecuteScalarAsync(ct) ?? 0L) > 0) continue;

                    await using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
                    await alter.ExecuteNonQueryAsync(ct);
                }
            }
            finally
            {
                await conn.CloseAsync();
            }
        }

        /// <inheritdoc/>
        public async Task<TestRunSaveResult> SaveRunAsync(TestRunSnapshot snapshot, CancellationToken ct = default)
        {
            TestDataPaths.EnsureDirectories();
            await WarmAsync(ct);   // 建表收口在预热：入库前库结构一定在，且不再每轮跑一遍 DDL

            await using var db = new TornpiDbContext();

            // 单事务：轮次 + 测试项 + 逐点，任何一步失败整体回滚
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // 计数与整体判定只统计参与判定的项（只展示/不判定项不计），与界面模块判定同口径
            var judged = snapshot.Items.Where(i => !i.DisplayOnly && !i.NotJudged).ToList();
            var pass = judged.Count(i => i.Result == TestResultStatus.Pass);
            var fail = judged.Count(i => i.Result == TestResultStatus.Fail);
            var notTested = judged.Count - pass - fail;
            var overall = fail > 0 ? "Fail" : (judged.Count > 0 && pass == judged.Count ? "Pass" : "NotTested");

            var reportNo = await GenerateReportNoAsync(db, snapshot.FinishedAt, ct);

            var run = new TestRunEntity
            {
                ReportNo = reportNo,
                Station = snapshot.Station,
                PlanName = snapshot.PlanName,
                ModulesJson = JsonSerializer.Serialize(snapshot.ModuleIds),
                ModuleTimingsJson = snapshot.ModuleTimings.Count > 0 ? JsonSerializer.Serialize(snapshot.ModuleTimings) : null,
                MeasureSeconds = snapshot.MeasureSeconds,
                StartedAt = snapshot.StartedAt,
                FinishedAt = snapshot.FinishedAt,
                OverallResult = overall,
                PassCount = pass,
                FailCount = fail,
                NotTestedCount = notTested,
                ProductMAC = ResolveProductMac(snapshot),
                ProductSN = snapshot.ProductSN,
                Operator = snapshot.Operator,
                CreatedAt = DateTime.Now,
            };
            db.TestRuns.Add(run);
            await db.SaveChangesAsync(ct);

            var pointCount = 0;
            var itemSort = 0;
            foreach (var item in snapshot.Items)
            {
                var entity = new TestItemResultEntity
                {
                    RunId = run.Id,
                    ModuleId = item.ModuleId,
                    ModuleName = item.ModuleName,
                    Sequence = item.Sequence,
                    ItemName = item.ItemName,
                    Unit = item.Unit,
                    ValueSource = item.ValueSource,
                    Value = NumOrNull(item.Value),
                    InstrumentValue = NumOrNull(item.InstrumentValue),
                    ProductValue = NumOrNull(item.ProductValue),
                    InstrumentText = item.InstrumentText,
                    ProductText = item.ProductText,
                    SpecMin = item.SpecMin,
                    SpecMax = item.SpecMax,
                    SpecText = item.SpecText,
                    Result = item.Result.ToString(),
                    DisplayOnly = item.DisplayOnly,
                    NotJudged = item.NotJudged,
                    DurationSeconds = item.DurationSeconds,
                    SortOrder = itemSort++,
                };
                db.TestItemResults.Add(entity);
                await db.SaveChangesAsync(ct);   // 先拿测试项 Id，逐点行挂它

                var pointSort = 0;
                foreach (var p in item.Points)
                {
                    db.TestPointResults.Add(new TestPointResultEntity
                    {
                        ItemResultId = entity.Id,
                        Name = p.Name,
                        Schema = p.Schema,
                        Unit = p.Unit,
                        TargetPressure = p.TargetPressure,
                        InstrumentValue = NumOrNull(p.InstrumentValue),
                        ProductValue = NumOrNull(p.ProductValue),
                        Error = NumOrNull(p.Error),
                        Pass = p.Pass,
                        Remark = p.Remark,
                        Average = NumOrNull(p.Average),
                        Max = NumOrNull(p.Max),
                        Min = NumOrNull(p.Min),
                        Range = NumOrNull(p.Range),
                        CellsJson = p.Cells is { Count: > 0 } ? JsonSerializer.Serialize(p.Cells) : null,
                        ShowVerdict = p.ShowVerdict,
                        SortOrder = pointSort++,
                    });
                    pointCount++;
                }
            }
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            _log.Info($"测试数据已入库：编号 {reportNo}（{snapshot.PlanName}），" +
                      $"{snapshot.Items.Count} 个测试项 / {pointCount} 条子表，整体判定 {overall}");
            return new TestRunSaveResult
            {
                RunId = run.Id,
                ReportNo = reportNo,
                PassCount = pass,
                FailCount = fail,
                NotTestedCount = notTested,
                PointCount = pointCount,
            };
        }

        /// <inheritdoc/>
        public async Task<List<TestRunSummary>> QueryRunsAsync(int maxCount = 300, CancellationToken ct = default)
        {
            await PrepareStorageAsync(ct);   // 只等预热，读路径不碰 DDL（绿场开页同样走空态提示）
            await using var db = new TornpiDbContext();
            var runs = await db.TestRuns
                .OrderByDescending(r => r.FinishedAt)
                .Take(maxCount)
                .ToListAsync(ct);
            return runs.Select(r => new TestRunSummary
            {
                ReportNo = r.ReportNo,
                Station = r.Station,
                PlanName = r.PlanName,
                OverallResult = r.OverallResult,
                ProductMAC = r.ProductMAC,
                StartedAt = r.StartedAt,
                FinishedAt = r.FinishedAt,
                PassCount = r.PassCount,
                FailCount = r.FailCount,
                NotTestedCount = r.NotTestedCount,
                ModuleIds = ParseModuleIds(r.ModulesJson),
            }).ToList();
        }

        /// <inheritdoc/>
        public async Task<RangeStatResult> QueryRangeStatsAsync(RangeStatQuery query, CancellationToken ct = default)
        {
            await PrepareStorageAsync(ct);   // 同 QueryRunsAsync：读路径只等预热
            await using var db = new TornpiDbContext();

            var result = new RangeStatResult
            {
                From = query.From.Date,
                To = query.To.Date,
                Station = query.Station,
                PlanName = query.PlanName,
            };

            // 区间闭区间：To 含当天全天；工位/方案可选筛选。筛选值来自可手输的 ComboBox，
            // 两边都去首尾空格并按大小写不敏感比（不能给 string.Equals 传 StringComparison——
            // Sqlite provider 翻译不了会直接抛，改成两边 upper()）
            var toExclusive = query.To.Date.AddDays(1);
            var runsQ = db.TestRuns.Where(r => r.FinishedAt >= query.From.Date && r.FinishedAt < toExclusive);
            if (query.Station?.Trim().ToUpper() is { Length: > 0 } station)
                runsQ = runsQ.Where(r => r.Station.ToUpper() == station);
            if (query.PlanName?.Trim().ToUpper() is { Length: > 0 } planName)
                runsQ = runsQ.Where(r => r.PlanName.ToUpper() == planName);

            var runs = await runsQ.ToListAsync(ct);
            if (runs.Count == 0) return result;

            result.RunCount = runs.Count;
            result.TotalPass = runs.Sum(r => r.PassCount);
            result.TotalFail = runs.Sum(r => r.FailCount);
            result.TotalNotTested = runs.Sum(r => r.NotTestedCount);

            // 区间内参与判定的测试项：与轮次表 JOIN 后过滤（不要用 runsQ.Any 相关子查询——
            // 在 GROUP BY 投影里会被翻译成行数放大，模块计数会翻成轮次数倍）
            var itemRuns = db.TestItemResults
                .Join(runsQ, i => i.RunId, r => r.Id, (i, r) => new { Item = i, Run = r })
                .Where(x => !x.Item.DisplayOnly && !x.Item.NotJudged);

            // 模块通过率：按模块分组，计数与单轮模块判定同口径
            result.Modules = await itemRuns
                .GroupBy(x => new { x.Item.ModuleId, x.Item.ModuleName })
                .Select(g => new ModuleStat
                {
                    ModuleId = g.Key.ModuleId,
                    ModuleName = g.Key.ModuleName,
                    PassCount = g.Count(x => x.Item.Result == "Pass"),
                    FailCount = g.Count(x => x.Item.Result == "Fail"),
                    NotTestedCount = g.Count(x => x.Item.Result == "NotTested"),
                })
                .ToListAsync(ct);
            result.Modules = result.Modules.OrderBy(m => m.ModuleId).ToList();

            // NG 原因：失败项按项名聚合，备注取最近一次失败轮次里失败点的备注（没有备注显示 —）
            var failed = await db.TestItemResults
                .Where(i => !i.DisplayOnly && !i.NotJudged && i.Result == "Fail")
                .Join(runsQ, i => i.RunId, r => r.Id, (i, r) => i)
                .Include(i => i.Points)
                .Include(i => i.Run)
                .ToListAsync(ct);
            result.NgReasons = failed
                .GroupBy(i => (i.ModuleId, i.ItemName))
                .Select(g => new NgReason
                {
                    ModuleId = g.Key.ModuleId,
                    ModuleName = g.First().ModuleName,
                    ItemName = g.Key.ItemName,
                    FailCount = g.Count(),
                    LastRemark = g.OrderByDescending(i => i.Run!.FinishedAt)
                        .SelectMany(i => i.Points.Where(p => !p.Pass && !string.IsNullOrEmpty(p.Remark)).Select(p => p.Remark))
                        .FirstOrDefault(),
                })
                .OrderByDescending(x => x.FailCount)
                .ThenBy(x => x.ItemName)
                .ToList();

            // 按天趋势：轮次已全在内存，按完成日期归组
            result.Trend = runs
                .GroupBy(r => r.FinishedAt.Date)
                .Select(g => new TrendPoint
                {
                    Date = g.Key,
                    RunCount = g.Count(),
                    PassCount = g.Sum(r => r.PassCount),
                    FailCount = g.Sum(r => r.FailCount),
                })
                .OrderBy(t => t.Date)
                .ToList();

            return result;
        }

        /// <summary>解析轮次的模块 ID 快照（历史列表显示"这轮测了哪几个模块"；解析失败不挡列表）</summary>
        private static List<string> ParseModuleIds(string json)        {
            if (string.IsNullOrEmpty(json)) return new List<string>();
            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>MAC 自动带入：UI 传空时从 A16「产品信息获取(MAC地址)」结果取（Value=1 才是有效读回）</summary>
        private static string? ResolveProductMac(TestRunSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.ProductMAC))
                return snapshot.ProductMAC;

            var macItem = snapshot.Items.FirstOrDefault(i => i.ItemName == SpecItems.ProductMacAddress);
            if (macItem is { Value: 1 } && !string.IsNullOrWhiteSpace(macItem.ProductText)
                && macItem.ProductText != "无响应" && macItem.ProductText != "通讯失败")
                return macItem.ProductText;
            return null;
        }

        /// <summary>非有限值（NaN/±∞）视为"没测到值"：存 null（项级与逐点级同一口径，脏值进库会污染统计与报表坐标）</summary>
        private static double? NumOrNull(double? value)
            => value.HasValue && double.IsFinite(value.Value) ? value : null;

        /// <summary>报告编号：日期+当天序号（如 20261022-0001）。按当天序号的数值取最大再 +1，
        /// 脏编号跳过（字符串序在 9999 之后会退回旧号、非数字尾巴会直接抛异常，抛在重试循环里就永远存不进去）；
        /// 跨进程抢号仍由唯一索引兜底——失败后重试会重扫当天编号，自动跳到下一个空位</summary>
        private static async Task<string> GenerateReportNoAsync(TornpiDbContext db, DateTime date, CancellationToken ct)
        {
            var prefix = date.ToString("yyyyMMdd") + "-";
            var todays = await db.TestRuns
                .Where(r => r.ReportNo.StartsWith(prefix))
                .Select(r => r.ReportNo)
                .ToListAsync(ct);

            var maxSeq = 0;
            foreach (var no in todays)
                if (no.Length > prefix.Length && int.TryParse(no[prefix.Length..], out var seq) && seq > maxSeq)
                    maxSeq = seq;

            return $"{prefix}{maxSeq + 1:0000}";
        }
    }
}
