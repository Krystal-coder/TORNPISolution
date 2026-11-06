using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using TORNPISolution.Interface;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 把采样 CSV 转成带样式的同名 xlsx：每列的最大/最小值在数据行里标红，段末的平均/最大/最小加粗，
    /// 另开一页「Roll 曲线」放每段的时刻与 Roll（界面那条曲线的底数），并给这一页补挂一张原生散点折线图。
    /// 逐帧 CSV 只是中转原始记录，标红版与 PDF 报告都从它另出，标红版落定由服务层删 CSV；转换失败只记日志，中转 CSV 保留。
    /// </summary>
    public static class AccelCheckExcelWriter
    {
        private static readonly ILogService Log = new LogService(nameof(AccelCheckExcelWriter));

        /// <summary>极值标红（与采样表超限行同一档红）</summary>
        private static readonly XLColor Extreme = XLColor.FromHtml("#C62828");

        /// <summary>按列头给宽：默认 10 个字符装不下"19:43:15.339"，会被裁成一半</summary>
        private static readonly Dictionary<string, double> Widths = new()
        {
            ["序号"] = 6,
            ["时间"] = 13,
            ["设置角度"] = 14,
            ["比对角(°)"] = 11,
            ["超判定"] = 8,
        };

        /// <returns>生成的 xlsx 全路径；没有可写的内容或失败时为 null</returns>
        public static string? WriteFromCsv(string csvPath)
        {
            // 临时名也得是 Excel 认的扩展名（ClosedXML 按后缀拒绝 .tmp），改名后才是正式那份
            var temp = Path.ChangeExtension(csvPath, ".xlsx") + ".tmp.xlsx";
            try
            {
                var blocks = ReadBlocks(csvPath);
                // 一段实测数据都没有就别配一份空壳：只留 CSV
                if (blocks.Count == 0 || blocks.All(b => b.Rows.Count == 0)) return null;

                var path = Path.ChangeExtension(csvPath, ".xlsx");
                using (var book = new XLWorkbook())
                {
                    var sheet = book.Worksheets.Add(SheetName(csvPath));

                    // 带小数的列统一按两位小数显示，与 CSV 里那套 F2 文案对齐
                    var decimalColumns = new HashSet<int>();
                    var row = 1;
                    foreach (var block in blocks)
                        row = WriteBlock(sheet, row, block, decimalColumns);

                    foreach (var column in decimalColumns)
                        sheet.Column(column).Style.NumberFormat.SetFormat("0.00");

                    // Excel 默认文本靠左、数字靠右，一份表里两种对齐看着就像没对齐：内容整体横竖都居中
                    var used = sheet.RangeUsed();
                    if (used != null)
                    {
                        used.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        used.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    }

                    WriteCurveSheet(book, blocks);

                    // 退出时这份生成可能被 5 秒收尾预算截断：先落临时名再改名，宁可少一个文件也不要半个
                    book.SaveAs(temp);
                }

                EmbedRollCurveChart(temp, blocks);

                File.Move(temp, path, true);

                Log.Info($"加速度采样标红版已生成：{path}");
                return path;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* 临时名删不掉只是多个残file，不影响 CSV */ }
                Log.Warn($"加速度采样 xlsx 生成失败（{csvPath}）：{ex.GetType().Name} {ex.Message}");
                return null;
            }
        }

        // ==================== 读 CSV ====================

        /// <summary>一段（一个姿态标签）的 CSV 内容，按落盘时的文本原样拿着（PDF 报告也吃这份解析，不再各写一套读法）</summary>
        internal sealed class Block
        {
            public string Title = string.Empty;
            public string[] Header = Array.Empty<string>();
            public readonly List<string[]> Rows = new();
            public string[]? Average;
            public string[]? Max;
            public string[]? Min;

            /// <summary>本段的列下标：按列名认，老文件少了哪一列也只是取不到，不会读错格</summary>
            public int Column(string name) => Array.IndexOf(Header, name);

            /// <summary>这一行按列名取值，列不在或这行短于列数就给空串（正在写的半行也走这里）</summary>
            public string Cell(string[] cells, string name)
            {
                var index = Column(name);
                return index >= 0 && index < cells.Length ? cells[index] : string.Empty;
            }

            /// <summary>段标签：段头「# 段 N：0°平放（起始 …）」里那一段，认不出来退到首行的「设置角度」</summary>
            public string Tag
            {
                get
                {
                    var from = Title.IndexOf('：') + 1;
                    if (from > 0)
                    {
                        var to = Title.IndexOf("（起始", from, StringComparison.Ordinal);
                        var tag = to > from ? Title[from..to].Trim() : Title[from..].Trim();
                        if (tag.Length > 0) return tag;
                    }

                    foreach (var cells in Rows)
                    {
                        var tag = Cell(cells, "设置角度");
                        if (!string.IsNullOrEmpty(tag)) return tag;
                    }

                    return "未命名";
                }
            }
        }

        /// <summary>
        /// 读一份采样文件的分段内容。保存途中也要能读（选报告格式时按当前采样补导一份），
        /// 而写侧的句柄只让并发读、不让自己被读到——读侧必须把 Write 回让出去才打得开，所以不能直接用 File.ReadLines。
        /// 逐行落盘且每行 Flush，读到末尾可能是半行：列数不齐的一律丢掉，免得残缺行混进统计与曲线。
        /// </summary>
        internal static List<Block> ReadBlocks(string csvPath)
        {
            var blocks = new List<Block>();
            Block? block = null;

            using var io = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(io, Encoding.UTF8, true);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;

                if (line.StartsWith("# 段", StringComparison.Ordinal))
                {
                    block = new Block { Title = Unquote(line) };
                    blocks.Add(block);
                    continue;
                }

                if (block == null) continue;

                var cells = SplitLine(line);
                switch (cells[0])
                {
                    case "序号":
                        block.Header = cells;
                        break;
                    case "平均":
                        block.Average = cells;
                        break;
                    case "最大":
                        block.Max = cells;
                        break;
                    case "最小":
                        block.Min = cells;
                        break;
                    default:
                        if (block.Header.Length > 0 && cells.Length < block.Header.Length) break;
                        block.Rows.Add(cells);
                        break;
                }
            }

            return blocks;
        }

        // ==================== 写 xlsx ====================

        private static int WriteBlock(IXLWorksheet sheet, int startRow, Block block, HashSet<int> decimalColumns)
        {
            var row = startRow;
            sheet.Cell(row++, 1).Value = block.Title;

            for (var c = 0; c < block.Header.Length; c++)
            {
                var cell = sheet.Cell(row, c + 1);
                cell.Value = block.Header[c];
                cell.Style.Font.SetBold();
                if (Widths.TryGetValue(block.Header[c], out var width))
                    sheet.Column(c + 1).Width = width;
            }
            row++;

            var firstDataRow = row;
            foreach (var cells in block.Rows)
            {
                for (var c = 0; c < cells.Length; c++)
                    if (PutCell(sheet.Cell(row, c + 1), cells[c]))
                        decimalColumns.Add(c + 1);
                row++;
            }

            // 极值按数值认（CSV 里整数列写 300、统计行写 300.00，比字符串会整列漏标），口径仍是落盘的那两位小数
            MarkExtremes(sheet, firstDataRow, block);

            foreach (var stat in new[] { block.Average, block.Max, block.Min })
            {
                if (stat == null) continue;
                for (var c = 0; c < stat.Length; c++)
                {
                    var cell = sheet.Cell(row, c + 1);
                    PutCell(cell, stat[c]);
                    cell.Style.Font.SetBold();
                }
                row++;
            }

            return row + 1;
        }

        private const string CurveSheetName = "Roll 曲线";

        /// <summary>
        /// 另开一页「Roll 曲线」：每段自占两列（时刻 + 该段 Roll），段与段并列——同名标签来回切过的多次区间各占一份，
        /// 拼起来就是界面那条曲线的底数。时刻写成真时间值（不是文本），曲线才排得出先后。
        /// 原生折线图在文件落定后由 <see cref="EmbedRollCurveChart"/> 补挂到本页。
        /// </summary>
        private static void WriteCurveSheet(XLWorkbook book, List<Block> blocks)
        {
            if (!blocks.Any(b => b.Rows.Count > 0 && b.Column("时间") >= 0 && b.Column("Roll(°)") >= 0)) return;

            var sheet = book.Worksheets.Add(CurveSheetName);
            var column = 1;
            var ordinal = 0;
            foreach (var block in blocks)
            {
                var cTime = block.Column("时间");
                var cRoll = block.Column("Roll(°)");
                if (cTime < 0 || cRoll < 0 || block.Rows.Count == 0) continue;

                ordinal++;
                sheet.Cell(1, column + 1).Value = $"段 {ordinal}：{block.Tag}";
                sheet.Cell(2, column).Value = "时间";
                sheet.Cell(2, column + 1).Value = "Roll(°)";
                sheet.Cell(1, column + 1).Style.Font.SetBold();
                sheet.Cell(2, column).Style.Font.SetBold();
                sheet.Cell(2, column + 1).Style.Font.SetBold();

                var row = 3;
                foreach (var cells in block.Rows)
                {
                    if (cRoll >= cells.Length) continue;
                    // Roll 解析不出来的行不占位：留一个空格会让曲线在这一格断掉
                    if (!double.TryParse(cells[cRoll], NumberStyles.Float, CultureInfo.InvariantCulture, out var roll)) continue;

                    sheet.Cell(row, column + 1).Value = roll;
                    if (cTime < cells.Length
                        && TimeSpan.TryParse(cells[cTime], CultureInfo.InvariantCulture, out var clock))
                        sheet.Cell(row, column).Value = DateTime.Today + clock;
                    row++;
                }

                sheet.Column(column).Width = 13;
                sheet.Column(column).Style.NumberFormat.SetFormat("hh:mm:ss.000");
                sheet.Column(column + 1).Width = 11;
                sheet.Column(column + 1).Style.NumberFormat.SetFormat("0.00");
                column += 2;
            }
        }

        // ==================== 原生折线图（OpenXML 直补） ====================

        /// <summary>图表的底数段：有数据且两列都在的段，段序与曲线页的列序一一对应</summary>
        private static List<Block> CurveBlocks(List<Block> blocks)
            => blocks.Where(b => b.Rows.Count > 0 && b.Column("时间") >= 0 && b.Column("Roll(°)") >= 0).ToList();

        /// <summary>
        /// 给「Roll 曲线」页补挂一张原生散点折线图：一段一条线、段名进图例，横轴用真时刻数值轴，数字底数不动。
        /// ClosedXML 0.104 的新图表引擎还没开放（XLChart 是 internal），所以文件落定后用 OpenXML 直接补图表部件；
        /// 补挂失败只记日志，数字页不受影响。
        /// </summary>
        private static void EmbedRollCurveChart(string xlsxPath, List<Block> blocks)
        {
            var segments = CurveBlocks(blocks);
            if (segments.Count == 0) return;

            try
            {
                using var doc = SpreadsheetDocument.Open(xlsxPath, true);
                var workbookPart = doc.WorkbookPart ?? throw new InvalidOperationException("缺 workbook part");
                var sheet = workbookPart.Workbook.Descendants<Sheet>().FirstOrDefault(s => s.Name == CurveSheetName);
                if (sheet?.Id?.Value is not { } sheetId || workbookPart.GetPartById(sheetId) is not WorksheetPart worksheetPart)
                    return;

                var worksheet = worksheetPart.Worksheet;
                var drawingsPart = worksheetPart.DrawingsPart ?? worksheetPart.AddNewPart<DrawingsPart>();
                if (!worksheet.Elements<Drawing>().Any())
                {
                    // 工作表子元素的顺序是 schema 钉死的：drawing 只许排在 tableParts/extLst 之前；
                    // r:id 必须照工作表部件已有的关系号写，自己编一个号 Excel 会拒开
                    var anchor = worksheet.Elements().LastOrDefault(e => e is not TableParts and not ExtensionList);
                    worksheet.InsertAfter(new Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) }, anchor);
                }

                var chartPart = drawingsPart.AddNewPart<ChartPart>();
                var chartId = drawingsPart.GetIdOfPart(chartPart);
                chartPart.ChartSpace = BuildRollChartSpace(segments);

                var drawingRoot = drawingsPart.WorksheetDrawing;
                if (drawingRoot == null)
                {
                    drawingRoot = new Xdr.WorksheetDrawing();
                    drawingsPart.WorksheetDrawing = drawingRoot;
                }

                // 图摆在数字底数右边第 3 列起：一段占两列，段数 * 2 之后留一列空当
                var column = segments.Count * 2 + 2;
                drawingRoot.Append(new Xdr.TwoCellAnchor(
                    new Xdr.FromMarker
                    {
                        ColumnId = new Xdr.ColumnId((column - 1).ToString()),
                        RowId = new Xdr.RowId("0"),
                        ColumnOffset = new Xdr.ColumnOffset("0"),
                        RowOffset = new Xdr.RowOffset("0")
                    },
                    new Xdr.ToMarker
                    {
                        ColumnId = new Xdr.ColumnId((column + 8).ToString()),
                        RowId = new Xdr.RowId("18"),
                        ColumnOffset = new Xdr.ColumnOffset("0"),
                        RowOffset = new Xdr.RowOffset("0")
                    },
                    new Xdr.GraphicFrame(
                        new Xdr.NonVisualGraphicFrameProperties(
                            new Xdr.NonVisualDrawingProperties { Id = 2U, Name = "Roll 折线图" },
                            new Xdr.NonVisualGraphicFrameDrawingProperties()),
                        // xfrm 在 graphicFrame 里是必填位：空着不填位置，Excel 自己摆
                        new Xdr.Transform(),
                        new DocumentFormat.OpenXml.Drawing.Graphic(
                            new DocumentFormat.OpenXml.Drawing.GraphicData(
                                new C.ChartReference { Id = chartId })
                            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart" })),
                    new Xdr.ClientData()));
            }
            catch (Exception ex)
            {
                Log.Warn($"Roll 曲线原生折线图补挂失败（{xlsxPath}）：{ex.GetType().Name} {ex.Message}");
            }
        }

        /// <summary>散点折线图：每段一条线，横轴是真时刻（OADate）数值轴，纵轴 Roll；图例取每段标题格</summary>
        private static C.ChartSpace BuildRollChartSpace(List<Block> segments)
        {
            var series = new List<C.ScatterChartSeries>();
            for (var i = 0; i < segments.Count; i++)
            {
                var block = segments[i];
                var cTime = block.Column("时间");
                var cRoll = block.Column("Roll(°)");
                var timeColumn = i * 2 + 1;
                var rollColumn = timeColumn + 1;

                // 曲线页的落法：行 1 段标题、行 2 列头、行 3 起数据；Roll 与时刻任一解析不出的行不占位，
                // 保证 x/y 两个缓存点数一致——少一个横坐标，Excel 会把整条线画空
                var timePoints = new List<C.NumericPoint>();
                var rollPoints = new List<C.NumericPoint>();
                var valid = 0;
                for (var r = 0; r < block.Rows.Count; r++)
                {
                    var cells = block.Rows[r];
                    if (cRoll >= cells.Length || cTime >= cells.Length) continue;
                    if (!double.TryParse(cells[cRoll], NumberStyles.Float, CultureInfo.InvariantCulture, out var roll)) continue;
                    if (!TimeSpan.TryParse(cells[cTime], CultureInfo.InvariantCulture, out var clock)) continue;

                    rollPoints.Add(new C.NumericPoint(new C.NumericValue(roll.ToString("R", CultureInfo.InvariantCulture))) { Index = (uint)valid });
                    timePoints.Add(new C.NumericPoint(
                        new C.NumericValue(((DateTime.Today + clock).ToOADate()).ToString("R", CultureInfo.InvariantCulture))) { Index = (uint)valid });
                    valid++;
                }

                if (valid == 0) continue;

                var titleCell = $"'{CurveSheetName}'!{ColumnLetters(rollColumn)}$1";
                var timeRange = $"'{CurveSheetName}'!${ColumnLetters(timeColumn)}$3:${ColumnLetters(timeColumn)}${2 + valid}";
                var rollRange = $"'{CurveSheetName}'!${ColumnLetters(rollColumn)}$3:${ColumnLetters(rollColumn)}${2 + valid}";

                var timeCache = new C.NumberingCache(new C.FormatCode("h:mm:ss"), new C.PointCount { Val = (uint)valid });
                foreach (var point in timePoints) timeCache.Append(point);
                var rollCache = new C.NumberingCache(new C.FormatCode("0.00"), new C.PointCount { Val = (uint)valid });
                foreach (var point in rollPoints) rollCache.Append(point);

                series.Add(new C.ScatterChartSeries(
                    new C.Index { Val = (uint)i },
                    new C.Order { Val = (uint)i },
                    new C.SeriesText(new C.StringReference(
                        new C.Formula(titleCell),
                        new C.StringCache(new C.PointCount { Val = 1U },
                            new C.StringPoint { Index = 0U, NumericValue = new C.NumericValue($"段 {i + 1}：{block.Tag}") }))),
                    new C.XValues(new C.NumberReference(new C.Formula(timeRange), timeCache)),
                    new C.YValues(new C.NumberReference(new C.Formula(rollRange), rollCache)),
                    new C.Smooth { Val = false }));
            }

            var scatter = new C.ScatterChart(
                new C.ScatterStyle { Val = C.ScatterStyleValues.Line },
                new C.VaryColors { Val = false });
            foreach (var s in series) scatter.Append(s);
            scatter.Append(new C.AxisId { Val = 1U }, new C.AxisId { Val = 2U });

            return new C.ChartSpace(
                new C.Chart(
                    new C.PlotArea(
                        scatter,
                        new C.ValueAxis(
                            new C.AxisId { Val = 1U },
                            new C.Scaling(),
                            new C.Delete { Val = false },
                            new C.AxisPosition { Val = C.AxisPositionValues.Left },
                            new C.NumberingFormat { FormatCode = "0.00" },
                            new C.CrossingAxis { Val = 2U }),
                        new C.ValueAxis(
                            new C.AxisId { Val = 2U },
                            new C.Scaling(),
                            new C.Delete { Val = false },
                            new C.AxisPosition { Val = C.AxisPositionValues.Bottom },
                            new C.NumberingFormat { FormatCode = "h:mm:ss" },
                            new C.CrossingAxis { Val = 1U })),
                    new C.Legend(new C.LegendPosition { Val = C.LegendPositionValues.Bottom }),
                    new C.PlotVisibleOnly { Val = true }));
        }

        /// <summary>1 起的列号转字母（1→A、27→AA）</summary>
        private static string ColumnLetters(int oneBased)
        {
            var letters = string.Empty;
            for (var n = oneBased; n > 0; n = (n - 1) / 26)
                letters = (char)('A' + (n - 1) % 26) + letters;
            return letters;
        }

        private static void MarkExtremes(IXLWorksheet sheet, int firstDataRow, Block block)
        {
            if (block.Max == null || block.Min == null || block.Rows.Count == 0) return;

            for (var c = 0; c < block.Max.Length; c++)
            {
                // 只认数值格：段统计行的「设置角度」列写的是标签文本，按列刷会把整段都染红
                if (!IsNumber(block.Max[c]) || !IsNumber(block.Min[c])) continue;
                var max = double.Parse(block.Max[c], CultureInfo.InvariantCulture);
                var min = double.Parse(block.Min[c], CultureInfo.InvariantCulture);
                // 整段同值时最大就是最小，全列标红只是噪声
                if (max == min) continue;

                for (var r = 0; r < block.Rows.Count; r++)
                {
                    var cells = block.Rows[r];
                    if (c >= cells.Length) continue;
                    if (!IsExtreme(cells[c], max) && !IsExtreme(cells[c], min)) continue;

                    sheet.Cell(firstDataRow + r, c + 1).Style
                        .Font.SetFontColor(Extreme)
                        .Font.SetBold();
                }
            }
        }

        /// <summary>这一格是不是段统计里的极值：按 CSV 落盘的两位小数比，整数列的 300 与统计行的 300.00 算同一个数</summary>
        private static bool IsExtreme(string text, double extreme)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
               && Math.Round(value, 2) == Math.Round(extreme, 2);

        /// <returns>这一格是否按带小数的数值写进去了（用来决定这一列的显示位数）</returns>
        private static bool PutCell(IXLCell cell, string text)
        {
            // 数值按数值写，Excel 才能直接排序与再统计；带冒号的时间与带°的标签落不回数字，保持文本
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
            {
                cell.Value = whole;
                return false;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                cell.Value = number;
                return text.Contains('.');
            }

            if (text.Length > 0) cell.Value = text;
            return false;
        }

        private static bool IsNumber(string text)
            => text.Length > 0 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        private static string SheetName(string csvPath)
        {
            var name = Path.GetFileNameWithoutExtension(csvPath);
            foreach (var invalid in new[] { '[', ']', '*', '?', '/', '\\', ':' })
                name = name.Replace(invalid, '-');
            return name.Length == 0 ? "采样" : name[..Math.Min(name.Length, 31)];
        }

        private static string Unquote(string line)
            => line.StartsWith("\"", StringComparison.Ordinal) && line.EndsWith("\"", StringComparison.Ordinal)
                ? line[1..^1].Replace("\"\"", "\"")
                : line;

        /// <summary>按 CSV 规则切一行：引号里的逗号不算分隔，两个连续引号是一个引号</summary>
        private static string[] SplitLine(string line)
        {
            var fields = new List<string>();
            var text = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (quoted)
                {
                    if (ch != '"' || i + 1 < line.Length && line[i + 1] != '"')
                    {
                        if (ch == '"') quoted = false;
                        else text.Append(ch);
                    }
                    else
                    {
                        text.Append('"');
                        i++;
                    }
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',')
                {
                    fields.Add(text.ToString());
                    text.Clear();
                }
                else text.Append(ch);
            }

            fields.Add(text.ToString());
            return fields.ToArray();
        }
    }
}
