using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>出报告时的口径快照：页头信息表与「判定口径」这一句按这几个字写</summary>
    public sealed class AccelReportMeta
    {
        public string PortSummary { get; init; } = "未连接";

        public double ToleranceDegrees { get; init; }

        public string CompareAxis { get; init; } = AccelAngleJudge.AxisRoll;
    }

    /// <summary>
    /// 加速度充电检查的 PDF 报告：吃已经落盘的那份采样文件（CSV），页头 + 判定口径 + 分段统计 + Roll 折线 + 逐段分档明细（超限/合格/未比对），
    /// 版式与整机报告同一档。数据只认文件里的行——暂停保存那一段没进文件，也就不进报告。
    /// </summary>
    public static class AccelReportService
    {
        private static readonly ILogService Log = new LogService(nameof(AccelReportService));

        /// <summary>线色按标签首次出现的顺序取，与界面 Roll 曲线同一排（十六进制写法给 SVG 用）</summary>
        private static readonly string[] Palette =
        {
            "#1F77B4", "#D62728", "#2CA02C", "#9467BD", "#FF7F0E",
            "#17BECF", "#E377C2", "#BCBD22", "#8C564B", "#7F7F7F"
        };

        /// <summary>每段画进图的点数上限（min/max 包络折下来，几小时的采样也不会把 SVG 撑成几兆）</summary>
        private const int ChartPointsPerSegment = 320;

        /// <summary>每张逐帧明细小表的行数上限（按段、按超限/合格/未比对各算一份），余下的指向同名标红表</summary>
        private const int MaxDetailRowsPerTable = 40;

        static AccelReportService() => QuestPDF.Settings.License = LicenseType.Community;

        /// <summary>
        /// 从这份采样文件出同名 PDF（.csv 换成 .pdf，落在同一个目录）。
        /// 先落临时名再改名：退出时的收尾预算只有几秒，宁可少一个文件也不要半个。
        /// </summary>
        /// <returns>PDF 全路径；这份文件里没有可写的行或生成失败时为 null</returns>
        public static string? WritePdf(string dataPath, AccelReportMeta meta)
        {
            var path = Path.ChangeExtension(dataPath, ".pdf");
            var temp = path + ".tmp.pdf";
            try
            {
                var report = ReadReport(dataPath);
                if (report == null)
                {
                    Log.Info($"加速度 PDF 跳过：{dataPath} 里没有已落盘的采样行");
                    return null;
                }

                BuildDocument(report, meta).GeneratePdf(temp);
                File.Move(temp, path, true);
                Log.Info($"加速度充电检查报告已生成：{path}（数据量 {report.TotalFrames}，超限 {report.ExceededFrames}，未比对 {report.NotJudgedFrames}）");
                return path;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* 临时名删不掉只是多个残文件 */ }
                Log.Warn($"加速度 PDF 生成失败（{dataPath}）：{ex.GetType().Name} {ex.Message}");
                return null;
            }
        }

        // ==================== 读采样文件 ====================

        /// <summary>一帧（CSV 的一行数据），时刻已换成相对首帧的秒数</summary>
        private sealed class Frame
        {
            public int Seq;
            public string TimeText = string.Empty;
            public double AtSeconds;
            public double SequenceClock;
            public double Roll;
            public double Pitch;
            public double? CompareAngle;
            public double? Deviation;
            public bool Exceeded;

            /// <summary>本档没示教、标签里也读不出角度：比不出偏差，既不算超限也不算合格</summary>
            public bool NotJudged;
        }

        /// <summary>一段（同一个姿态标签的一次连续区间）：逐帧 + 段统计行搬过来的平均/最大/最小</summary>
        private sealed class Segment
        {
            public int Index;
            public string Tag = string.Empty;
            public string Color = Palette[0];
            public readonly List<Frame> Frames = new();
            public string? RollAvg;
            public string? RollMax;
            public string? RollMin;
            public string? DevAvg;
            public string? DevMax;
            public string? DevMin;

            public int ExceededCount => Frames.Count(f => f.Exceeded);

            public int NotJudgedCount => Frames.Count(f => f.NotJudged);

            /// <summary>合格只数真比过且没超的：未比对混进合格里就是报告上的假合格</summary>
            public int PassedCount => Frames.Count(f => !f.Exceeded && !f.NotJudged);
        }

        private sealed class Report
        {
            public List<Segment> Segments = new();
            public int TotalFrames;
            public int ExceededFrames;
            public int NotJudgedFrames;
            public string RangeText = string.Empty;
            public string FileName = string.Empty;
        }

        private static Report? ReadReport(string dataPath)
        {
            var blocks = AccelCheckExcelWriter.ReadBlocks(dataPath);
            var segments = new List<Segment>();
            var colors = new Dictionary<string, string>();
            var origin = 0.0;
            var previousSeconds = 0.0;
            var dayShift = 0.0;
            var firstFrame = true;
            var lastSeconds = 0.0;

            foreach (var block in blocks)
            {
                var cTime = block.Column("时间");
                var cRoll = block.Column("Roll(°)");
                // 列名认不出来就跳过这段：拿错列画出来的曲线比不画更坏
                if (cTime < 0 || cRoll < 0) continue;

                var segment = new Segment
                {
                    Index = segments.Count + 1,
                    Tag = block.Tag
                };

                foreach (var cells in block.Rows)
                {
                    var frame = ReadFrame(cells, block, cTime, cRoll);
                    if (frame == null) continue;

                    // 时刻只到时分秒（落盘就是 HH:mm:ss.fff）：跨午夜时时刻会掉回一天前，靠这个跌幅补回偏移
                    var secondsOfDay = frame.SequenceClock;
                    if (firstFrame)
                    {
                        origin = secondsOfDay;
                        previousSeconds = secondsOfDay;
                        firstFrame = false;
                    }
                    else if (previousSeconds - secondsOfDay > 43_200)
                    {
                        dayShift += 86_400;
                    }

                    previousSeconds = secondsOfDay;
                    frame.AtSeconds = secondsOfDay + dayShift - origin;
                    lastSeconds = Math.Max(lastSeconds, frame.AtSeconds);
                    segment.Frames.Add(frame);
                }

                if (segment.Frames.Count == 0) continue;

                if (!colors.TryGetValue(segment.Tag, out var color))
                    color = colors[segment.Tag] = Palette[colors.Count % Palette.Length];
                segment.Color = color;

                ReadStats(block, segment);
                segments.Add(segment);
            }

            if (segments.Count == 0) return null;

            return new Report
            {
                Segments = segments,
                TotalFrames = segments.Sum(s => s.Frames.Count),
                ExceededFrames = segments.Sum(s => s.ExceededCount),
                NotJudgedFrames = segments.Sum(s => s.NotJudgedCount),
                FileName = Path.GetFileName(dataPath),
                RangeText = Clock(lastSeconds)
            };
        }

        private static Frame? ReadFrame(string[] cells, AccelCheckExcelWriter.Block block, int cTime, int cRoll)
        {
            if (cTime >= cells.Length || cRoll >= cells.Length) return null;   // 正在写的那半行
            if (!TimeSpan.TryParse(cells[cTime], CultureInfo.InvariantCulture, out var clock)) return null;
            if (!double.TryParse(cells[cRoll], NumberStyles.Float, CultureInfo.InvariantCulture, out var roll)) return null;

            var judgement = block.Cell(cells, "超判定");
            return new Frame
            {
                Seq = ParseInt(block.Cell(cells, "序号")),
                TimeText = cells[cTime],
                SequenceClock = clock.TotalSeconds,
                Roll = roll,
                Pitch = ParseDouble(block.Cell(cells, "Pitch(°)")),
                CompareAngle = ParseDoubleOrNull(block.Cell(cells, "比对角(°)")),
                Deviation = ParseDoubleOrNull(block.Cell(cells, "偏差(°)")),
                Exceeded = judgement == "超限",
                NotJudged = judgement == "未比对"
            };
        }

        /// <summary>段统计那三行（平均/最大/最小）按列名取值，报告上的大小值就是这里来的，口径仍是落盘的两位小数</summary>
        private static void ReadStats(AccelCheckExcelWriter.Block block, Segment segment)
        {
            segment.RollAvg = StatOf(block.Average, block, "Roll(°)");
            segment.RollMax = StatOf(block.Max, block, "Roll(°)");
            segment.RollMin = StatOf(block.Min, block, "Roll(°)");
            segment.DevAvg = StatOf(block.Average, block, "偏差(°)");
            segment.DevMax = StatOf(block.Max, block, "偏差(°)");
            segment.DevMin = StatOf(block.Min, block, "偏差(°)");
        }

        private static string? StatOf(string[]? stat, AccelCheckExcelWriter.Block block, string columnName)
        {
            if (stat == null) return null;
            var text = block.Cell(stat, columnName);
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static int ParseInt(string text)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

        private static double ParseDouble(string text)
            => ParseDoubleOrNull(text) ?? 0;

        private static double? ParseDoubleOrNull(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

        // ==================== 文档 ====================

        private static IDocument BuildDocument(Report report, AccelReportMeta meta)
        {
            var segments = report.Segments;
            var totalFrames = report.TotalFrames;
            var exceededFrames = report.ExceededFrames;
            var notJudgedFrames = report.NotJudgedFrames;
            var fileName = report.FileName;
            var rangeText = report.RangeText;
            var chart = ReportCharts.BuildAccelRollLineChart(BuildChartModel(segments));
            var caption = JudgementCaption(meta);

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(x => x.FontSize(9f).FontColor("#333333"));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                PutText(c.Item(), "加速度充电检查报告", 17f, "#212121", bold: true);
                                PutText(c.Item(), $"采样文件 {fileName}", 9f, "#757575");
                            });
                            VerdictChip(row.ConstantItem(150).AlignRight().AlignMiddle(), exceededFrames, notJudgedFrames, totalFrames);
                        });

                        col.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(1);
                                c.RelativeColumn(1.2f);
                                c.RelativeColumn(1.1f);
                                c.RelativeColumn(1.3f);
                            });
                            void Info(string label, string value)
                            {
                                table.Cell().Padding(2).Column(c =>
                                {
                                    PutText(c.Item(), label, 7.5f, "#9E9E9E");
                                    PutText(c.Item(), value, 9f, "#37474F");
                                });
                            }
                            Info("产品串口", meta.PortSummary);
                            Info("采集区间", $"0:00 ~ {rangeText}");
                            Info("姿态段数", segments.Count.ToString());
                            Info("数据量", totalFrames.ToString());
                            Info("超限数", exceededFrames.ToString());
                            Info("合格数", (totalFrames - exceededFrames - notJudgedFrames).ToString());
                            Info("未比对数", notJudgedFrames.ToString());
                            Info("生成时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        });

                        col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#E0E0E0");
                    });

                    page.Content().PaddingTop(6).Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Background("#F5F7FA").Padding(6).Text(text =>
                        {
                            text.Span("判定口径").Bold().FontSize(10f).FontColor("#37474F");
                            text.Span($"　{caption}").FontSize(8.5f).FontColor("#78909C");
                        });

                        col.Item().Element(c => ComposeSegmentTable(c, segments));
                        col.Item().Element(c => ComposeChart(c, chart));
                        col.Item().Element(c => ComposeDetail(c, segments));
                    });

                    page.Footer().Height(22).AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(7.5f).FontColor("#9E9E9E"));
                        text.Span($"{fileName} · 生成于 {DateTime.Now:yyyy-MM-dd HH:mm} · 第 ");
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                        text.Span(" 页");
                    });
                });
            });
        }

        private static void ComposeSegmentTable(IContainer container, List<Segment> segments)
        {
            container.Column(col =>
            {
                PutText(col.Item(), "分段统计", 11f, "#263238", bold: true);
                col.Item().PaddingTop(2).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(24);
                        c.RelativeColumn(1.6f);
                        c.ConstantColumn(36);
                        c.ConstantColumn(32);
                        c.ConstantColumn(32);
                        c.ConstantColumn(36);
                        c.ConstantColumn(44);
                        c.ConstantColumn(44);
                        c.ConstantColumn(44);
                        c.ConstantColumn(44);
                        c.ConstantColumn(44);
                        c.ConstantColumn(44);
                    });
                    table.Header(h =>
                    {
                        HeadCell(h, "段");
                        HeadCell(h, "姿态标签");
                        HeadCell(h, "数据量", right: true);
                        HeadCell(h, "超限", right: true);
                        HeadCell(h, "合格", right: true);
                        HeadCell(h, "未比对", right: true);
                        HeadCell(h, "Roll 平均", right: true);
                        HeadCell(h, "Roll 极大", right: true);
                        HeadCell(h, "Roll 极小", right: true);
                        HeadCell(h, "偏差平均", right: true);
                        HeadCell(h, "偏差极大", right: true);
                        HeadCell(h, "偏差极小", right: true);
                    });

                    foreach (var s in segments)
                    {
                        PutText(NewCell(table, middle: true), s.Index.ToString(), 8.5f, "#90A4AE");
                        PutText(NewCell(table, middle: true), s.Tag, 8.5f);
                        PutText(NewCell(table, right: true, middle: true), s.Frames.Count.ToString(), 8.5f);
                        PutText(NewCell(table, right: true, middle: true), s.ExceededCount.ToString(), 8.5f,
                            s.ExceededCount > 0 ? "#C62828" : "#757575", bold: s.ExceededCount > 0);
                        PutText(NewCell(table, right: true, middle: true), s.PassedCount.ToString(), 8.5f, "#757575");
                        PutText(NewCell(table, right: true, middle: true), s.NotJudgedCount.ToString(), 8.5f,
                            s.NotJudgedCount > 0 ? "#EF6C00" : "#757575", bold: s.NotJudgedCount > 0);
                        PutText(NewCell(table, right: true, middle: true), Dash(s.RollAvg), 8.5f, "#37474F");
                        PutText(NewCell(table, right: true, middle: true), Dash(s.RollMax), 8.5f, "#37474F", bold: true);
                        PutText(NewCell(table, right: true, middle: true), Dash(s.RollMin), 8.5f, "#37474F", bold: true);
                        PutText(NewCell(table, right: true, middle: true), Dash(s.DevAvg), 8f, "#757575");
                        PutText(NewCell(table, right: true, middle: true), Dash(s.DevMax), 8f, "#757575");
                        PutText(NewCell(table, right: true, middle: true), Dash(s.DevMin), 8f, "#757575");
                    }
                });
                PutText(col.Item().PaddingTop(2), "偏差列空着（显 —）表示这一段没示教名义角、按标签文本比不出偏差；大小值取段统计行，与文件里那两位小数同源",
                    7f, "#9E9E9E");
            });
        }

        private static void ComposeChart(IContainer container, string svg)
        {
            container.Column(col =>
            {
                PutText(col.Item(), "Roll 折线", 11f, "#263238", bold: true);
                col.Item().PaddingTop(2).Svg(svg);
                PutText(col.Item(), "一色一段（同一姿态标签的一次连续区间）；同名标签来回切过的多次区间共用这一色但中间不连线。红点=超限帧，虚线=0°。"
                        + $"每段超 {ChartPointsPerSegment} 点按波峰波谷抽稀，红点只标抽稀后仍留下的超限帧，条数可能少于本段超限数。",
                    7f, "#9E9E9E");
            });
        }

        /// <summary>逐帧明细按段成节：一档一小节，节里先列本档的超限帧、再列本档的合格帧，各节的条数加起来就是这一档的数据量</summary>
        private static void ComposeDetail(IContainer container, List<Segment> segments)
        {
            container.Column(col =>
            {
                PutText(col.Item(), "逐帧明细", 11f, "#263238", bold: true);

                foreach (var segment in segments)
                {
                    col.Item().PaddingTop(8).Column(inner =>
                    {
                        PutText(inner.Item(),
                            $"段 {segment.Index}　{segment.Tag}　数据量 {segment.Frames.Count}｜超限 {segment.ExceededCount}｜合格 {segment.PassedCount}｜未比对 {segment.NotJudgedCount}",
                            9f, "#37474F", bold: true);

                        FrameTable(inner.Item().PaddingTop(3), segment,
                            segment.Frames.Where(f => f.Exceeded).ToList(), "超限帧", "#C62828");
                        FrameTable(inner.Item().PaddingTop(5), segment,
                            segment.Frames.Where(f => !f.Exceeded && !f.NotJudged).ToList(), "合格帧", "#2E7D32");
                        FrameTable(inner.Item().PaddingTop(5), segment,
                            segment.Frames.Where(f => f.NotJudged).ToList(), "未比对帧（比不出偏差，既不算超限也不算合格）", "#EF6C00");
                    });
                }
            });
        }

        /// <summary>一表一类的逐帧明细：没有就一句话带过，超上限的按类各截各的（不再全局截断，免得后几档一行不剩）</summary>
        private static void FrameTable(IContainer container, Segment segment, List<Frame> frames, string title, string color)
        {
            if (frames.Count == 0)
            {
                PutText(container, $"{title}：本段没有。", 8f, "#9E9E9E");
                return;
            }

            container.Column(col =>
            {
                PutText(col.Item(), $"{title} {frames.Count} 条", 8.5f, color, bold: true);
                var listed = frames.Take(MaxDetailRowsPerTable).ToList();

                col.Item().PaddingTop(2).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(36);
                        c.ConstantColumn(62);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                    });
                    table.Header(h =>
                    {
                        HeadCell(h, "序号");
                        HeadCell(h, "时间");
                        HeadCell(h, "Roll(°)", right: true);
                        HeadCell(h, "Pitch(°)", right: true);
                        HeadCell(h, "比对角(°)", right: true);
                        HeadCell(h, "偏差(°)", right: true);
                    });

                    foreach (var frame in listed)
                    {
                        PutText(NewCell(table, middle: true), frame.Seq.ToString(), 8f, "#90A4AE");
                        PutText(NewCell(table, middle: true), frame.TimeText, 8f);
                        PutText(NewCell(table, right: true, middle: true), frame.Roll.ToString("0.00", CultureInfo.InvariantCulture), 8f,
                            frame.Exceeded ? "#C62828" : "#37474F", bold: frame.Exceeded);
                        PutText(NewCell(table, right: true, middle: true), NumberText(frame.Pitch), 8f, "#757575");
                        PutText(NewCell(table, right: true, middle: true), NumberText(frame.CompareAngle), 8f, "#757575");
                        PutText(NewCell(table, right: true, middle: true), NumberText(frame.Deviation), 8f, "#757575");
                    }
                });

                if (frames.Count > listed.Count)
                    PutText(col.Item().PaddingTop(2), $"另有 {frames.Count - listed.Count} 条未列出，逐帧数据看同名标红表。",
                        7.5f, "#9E9E9E");
            });
        }

        private static AccelRollChartModel BuildChartModel(List<Segment> segments)
            => new()
            {
                Series = segments.Select(s => new AccelRollLineSeries
                {
                    Name = s.Tag,
                    Color = s.Color,
                    Points = Decimate(s.Frames)
                }).ToList()
            };

        /// <summary>按点数折到上限：每桶留 Roll 最小与最大两点（按时刻先后排），红点各按自己那一帧的判定标</summary>
        private static List<AccelRollSample> Decimate(List<Frame> frames)
        {
            if (frames.Count <= ChartPointsPerSegment)
                return frames.Select(f => new AccelRollSample(f.AtSeconds, f.Roll, f.Exceeded)).ToList();

            var bucketSize = (int)Math.Ceiling(frames.Count / (double)ChartPointsPerSegment);
            var kept = new List<AccelRollSample>(bucketSize * 2);
            for (var i = 0; i < frames.Count; i += bucketSize)
            {
                var last = Math.Min(i + bucketSize, frames.Count);
                var iMin = i;
                var iMax = i;
                for (var j = i + 1; j < last; j++)
                {
                    if (frames[j].Roll < frames[iMin].Roll) iMin = j;
                    if (frames[j].Roll > frames[iMax].Roll) iMax = j;
                }

                // 桶里有没有超限要各看各的极值帧：整桶共用一个标记会把合格帧点红，也会把没当极值的真超限帧藏掉
                if (iMax < iMin) (iMin, iMax) = (iMax, iMin);
                kept.Add(new AccelRollSample(frames[iMin].AtSeconds, frames[iMin].Roll, frames[iMin].Exceeded));
                if (iMax != iMin) kept.Add(new AccelRollSample(frames[iMax].AtSeconds, frames[iMax].Roll, frames[iMax].Exceeded));
            }

            return kept;
        }

        /// <summary>判定口径一句话：轴与容差按出报告时的界面设置，名义角是每档示教进来的（文件里的偏差列就是当时算的）</summary>
        private static string JudgementCaption(AccelReportMeta meta)
            => meta.CompareAxis switch
            {
                AccelAngleJudge.AxisBoth => $"Roll 与 Pitch 各自比本档名义角，任一相差超过 {meta.ToleranceDegrees:0.#}° 记超限",
                _ => $"只比 {meta.CompareAxis} 与本档名义角，相差超过 {meta.ToleranceDegrees:0.#}° 记超限"
            };

        private static string Clock(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1
                ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }

        private static string Dash(string? text) => string.IsNullOrEmpty(text) ? "—" : text;

        private static string NumberText(double? value)
            => value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) : "—";

        // ==================== 版式小件（与整机报告同一档口径） ====================

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

        private static IContainer NewCell(TableDescriptor table, bool right = false, bool middle = false)
        {
            var cell = (IContainer)table.Cell();
            cell = cell.BorderBottom(0.5f).BorderColor("#EEEEEE").PaddingVertical(3).PaddingHorizontal(4);
            if (right) cell = cell.AlignRight();
            if (middle) cell = cell.AlignMiddle();
            return cell;
        }

        private static void HeadCell(TableCellDescriptor table, string text, bool right = false)
        {
            var cell = (IContainer)table.Cell();
            if (right) cell = cell.AlignRight();
            cell = cell.Background("#ECEFF1").Padding(4);
            PutText(cell, text, 8f, "#455A64", bold: true);
        }

        /// <summary>页头徽章：合格只在一帧没超且没有未比对时才给；未比对单列一个色，免得没示教的整档被读成全部合格</summary>
        private static void VerdictChip(IContainer cell, int exceededFrames, int notJudgedFrames, int totalFrames)
        {
            var (bg, fg, text) = totalFrames == 0 ? ("#ECEFF1", "#607D8B", "无数据")
                : exceededFrames > 0 ? ("#FFEBEE", "#C62828", $"超限 {exceededFrames} 帧")
                : notJudgedFrames > 0 ? ("#FFF3E0", "#EF6C00", $"未比对 {notJudgedFrames} 帧")
                : ("#E8F5E9", "#2E7D32", "全部合格");
            var styled = cell.Background(bg).PaddingHorizontal(10).PaddingVertical(5).AlignCenter().MinWidth(90);
            PutText(styled, text, 13.5f, fg, bold: true);
        }
    }
}
