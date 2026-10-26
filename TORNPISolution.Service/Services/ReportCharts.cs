using System.Globalization;
using System.Text;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>重复性折线图的一条数据系列</summary>
    public sealed class RepeatSeries
    {
        /// <summary>系列名（收缩压/舒张压/心率）</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>系列色（HEX）</summary>
        public string Color { get; init; } = "#1E88E5";

        /// <summary>单位（mmHg/BPM）</summary>
        public string Unit { get; init; } = string.Empty;

        /// <summary>本系列的设定值（虚线参考与容差带中心）——收缩压/舒张压各自的设定不同，必须按系列给不能共用</summary>
        public double Target { get; init; }

        /// <summary>本系列的容差（±，绿色容差带）</summary>
        public double Tolerance { get; init; }

        /// <summary>逐轮值（null = 该轮无有效回读，折线断开不前移）</summary>
        public List<double?> Values { get; init; } = new();

        /// <summary>逐轮判定（超容差为 false，红点点出）</summary>
        public List<bool> Pass { get; init; } = new();
    }

    /// <summary>重复性折线图模型：X=轮次，Y=回读值；设定虚线与容差带按系列各画各的（A06 一屏两条线两个带）</summary>
    public sealed class RepeatChartModel
    {
        /// <summary>X 轴标签（第1轮/第2轮…，下标与轮次严格对应）</summary>
        public List<string> XLabels { get; init; } = new();

        public List<RepeatSeries> Series { get; init; } = new();

        /// <summary>单位</summary>
        public string Unit { get; init; } = string.Empty;
    }

    /// <summary>
    /// 报表图表（SVG 矢量，QuestPDF 原生渲染，无位图依赖）：重复性折线图 <see cref="BuildRepeatLineChart"/>、
    /// 判定构成环形图 <see cref="BuildVerdictDoughnut"/>、模块通过率横条 <see cref="BuildModuleRateBars"/>、按天趋势图 <see cref="BuildTrendChart"/>。
    /// </summary>
    public static class ReportCharts
    {
        private const int ChartWidth = 300;
        private const int ChartHeight = 196;
        private const int PlotLeft = 34;
        private const int PlotRight = ChartWidth - 8;
        private const int PlotTop = 18;
        private const int PlotBottom = ChartHeight - 24;
        private const string Font = "Microsoft YaHei";

        /// <summary>重复性折线图：每个压力组/心率点一个面板（A06 双系列、A21 单系列）</summary>
        public static string BuildRepeatLineChart(RepeatChartModel model)
        {
            var values = model.Series.SelectMany(s => s.Values).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            // 每个系列各有设定与容差（A06 一屏收缩/舒张两个带）：视野范围必须把全部带边算进来，
            // 否则舒张压那条 80±2 的带会被裁到轴外，图上看着"全线超出绿带"而库里判的是 PASS
            var bandEdges = model.Series.SelectMany(s => new[] { s.Target - s.Tolerance, s.Target + s.Tolerance }).ToList();
            var lo = Math.Min(values.Count > 0 ? values.Min() : bandEdges.Min(), bandEdges.Min());
            var hi = Math.Max(values.Count > 0 ? values.Max() : bandEdges.Max(), bandEdges.Max());
            var pad = Math.Max((hi - lo) * 0.15, 0.5);
            lo -= pad;
            hi += pad;

            // 刻度取整到"好看"的步长（1/2/2.5/5 ×10^k）：避免 115.6、104.4 这种毛刺刻度；
            // 只放宽不放窄，容差带与设定线一定还在视野内
            var step = NiceStep((hi - lo) / 4);
            lo = Math.Floor(lo / step) * step;
            hi = Math.Ceiling(hi / step) * step;

            float X(int i) => PlotLeft + (PlotRight - PlotLeft) *
                (model.XLabels.Count <= 1 ? 0.5f : i / (float)(model.XLabels.Count - 1));
            float Y(double v) => PlotBottom - (PlotBottom - PlotTop) * (float)((v - lo) / (hi - lo));

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{ChartWidth}\" height=\"{ChartHeight}\" " +
                      $"viewBox=\"0 0 {ChartWidth} {ChartHeight}\" font-family=\"{Font}\">");
            sb.Append($"<rect width=\"{ChartWidth}\" height=\"{ChartHeight}\" fill=\"#FFFFFF\"/>");

            // 容差带（浅绿底 + 上下沿虚线）：按系列各画一条，收缩压在 118~122、舒张压在 78~82，两带不重叠
            foreach (var s in model.Series)
            {
                var bandTop = Y(s.Target + s.Tolerance);
                var bandBottom = Y(s.Target - s.Tolerance);
                sb.Append($"<rect x=\"{PlotLeft}\" y=\"{F(bandTop)}\" width=\"{PlotRight - PlotLeft}\" " +
                          $"height=\"{F(Math.Max(1f, bandBottom - bandTop))}\" fill=\"#E8F5E9\" opacity=\"0.75\"/>");
                foreach (var by in new[] { bandTop, bandBottom })
                    sb.Append($"<line x1=\"{PlotLeft}\" y1=\"{F(by)}\" x2=\"{PlotRight}\" y2=\"{F(by)}\" " +
                              $"stroke=\"#A5D6A7\" stroke-width=\"1\" stroke-dasharray=\"3,3\"/>");
            }

            // Y 网格与刻度
            for (var t = 0; t <= 4; t++)
            {
                var v = lo + (hi - lo) * t / 4;
                var y = Y(v);
                sb.Append($"<line x1=\"{PlotLeft}\" y1=\"{F(y)}\" x2=\"{PlotRight}\" y2=\"{F(y)}\" stroke=\"#EEEEEE\"/>");
                sb.Append($"<text x=\"{PlotLeft - 4}\" y=\"{F(y + 3)}\" font-size=\"8\" fill=\"#999999\" text-anchor=\"end\">{V(v)}</text>");
            }

            // X 轴与轮次标签
            sb.Append($"<line x1=\"{PlotLeft}\" y1=\"{PlotBottom}\" x2=\"{PlotRight}\" y2=\"{PlotBottom}\" stroke=\"#CCCCCC\"/>");
            for (var i = 0; i < model.XLabels.Count; i++)
                sb.Append($"<text x=\"{F(X(i))}\" y=\"{PlotBottom + 14}\" font-size=\"8.5\" fill=\"#666666\" " +
                          $"text-anchor=\"middle\">{model.XLabels[i]}</text>");

            // 设定值虚线 + 右端标注：用各自系列的颜色，两条设定线才不会看着像同一条
            foreach (var s in model.Series)
            {
                var ty = Y(s.Target);
                sb.Append($"<line x1=\"{PlotLeft}\" y1=\"{F(ty)}\" x2=\"{PlotRight}\" y2=\"{F(ty)}\" " +
                          $"stroke=\"{s.Color}\" stroke-width=\"1.2\" stroke-dasharray=\"6,4\"/>");
                sb.Append($"<text x=\"{PlotRight - 2}\" y=\"{F(ty - 4)}\" font-size=\"8\" fill=\"{s.Color}\" " +
                          $"text-anchor=\"end\">设定 {V(s.Target)}</text>");
            }

            // 数据系列：缺失轮次断线（绝不把后续轮次前移，否则"第N轮"全部错位）
            for (var si = 0; si < model.Series.Count; si++)
            {
                var s = model.Series[si];
                var path = new StringBuilder();
                var pen = false;
                for (var i = 0; i < s.Values.Count; i++)
                {
                    if (s.Values[i] is not { } v) { pen = false; continue; }
                    path.Append(pen ? " L " : " M ").Append($"{F(X(i))},{F(Y(v))}");
                    pen = true;
                }
                if (path.Length > 0)
                    sb.Append($"<path d=\"{path}\" fill=\"none\" stroke=\"{s.Color}\" stroke-width=\"2\" " +
                              $"stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");

                for (var i = 0; i < s.Values.Count; i++)
                {
                    if (s.Values[i] is not { } v) continue;
                    var pass = s.Pass.Count > i && s.Pass[i];
                    sb.Append($"<circle cx=\"{F(X(i))}\" cy=\"{F(Y(v))}\" r=\"3.2\" " +
                              $"fill=\"{(pass ? "#FFFFFF" : "#E53935")}\" stroke=\"{s.Color}\" stroke-width=\"1.6\"/>");
                    // 数值标注：第一系列标点上、其余标点下，避免两系列贴太近时重叠
                    var dy = si == 0 ? -7 : 12;
                    sb.Append($"<text x=\"{F(X(i))}\" y=\"{F(Y(v) + dy)}\" font-size=\"7.5\" fill=\"#444444\" " +
                              $"text-anchor=\"middle\">{v.ToString("0.0", CultureInfo.InvariantCulture)}</text>");
                }

                // 图例：左上角横排，每系列固定槽位（中文按全角估算宽度）
                var legendX = PlotLeft + 4 + si * 118;
                sb.Append($"<line x1=\"{legendX}\" y1=\"{PlotTop - 6}\" x2=\"{legendX + 14}\" y2=\"{PlotTop - 6}\" " +
                          $"stroke=\"{s.Color}\" stroke-width=\"2.5\"/>");
                sb.Append($"<text x=\"{legendX + 18}\" y=\"{PlotTop - 3}\" font-size=\"8\" fill=\"#444444\">{s.Name}</text>");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        /// <summary>判定构成环形图：PASS/FAIL 轮次占比</summary>
        public static string BuildVerdictDoughnut(int pass, int fail)
        {
            const int w = 118;
            const int h = 140;
            const int cx = 59;
            const int cy = 54;
            const int r = 40;
            const int sw = 14;
            var total = pass + fail;
            var circumference = 2 * Math.PI * r;
            var passLen = total == 0 ? 0 : circumference * pass / total;
            var failLen = total == 0 ? 0 : circumference * fail / total;

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"{h}\" " +
                      $"viewBox=\"0 0 {w} {h}\" font-family=\"{Font}\">");
            sb.Append($"<rect width=\"{w}\" height=\"{h}\" fill=\"#FFFFFF\"/>");
            sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r}\" fill=\"none\" stroke=\"#EBEBEB\" stroke-width=\"{sw}\"/>");
            if (passLen > 0)
                sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r}\" fill=\"none\" stroke=\"#4CAF50\" stroke-width=\"{sw}\" " +
                          $"stroke-dasharray=\"{F2(passLen)} {F2(circumference)}\" transform=\"rotate(-90 {cx} {cy})\"/>");
            if (failLen > 0)
                sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r}\" fill=\"none\" stroke=\"#E53935\" stroke-width=\"{sw}\" " +
                          $"stroke-dasharray=\"{F2(failLen)} {F2(circumference)}\" stroke-dashoffset=\"{F2(-passLen)}\" " +
                          $"transform=\"rotate(-90 {cx} {cy})\"/>");
            sb.Append($"<text x=\"{cx}\" y=\"{cy + 1}\" font-size=\"19\" font-weight=\"bold\" fill=\"#333333\" " +
                      $"text-anchor=\"middle\">{pass}/{total}</text>");
            sb.Append($"<text x=\"{cx}\" y=\"{cy + 15}\" font-size=\"8\" fill=\"#999999\" text-anchor=\"middle\">PASS 轮次</text>");
            sb.Append($"<rect x=\"16\" y=\"{h - 24}\" width=\"9\" height=\"9\" fill=\"#4CAF50\"/>");
            sb.Append($"<text x=\"29\" y=\"{h - 16}\" font-size=\"8.5\" fill=\"#444444\">PASS {pass} 轮</text>");
            sb.Append($"<rect x=\"68\" y=\"{h - 24}\" width=\"9\" height=\"9\" fill=\"#E53935\"/>");
            sb.Append($"<text x=\"81\" y=\"{h - 16}\" font-size=\"8.5\" fill=\"#444444\">FAIL {fail} 轮</text>");
            sb.Append("</svg>");
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string F2(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string V(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>SVG 文本转义（模块名/项名来自配置，稳妥起见转一遍）</summary>
        private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        /// <summary>中文按全角估算截断（图表标签位窄，模块名超宽就截断加…）</summary>
        private static string Truncate(string s, int maxChars)
            => s.Length <= maxChars ? s : s[..maxChars] + "…";

        /// <summary>好看步长：把粗略步长向上取到 1/2/2.5/5 ×10^k</summary>
        private static double NiceStep(double raw)
        {
            if (raw <= 0) return 1;
            var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            var norm = raw / mag;
            var nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10;
            return nice * mag;
        }

        // ==================== 区间统计图（第 2 页） ====================

        /// <summary>模块通过率堆叠横条：每模块一行，绿=通过 / 红=失败 / 灰=未测，条末标通过率（通过/(通过+失败)）</summary>
        public static string BuildModuleRateBars(List<ModuleStat> modules)
        {
            const int rowH = 21;
            const int labelW = 96;
            const int barLeft = labelW + 6;
            const int barMaxW = 150;
            const int w = barLeft + barMaxW + 46;
            // 有轮次但没有参与判定的项（整轮都是演示/只展示项）时集合为空，Max() 会抛——与趋势图同样出一句占位
            if (modules.Count == 0)
            {
                return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"24\" " +
                       $"viewBox=\"0 0 {w} 24\" font-family=\"{Font}\">" +
                       $"<rect width=\"{w}\" height=\"24\" fill=\"#FFFFFF\"/>" +
                       $"<text x=\"{w / 2}\" y=\"14\" font-size=\"9\" fill=\"#9E9E9E\" text-anchor=\"middle\">区间内没有可统计的测试项</text></svg>";
            }

            var h = 6 + modules.Count * rowH + 4;
            var maxTotal = modules.Max(m => m.PassCount + m.FailCount + m.NotTestedCount);
            if (maxTotal <= 0) maxTotal = 1;

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"{h}\" " +
                      $"viewBox=\"0 0 {w} {h}\" font-family=\"{Font}\">");
            sb.Append($"<rect width=\"{w}\" height=\"{h}\" fill=\"#FFFFFF\"/>");

            for (var i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                var y = 6 + i * rowH;
                var total = m.PassCount + m.FailCount + m.NotTestedCount;
                var scale = barMaxW / (double)maxTotal;

                sb.Append($"<text x=\"{labelW}\" y=\"{F(y + 13)}\" font-size=\"8\" fill=\"#444444\" " +
                          $"text-anchor=\"end\">{Esc(Truncate($"{m.ModuleId} {m.ModuleName}", 12))}</text>");

                var x = (double)barLeft;
                foreach (var (count, color) in new[] { (m.PassCount, "#66BB6A"), (m.FailCount, "#EF5350"), (m.NotTestedCount, "#CFD8DC") })
                {
                    if (count <= 0) continue;
                    var segW = Math.Max(1.5, count * scale);
                    sb.Append($"<rect x=\"{F2(x)}\" y=\"{F(y + 3)}\" width=\"{F2(segW)}\" height=\"12\" fill=\"{color}\"/>");
                    x += segW;
                }
                // 条内计数标注（段够宽才写，避免糊成一团）
                if (m.PassCount > 0 && m.PassCount * scale > 14)
                    sb.Append($"<text x=\"{F2(barLeft + m.PassCount * scale / 2)}\" y=\"{F2(y + 12.5)}\" font-size=\"7.5\" " +
                              $"fill=\"#FFFFFF\" text-anchor=\"middle\">{m.PassCount}</text>");
                if (m.FailCount > 0 && m.FailCount * scale > 14)
                {
                    var failCenter = barLeft + (m.PassCount + m.FailCount / 2.0) * scale;
                    sb.Append($"<text x=\"{F2(failCenter)}\" y=\"{F2(y + 12.5)}\" font-size=\"7.5\" " +
                              $"fill=\"#FFFFFF\" text-anchor=\"middle\">{m.FailCount}</text>");
                }
                sb.Append($"<text x=\"{F2(barLeft + total * scale + 5)}\" y=\"{F(y + 13)}\" font-size=\"8.5\" " +
                          $"font-weight=\"bold\" fill=\"#2D3748\">{Esc(m.PassRateText)}</text>");
            }

            // 图例
            var legendY = h - 2;
            sb.Append($"<rect x=\"{barLeft}\" y=\"{legendY - 7}\" width=\"8\" height=\"8\" fill=\"#66BB6A\"/>");
            sb.Append($"<text x=\"{barLeft + 11}\" y=\"{legendY}\" font-size=\"7.5\" fill=\"#666666\">通过</text>");
            sb.Append($"<rect x=\"{barLeft + 40}\" y=\"{legendY - 7}\" width=\"8\" height=\"8\" fill=\"#EF5350\"/>");
            sb.Append($"<text x=\"{barLeft + 51}\" y=\"{legendY}\" font-size=\"7.5\" fill=\"#666666\">失败</text>");
            sb.Append($"<rect x=\"{barLeft + 80}\" y=\"{legendY - 7}\" width=\"8\" height=\"8\" fill=\"#CFD8DC\"/>");
            sb.Append($"<text x=\"{barLeft + 91}\" y=\"{legendY}\" font-size=\"7.5\" fill=\"#666666\">未测</text>");
            sb.Append("</svg>");
            return sb.ToString();
        }

        /// <summary>趋势图：按天堆叠柱（通过/失败，左轴=轮次）+ 通过率折线（右轴 0~100%）</summary>
        public static string BuildTrendChart(List<TrendPoint> trend)
        {
            const int w = 300;
            const int h = 168;
            const int plotLeft = 30;
            const int plotRight = w - 30;
            const int plotTop = 20;
            const int plotBottom = h - 26;
            if (trend.Count == 0)
            {
                return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"{h}\" " +
                       $"viewBox=\"0 0 {w} {h}\" font-family=\"{Font}\">" +
                       $"<rect width=\"{w}\" height=\"{h}\" fill=\"#FFFFFF\"/>" +
                       $"<text x=\"{w / 2}\" y=\"{h / 2}\" font-size=\"9\" fill=\"#999999\" text-anchor=\"middle\">区间内没有数据</text></svg>";
            }

            // 左轴按"每日通过+失败项数"的峰值取整（不是轮次数——柱子堆的是项数，轴跟不上就会溢出成负坐标，
            // Skia 会按内容边界把整张图拉伸到全页，版式全毁）
            var maxStack = trend.Max(t => t.PassCount + t.FailCount);
            if (maxStack <= 0) maxStack = 1;
            var runStep = NiceStep(maxStack / 3.0);
            // 左轴数的是项数，刻度必须落在整数上（NiceStep 会给到 2.5/0.5 这类小数）
            runStep = Math.Max(1, Math.Ceiling(runStep));
            var runTop = Math.Ceiling(maxStack / runStep) * runStep;

            float X(int i) => plotLeft + (plotRight - plotLeft) *
                (trend.Count <= 1 ? 0.5f : i / (float)(trend.Count - 1));
            float YRuns(int v) => plotBottom - (plotBottom - plotTop) * (v / (float)runTop);
            float YRate(double r) => plotBottom - (plotBottom - plotTop) * (float)r;

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"{h}\" " +
                      $"viewBox=\"0 0 {w} {h}\" font-family=\"{Font}\">");
            sb.Append($"<rect width=\"{w}\" height=\"{h}\" fill=\"#FFFFFF\"/>");

            // 左轴（项数）网格按 runStep 的整数倍铺：线位与标签同源，按三等分取整会让两者错位、顶线跑到绘图区外
            var runTicks = (int)Math.Round(runTop / runStep);
            for (var t = 0; t <= runTicks; t++)
            {
                var v = (int)Math.Round(runStep * t);
                var y = YRuns(v);
                sb.Append($"<line x1=\"{plotLeft}\" y1=\"{F(y)}\" x2=\"{plotRight}\" y2=\"{F(y)}\" stroke=\"#EEEEEE\"/>");
                sb.Append($"<text x=\"{plotLeft - 4}\" y=\"{F(y + 3)}\" font-size=\"7.5\" fill=\"#999999\" " +
                          $"text-anchor=\"end\">{V(v)}</text>");
            }

            // 右轴（通过率）刻度独立按 0~100% 四档铺，不跟着左轴档数走
            for (var t = 0; t <= 3; t++)
            {
                var r = t / 3.0;
                sb.Append($"<text x=\"{w - 1}\" y=\"{F(YRate(r) + 3)}\" font-size=\"7.5\" fill=\"#B39DDB\" " +
                          $"text-anchor=\"end\">{(int)(r * 100)}%</text>");
            }

            // 每日堆叠柱：绿=通过 红=失败（柱宽随天数收缩，至少 4px）
            var slot = (plotRight - plotLeft) / (float)trend.Count;
            var barW = Math.Max(4f, Math.Min(16f, slot * 0.6f));
            for (var i = 0; i < trend.Count; i++)
            {
                var t = trend[i];
                var cx = X(i);
                var passH = (plotBottom - plotTop) * (t.PassCount / (float)runTop);
                var failH = (plotBottom - plotTop) * (t.FailCount / (float)runTop);
                var x = cx - barW / 2;
                if (t.PassCount > 0)
                    sb.Append($"<rect x=\"{F(x)}\" y=\"{F(plotBottom - passH)}\" width=\"{F(barW)}\" " +
                              $"height=\"{F(passH)}\" fill=\"#66BB6A\"/>");
                if (t.FailCount > 0)
                    sb.Append($"<rect x=\"{F(x)}\" y=\"{F(plotBottom - passH - failH)}\" width=\"{F(barW)}\" " +
                              $"height=\"{F(failH)}\" fill=\"#EF5350\"/>");
            }

            // X 轴日期标签（天数多时间隔显示，避免叠字）
            var labelEvery = trend.Count > 16 ? (int)Math.Ceiling(trend.Count / 16.0) : 1;
            sb.Append($"<line x1=\"{plotLeft}\" y1=\"{plotBottom}\" x2=\"{plotRight}\" y2=\"{plotBottom}\" stroke=\"#CCCCCC\"/>");
            for (var i = 0; i < trend.Count; i++)
            {
                if (i % labelEvery != 0 && i != trend.Count - 1) continue;
                sb.Append($"<text x=\"{F(X(i))}\" y=\"{plotBottom + 12}\" font-size=\"7.5\" fill=\"#666666\" " +
                          $"text-anchor=\"middle\">{trend[i].DateText}</text>");
            }

            // 通过率折线 + 数据点（有点才画；全零通过率也是有效点）
            var path = new StringBuilder();
            for (var i = 0; i < trend.Count; i++)
            {
                path.Append(i == 0 ? " M " : " L ").Append($"{F(X(i))},{F(YRate(trend[i].PassRate))}");
            }
            sb.Append($"<path d=\"{path}\" fill=\"none\" stroke=\"#7E57C2\" stroke-width=\"2\" " +
                      $"stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
            for (var i = 0; i < trend.Count; i++)
            {
                sb.Append($"<circle cx=\"{F(X(i))}\" cy=\"{F(YRate(trend[i].PassRate))}\" r=\"2.8\" " +
                          $"fill=\"#FFFFFF\" stroke=\"#7E57C2\" stroke-width=\"1.5\"/>");
                if (trend.Count <= 10)
                    sb.Append($"<text x=\"{F(X(i))}\" y=\"{F(YRate(trend[i].PassRate) - 6)}\" font-size=\"7\" " +
                              $"fill=\"#7E57C2\" text-anchor=\"middle\">{trend[i].PassRateText}</text>");
            }

            // 图例
            var legendY = plotTop - 8;
            sb.Append($"<rect x=\"{plotLeft}\" y=\"{legendY - 7}\" width=\"8\" height=\"8\" fill=\"#66BB6A\"/>");
            sb.Append($"<text x=\"{plotLeft + 11}\" y=\"{legendY}\" font-size=\"7.5\" fill=\"#666666\">每日通过/失败项数（左轴）</text>");
            sb.Append($"<line x1=\"{plotLeft + 150}\" y1=\"{legendY - 3}\" x2=\"{plotLeft + 164}\" y2=\"{legendY - 3}\" " +
                      $"stroke=\"#7E57C2\" stroke-width=\"2\"/>");
            sb.Append($"<text x=\"{plotLeft + 168}\" y=\"{legendY}\" font-size=\"7.5\" fill=\"#666666\">通过率（右轴）</text>");
            sb.Append("</svg>");
            return sb.ToString();
        }
    }
}
