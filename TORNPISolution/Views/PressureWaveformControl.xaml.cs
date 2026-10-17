using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 多点扫描项按测试点段号分色绘制，悬停提示附测试点序号。
    /// 横轴两种定法：
    /// <see cref="PixelsPerSecond"/> 固定像素倍率（行内小图）
    /// <see cref="VisibleSeconds"/> 固定窗口秒数（ECG Signal：拉大窗口只是格子变宽，看到的时长不变）
    /// </summary>
    public partial class PressureWaveformControl : UserControl
    {
        /// <summary>固定时间刻度：像素/秒（可在 XAML 中按测试项绑定）</summary>
        public static readonly DependencyProperty PixelsPerSecondProperty = DependencyProperty.Register(
            nameof(PixelsPerSecond), typeof(double), typeof(PressureWaveformControl),
            new PropertyMetadata(24.0, OnPixelsPerSecondChanged));

        /// <summary>
        /// 横轴窗口宽度（秒）：给了这个值就按“图幅宽度 / 该值”换算像素每秒，
        /// 窗口拉大拉小都还是显示这么长时间（ECG Signal窗口用）；NaN = 用 <see cref="PixelsPerSecond"/> 固定倍率。
        /// </summary>
        public static readonly DependencyProperty VisibleSecondsProperty = DependencyProperty.Register(
            nameof(VisibleSeconds), typeof(double), typeof(PressureWaveformControl),
            new PropertyMetadata(double.NaN, OnPixelsPerSecondChanged));

        /// <summary>ECG 坐标纸样式：粉色大小格 + 左侧数值轴 + 黑色边框与轴标题（ECG Signal窗口用）</summary>
        public static readonly DependencyProperty EcgGridProperty = DependencyProperty.Register(
            nameof(EcgGrid), typeof(bool), typeof(PressureWaveformControl),
            new PropertyMetadata(false, OnGridStyleChanged));

        /// <summary>底部轴区高度（时间刻度 + 内置进度条）</summary>
        private const double AxisHeight = 22;
        /// <summary>绘图区内边距</summary>
        private const double DrawPadding = 10;
        /// <summary>ECG 坐标纸下的左侧留白（数值 + 竖排单位标题）</summary>
        private const double EcgLeftGutter = 46;
        /// <summary>ECG 坐标纸下的底部轴区高度（时间刻度 + 轴标题 + 进度条）</summary>
        private const double EcgBottomBand = 50;
        /// <summary>时间刻度候选间隔（秒）</summary>
        private static readonly double[] TickCandidates = { 1, 2, 5, 10, 15, 30, 60, 120 };
        /// <summary>坐标纸大格候选间隔（从密到疏），取“第一个不小于需要值”的档</summary>
        private static readonly double[] StepCandidates =
            { 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 2.5, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000 };

        /// <summary>
        /// 格号迭代上限，兼作浮点格号强转前的夹取值：脏数据（NaN/无穷）会让“越过右边界”永远不成立，
        /// 上下界又能溢出成 ±20 亿，两头都卡在 UI 线程上出不来。正常网格几十格，碰不到这道闸。
        /// </summary>
        private const int GridIndexLimit = 2000;

        /// <summary>坐标纸细格（浅粉）/ 大格（深粉）/ 轴与边框（近黑）</summary>
        private static readonly Brush EcgMinorBrush = FrozenBrush(Color.FromRgb(0xFA, 0xD2, 0xD2));
        private static readonly Brush EcgMajorBrush = FrozenBrush(Color.FromRgb(0xF0, 0xA8, 0xA8));
        private static readonly Brush EcgAxisBrush = FrozenBrush(Color.FromRgb(0x33, 0x33, 0x33));

        private static Brush FrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>当前数据映射缓存（Redraw 时更新，悬停换算用）</summary>
        private double _tMin, _pMin, _pRange;
        private bool _hasData;
        /// <summary>可视窗口起点相对数据起点的偏移（秒）</summary>
        private double _viewOffset;
        /// <summary>是否跟随最新数据（实时采样时自动贴右端）</summary>
        private bool _followLatest = true;

        /// <summary>拖动状态：None/绘图区平移/进度条滑块</summary>
        private DragMode _dragMode = DragMode.None;
        private double _dragStartX, _dragStartOffset;

        /// <summary>内置进度条几何（Redraw 时计算，命中检测用）</summary>
        private bool _hasScrollbar;
        private double _sbTrackX0, _sbTrackW, _sbThumbX, _sbThumbW, _sbY;

        /// <summary>滚轮缩放倍数；有效时间刻度 = PixelsPerSecond × _zoom，夹在 [2, 480] 像素/秒</summary>
        private double _zoom = 1.0;
        /// <summary>当前数据是否含多个测试点段 </summary>
        private bool _multiSegment;

        private const double MinEffectivePps = 2.0;
        private const double MaxEffectivePps = 480.0;

        /// <summary>多测试点波形配色（按段号循环取色；单段用第一色即原蓝色）</summary>
        private static readonly Brush[] SegmentBrushes = CreateSegmentBrushes();

        private static Brush[] CreateSegmentBrushes()
        {
            var colors = new[] { "#1565C0", "#E8590C", "#2F9E44", "#9C36B5", "#E03131", "#1098AD", "#F08C00", "#5C7CFA" };
            var brushes = new Brush[colors.Length];
            for (var i = 0; i < colors.Length; i++)
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
                b.Freeze();
                brushes[i] = b;
            }
            return brushes;
        }

        /// <summary>有效时间刻度（像素/秒）：基础刻度 × 滚轮缩放倍数</summary>
        private double EffectivePps => Math.Clamp(BasePps * _zoom, MinEffectivePps, MaxEffectivePps);

        /// <summary>基础时间刻度（像素/秒）：填了 VisibleSeconds 就按图幅宽度换算（缩放前）</summary>
        private double BasePps => UseSpanZoom && PlotWidth > 1 ? PlotWidth / VisibleSeconds : PixelsPerSecond;

        /// <summary>绘图区左边距：ECG 坐标纸要给数值轴让位</summary>
        private double LeftPad => EcgGrid ? EcgLeftGutter : DrawPadding;

        /// <summary>横轴窗口宽度的可调范围（秒），与ECG Signal窗口底栏输入框的上下限一致</summary>
        private const double MinSpanSeconds = 0.5;
        private const double MaxSpanSeconds = 300;

        /// <summary>是否按“窗口宽度（秒）”定横轴：是则滚轮改的就是这个宽度</summary>
        private bool UseSpanZoom => !double.IsNaN(VisibleSeconds) && VisibleSeconds > 0;

        /// <summary>底部轴区高度：ECG 坐标纸要多画一行轴标题</summary>
        private double BottomBand => EcgGrid ? EcgBottomBand : AxisHeight;

        /// <summary>图例条高度：右上角有图例时给图例留出一条，不让曲线名压着图幅上边框</summary>
        private const double LegendBand = 20;

        /// <summary>绘图区上边距：图例可见时图幅整体下移，图例落在它上方那条里</summary>
        private double TopPad => LegendPanel.Visibility == Visibility.Visible ? LegendBand : DrawPadding;

        /// <summary>绘图区宽度（不含左右边距）</summary>
        private double PlotWidth => Math.Max(0, ActualWidth - LeftPad - DrawPadding);

        /// <summary>进度条命中区（底部靠下的一段）：ECG 坐标纸下轴区更高，只认最底下一条</summary>
        private bool InScrollbarBand(double y) => y >= ActualHeight - (EcgGrid ? 16 : AxisHeight);

        private enum DragMode { None, Pan, Scrollbar }

        public PressureWaveformControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
            nameof(Samples), typeof(IReadOnlyList<PressureSamplePoint>), typeof(PressureWaveformControl),
            new PropertyMetadata(null, OnSamplesChanged));

        public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
            nameof(Unit), typeof(string), typeof(PressureWaveformControl),
            new PropertyMetadata("mmHg", OnUnitChanged));

        public static readonly DependencyProperty YMinProperty = DependencyProperty.Register(
            nameof(YMin), typeof(double), typeof(PressureWaveformControl),
            new PropertyMetadata(double.NaN, OnRangeChanged));

        public static readonly DependencyProperty YMaxProperty = DependencyProperty.Register(
            nameof(YMax), typeof(double), typeof(PressureWaveformControl),
            new PropertyMetadata(double.NaN, OnRangeChanged));

        public static readonly DependencyProperty SegmentLabelsProperty = DependencyProperty.Register(
            nameof(SegmentLabels), typeof(IReadOnlyList<string>), typeof(PressureWaveformControl),
            new PropertyMetadata(null, OnSegmentLabelsChanged));

        public static readonly DependencyProperty SeriesLabelsProperty = DependencyProperty.Register(
            nameof(SeriesLabels), typeof(IReadOnlyList<string>), typeof(PressureWaveformControl),
            new PropertyMetadata(null, OnSeriesLabelsChanged));

        /// <summary>采样点集合（时间单调递增）</summary>
        public IReadOnlyList<PressureSamplePoint>? Samples
        {
            get => (IReadOnlyList<PressureSamplePoint>?)GetValue(SamplesProperty);
            set => SetValue(SamplesProperty, value);
        }

        /// <summary>波形幅值单位（默认 mmHg，ECG 可设为 μV/mV 等）</summary>
        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        /// <summary>时间刻度（像素/秒，默认 24）</summary>
        public double PixelsPerSecond
        {
            get => (double)GetValue(PixelsPerSecondProperty);
            set => SetValue(PixelsPerSecondProperty, value);
        }

        /// <summary>横轴窗口宽度（秒）：NaN = 不启用，改走 <see cref="PixelsPerSecond"/> 固定倍率</summary>
        public double VisibleSeconds
        {
            get => (double)GetValue(VisibleSecondsProperty);
            set => SetValue(VisibleSecondsProperty, value);
        }

        /// <summary>是否用 ECG 坐标纸样式（粉色大小格 + 数值轴 + 轴标题）；false = 原来的灰色网格</summary>
        public bool EcgGrid
        {
            get => (bool)GetValue(EcgGridProperty);
            set => SetValue(EcgGridProperty, value);
        }

        /// <summary>
        /// 纵轴固定量程下限：NaN（默认）= 按数据自适应，血压等既有调用方行为不变；
        /// 给值则 Y 轴钉住，ECG Signal窗口用它与 <see cref="YMax"/> 实现底栏 Y Min / Y Max。
        /// </summary>
        public double YMin
        {
            get => (double)GetValue(YMinProperty);
            set => SetValue(YMinProperty, value);
        }

        /// <summary>纵轴固定量程上限（与 <see cref="YMin"/> 配对使用，NaN = 自适应）</summary>
        public double YMax
        {
            get => (double)GetValue(YMaxProperty);
            set => SetValue(YMaxProperty, value);
        }

        /// <summary>分段标注（下标 = 采样点的 Segment 值）：非空时在每段起点画分隔虚线 + 档位名，ECG 波形弹窗用</summary>
        public IReadOnlyList<string>? SegmentLabels
        {
            get => (IReadOnlyList<string>?)GetValue(SegmentLabelsProperty);
            set => SetValue(SegmentLabelsProperty, value);
        }

        /// <summary>
        /// 多曲线标注（下标 = 采样点的 Series 值）：非空时按曲线号分色（同一条带上“仪器输出 + 产品回读”两条曲线）
        /// 并在右上角画图例；为空时保持原行为（按测试点段号分色），ECG 波形弹窗用
        /// </summary>
        public IReadOnlyList<string>? SeriesLabels
        {
            get => (IReadOnlyList<string>?)GetValue(SeriesLabelsProperty);
            set => SetValue(SeriesLabelsProperty, value);
        }

        private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PressureWaveformControl)d).Redraw();

        private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PressureWaveformControl)d).Redraw();

        private static void OnPixelsPerSecondChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PressureWaveformControl)d).Redraw();

        private static void OnGridStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PressureWaveformControl)d).Redraw();

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PressureWaveformControl)d).Redraw();

        private static void OnSegmentLabelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PressureWaveformControl)d).Redraw();

        private static void OnSeriesLabelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PressureWaveformControl)d).Redraw();

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

        /// <summary>复位视图：取消滚轮缩放、恢复跟随最新数据（波形弹窗的“复位视图”按钮用）</summary>
        public void ResetView()
        {
            _zoom = 1.0;
            _viewOffset = 0;
            _followLatest = true;
            Redraw();
        }

        /// <summary>渲染 [_viewOffset, _viewOffset + 窗口时长] 内的点，并更新刻度与进度条</summary>
        private void Redraw()
        {
            var pts = Samples;
            var w = ActualWidth;
            var h = ActualHeight;
            AxisCanvas.Children.Clear();
            WaveLayer.Children.Clear();
            UpdateLegend();

            // 坐标纸模式已有数值轴与 Time (s) 标题，左上/左下/右下这三处灰字（含量程单位字样）全部让位
            MaxLabelPanel.Visibility = MinLabelPanel.Visibility = DurationLabel.Visibility
                = EcgGrid ? Visibility.Collapsed : Visibility.Visible;
            // 钉住的量程小于数据幅度时（底栏 Y Min/Y Max 调窄）曲线会画出图幅，裁到绘图区里
            WaveLayer.Clip = new RectangleGeometry(new Rect(LeftPad, TopPad,
                Math.Max(0, w - LeftPad - DrawPadding), Math.Max(0, h - BottomBand - TopPad)));

            if (pts == null || pts.Count < 2 || w < LeftPad + DrawPadding + 20 || h < TopPad + BottomBand + 20)
            {
                _hasData = false;
                _multiSegment = false;
                EmptyText.Visibility = EcgGrid ? Visibility.Collapsed : Visibility.Visible;
                DurationLabel.Text = "";
                HideHover();
                DrawEmptyPaper(w, h);
                return;
            }
            _hasData = true;
            EmptyText.Visibility = Visibility.Collapsed;

            var drawW = w - LeftPad - DrawPadding;
            var drawH = h - BottomBand - TopPad;

            // 数据范围
            var tMax = pts[pts.Count - 1].TimeSeconds;
            _tMin = pts[0].TimeSeconds;
            var totalSpan = Math.Max(0.1, tMax - _tMin);
            _pMin = double.MaxValue;
            var pMax = double.MinValue;
            for (var i = 0; i < pts.Count; i++)
            {
                var p = pts[i].Pressure;
                if (p < _pMin) _pMin = p;
                if (p > pMax) pMax = p;
            }
            var pad = Math.Max((pMax - _pMin) * 0.08, 2.0);
            _pMin -= pad;
            _pRange = Math.Max(pMax + pad - _pMin, 1);
            var displayLo = _pMin + pad;
            var displayHi = pMax;

            // 固定量程（YMin/YMax 任一给值即生效）：幅度档差得大时（0.5mV 与 10mV 同屏）不让小信号被压成直线
            if (!double.IsNaN(YMin) || !double.IsNaN(YMax))
            {
                var fixedLo = double.IsNaN(YMin) ? displayLo : YMin;
                var fixedHi = double.IsNaN(YMax) ? displayHi : YMax;
                if (fixedHi > fixedLo)
                {
                    _pMin = fixedLo;
                    _pRange = fixedHi - fixedLo;
                    displayLo = fixedLo;
                    displayHi = fixedHi;
                }
            }

            // 可视窗口与偏移（实时采样默认贴最新端；缩放后按有效刻度换算窗口时长）
            var pps = EffectivePps;
            var windowSpan = drawW / pps;
            var maxOffset = Math.Max(0, totalSpan - windowSpan);
            if (_followLatest) _viewOffset = maxOffset;
            _viewOffset = Math.Clamp(_viewOffset, 0, maxOffset);
            var t0 = _tMin + _viewOffset;          // 可视窗口起点（数据时间）
            var t1 = t0 + windowSpan;

            MaxLabel.Text = Math.Round(displayHi, 2).ToString("0.##");
            MinLabel.Text = Math.Round(displayLo, 2).ToString("0.##");
            DurationLabel.Text = $"{totalSpan:0.#}s";

            DrawGridAndTicks(w, drawW, drawH, t0, t1, pps);

            // 波形窗口两侧各扩 2px 对应时长，保证曲线在视窗边缘连续
            var bySeries = SeriesLabels is { Count: > 0 };
            _multiSegment = false;
            var collections = new Dictionary<int, PointCollection>();
            var margin = 2.0 / pps;
            for (var i = 0; i < pts.Count; i++)
            {
                var seg = pts[i].Segment;
                if (seg > 0) _multiSegment = true;
                var t = pts[i].TimeSeconds;
                if (t < t0 - margin || t > t1 + margin) continue;
                var key = bySeries ? pts[i].Series : seg;
                if (!collections.TryGetValue(key, out var col))
                {
                    col = new PointCollection();
                    collections[key] = col;
                }
                col.Add(new Point(
                    LeftPad + (t - t0) * pps,
                    PressureToY(pts[i].Pressure, drawH)));
            }
            foreach (var kv in collections.OrderBy(kv => kv.Key))
            {
                WaveLayer.Children.Add(new Polyline
                {
                    Points = kv.Value,
                    Stroke = SegmentBrushes[kv.Key % SegmentBrushes.Length],
                    StrokeThickness = 1.4
                });
            }

            DrawSegmentSeparators(pts, w, drawH, t0, pps);

            DrawScrollbar(w, drawW, totalSpan, windowSpan, maxOffset);
        }

        /// <summary>
        /// 档位分隔标注：每段（按采样点 Segment 值）起点画一条虚竖线 + 段名， <see cref="SegmentLabels"/> 时生效。
        /// </summary>
        private void DrawSegmentSeparators(IReadOnlyList<PressureSamplePoint> pts, double w, double drawH,
            double t0, double pps)
        {
            var labels = SegmentLabels;
            if (labels == null || labels.Count == 0) return;

            var dash = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)) { Opacity = 0.9 };
            var starts = new Dictionary<int, double>();
            for (var i = 0; i < pts.Count; i++)
                if (!starts.ContainsKey(pts[i].Segment)) starts[pts[i].Segment] = pts[i].TimeSeconds;

            foreach (var kv in starts.OrderBy(kv => kv.Key))
            {
                var x = LeftPad + (kv.Value - t0) * pps;
                if (x < LeftPad - 1 || x > w - DrawPadding + 1) continue;
                AxisCanvas.Children.Add(new Line
                {
                    X1 = x, Y1 = TopPad, X2 = x, Y2 = TopPad + drawH,
                    Stroke = dash, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }
                });
                if (kv.Key >= labels.Count) continue;
                AxisCanvas.Children.Add(new TextBlock
                {
                    Text = labels[kv.Key],
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61)),
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)) { Opacity = 0.75 },
                    Margin = new Thickness(x + 3, TopPad + 2, 0, 0)
                });
            }
        }

        private double PressureToY(double p, double drawH)
            => TopPad + (1 - (p - _pMin) / _pRange) * drawH;

        /// <summary>右上角图例：只在填了 <see cref="SeriesLabels"/> 时显示，色块与曲线分色取同一调色板</summary>
        private void UpdateLegend()
        {
            var labels = SeriesLabels;
            LegendPanel.Children.Clear();
            if (labels == null || labels.Count == 0)
            {
                LegendPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var fg = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61));
            for (var i = 0; i < labels.Count; i++)
            {
                if (string.IsNullOrEmpty(labels[i])) continue;
                LegendPanel.Children.Add(new Rectangle
                {
                    Width = 16, Height = 3, RadiusX = 1.5, RadiusY = 1.5,
                    Fill = SegmentBrushes[i % SegmentBrushes.Length],
                    VerticalAlignment = VerticalAlignment.Center
                });
                LegendPanel.Children.Add(new TextBlock
                {
                    Text = labels[i], FontSize = 10, Foreground = fg,
                    Margin = new Thickness(4, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center
                });
            }
            LegendPanel.Visibility = Visibility.Visible;
        }

        /// <summary>横向网格线 + 可视窗口内时间刻度</summary>
        private void DrawGridAndTicks(double w, double drawW, double drawH, double t0, double t1, double pps)
        {
            if (EcgGrid)
            {
                DrawEcgPaper(w, drawW, drawH, t0, t1, pps);
                return;
            }

            var h = ActualHeight;
            var gridBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
            var axisBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
            for (var i = 0; i <= 4; i++)
            {
                var y = TopPad + drawH * i / 4.0;
                AxisCanvas.Children.Add(new Line
                { X1 = LeftPad, Y1 = y, X2 = w - DrawPadding, Y2 = y, Stroke = gridBrush, StrokeThickness = 1 });
            }
            AxisCanvas.Children.Add(new Line
            {
                X1 = LeftPad, Y1 = h - BottomBand, X2 = w - DrawPadding, Y2 = h - BottomBand,
                Stroke = axisBrush, StrokeThickness = 1
            });

            var tickSec = PickTick();
            var rel = Math.Ceiling((t0 - _tMin) / tickSec) * tickSec;   // 相对数据起点的刻度位置
            while (rel <= t1 - _tMin + 1e-9)
            {
                var x = LeftPad + (rel - _viewOffset) * pps;
                if (x >= LeftPad - 1 && x <= w - DrawPadding + 1)
                {
                    AxisCanvas.Children.Add(new Line
                    {
                        X1 = x, Y1 = h - BottomBand, X2 = x, Y2 = h - BottomBand + 4,
                        Stroke = axisBrush, StrokeThickness = 1
                    });
                    AxisCanvas.Children.Add(new TextBlock
                    {
                        Text = $"{rel:0.#}s", FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                        Margin = new Thickness(x - 12, h - BottomBand + 4, 0, 0)
                    });
                }
                rel += tickSec;
            }
        }

        /// <summary>选刻度间隔：固定时间刻度下取间距≥60px 的最小候选</summary>
        private double PickTick()
        {
            var pps = EffectivePps;
            foreach (var c in TickCandidates)
                if (c * pps >= 60) return c;
            return TickCandidates[^1];
        }

        // ── ECG 坐标纸（ECG Signal窗口用）─────────────────────────────

        /// <summary>
        /// 坐标纸网格：细格浅粉、大格深粉（一个大格 = 5 小格），外面一圈黑色边框 + 向外的刻度短线，
        /// 左侧每个大格标数值、下方每个大格标秒数，再配竖排单位与 Time (s) 标题。
        /// 大格间隔按“横轴约 12 格、纵轴约 10 格”从候选里取整，所以改 X Width 就是改每小格代表多少秒
        /// （12s → 大格 1s、小格 0.2s；10mV → 大格 1mV、小格 0.2mV）。
        /// </summary>
        private void DrawEcgPaper(double w, double drawW, double drawH, double t0, double t1, double pps)
        {
            var left = LeftPad;
            var right = w - DrawPadding;
            var top = TopPad;
            var bottom = TopPad + drawH;
            if (right - left < 2 || bottom - top < 2 || pps <= 0) return;

            var fg = FrozenBrush(Color.FromRgb(0x21, 0x21, 0x21));
            var xMajor = NiceStep(Math.Max(t1 - t0, 1e-6) / 12);
            var yMajor = NiceStep(_pRange / 10);

            // 整张网格只生两条 Path（细格一条、大格一条），比几百个 Line 元素轻得多
            var minor = new PathFigureCollection();
            var major = new PathFigureCollection();
            var rel0 = t0 - _tMin;              // 窗口起点对应的“相对数据起点”时刻，刻度按它对齐，拖动时数字不会乱跳

            // 轮数上限是硬闸：x 算成 NaN 时“越过右边界”不成立，只靠它出不去
            for (var k = ClampGridIndex(rel0 / (xMajor / 5)); k < GridIndexLimit; k++)
            {
                var x = left + (k * xMajor / 5 - rel0) * pps;
                if (x > right + 0.5) break;
                if (x < left - 0.5) continue;
                AddGridLine(k % 5 == 0 ? major : minor, x, top, x, bottom);
            }

            var vLo = _pMin;
            var vHi = _pMin + _pRange;
            for (var k = ClampGridIndex(Math.Floor(vLo / (yMajor / 5))); k <= ClampGridIndex(Math.Ceiling(vHi / (yMajor / 5))); k++)
            {
                var y = PressureToY(k * yMajor / 5, drawH);
                if (y < top - 0.5 || y > bottom + 0.5) continue;
                AddGridLine(k % 5 == 0 ? major : minor, left, y, right, y);
            }

            AxisCanvas.Children.Add(new Path
            {
                Data = new PathGeometry { Figures = minor },
                Stroke = EcgMinorBrush, StrokeThickness = 1
            });
            AxisCanvas.Children.Add(new Path
            {
                Data = new PathGeometry { Figures = major },
                Stroke = EcgMajorBrush, StrokeThickness = 1
            });

            // 边框
            AxisCanvas.Children.Add(new Rectangle
            {
                Width = right - left, Height = bottom - top,
                Stroke = EcgAxisBrush, StrokeThickness = 1, Fill = Brushes.Transparent,
                Margin = new Thickness(left, top, 0, 0)
            });

            // 横轴：大格刻度短线 + 秒数 + Time (s) 标题
            for (var k = ClampGridIndex(rel0 / xMajor); k < GridIndexLimit; k++)
            {
                var x = left + (k * xMajor - rel0) * pps;
                if (x > right + 0.5) break;
                if (x < left - 0.5) continue;
                AxisCanvas.Children.Add(new Line
                {
                    X1 = x, Y1 = bottom, X2 = x, Y2 = bottom + 4,
                    Stroke = EcgAxisBrush, StrokeThickness = 1
                });
                AddAxisLabel(FormatValue(k * xMajor, xMajor), fg, x, bottom + 6, true);
            }

            // 纵轴：大格刻度短线 + 数值 + 竖排单位标题
            for (var k = ClampGridIndex(Math.Floor(vLo / yMajor)); k <= ClampGridIndex(Math.Ceiling(vHi / yMajor)); k++)
            {
                var y = PressureToY(k * yMajor, drawH);
                if (y < top - 0.5 || y > bottom + 0.5) continue;
                AxisCanvas.Children.Add(new Line
                {
                    X1 = left - 4, Y1 = y, X2 = left, Y2 = y,
                    Stroke = EcgAxisBrush, StrokeThickness = 1
                });
                AddAxisLabel(FormatValue(k * yMajor, yMajor), fg, left - 6, y - 7, false);
            }

            if (!string.IsNullOrEmpty(Unit))
            {
                var yTitle = new TextBlock
                {
                    Text = Unit, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = fg,
                    LayoutTransform = new RotateTransform(-90)
                };
                yTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(yTitle, 1);
                Canvas.SetTop(yTitle, top + (bottom - top) / 2 - yTitle.DesiredSize.Height / 2);
                AxisCanvas.Children.Add(yTitle);
            }
            var xTitle = new TextBlock
            {
                Text = "Time (s)", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = fg
            };
            xTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(xTitle, left + (right - left) / 2 - xTitle.DesiredSize.Width / 2);
            Canvas.SetTop(xTitle, bottom + 22);
            AxisCanvas.Children.Add(xTitle);
        }

        /// <summary>一个采样点都还没有时，按当前量程与窗口宽度先画一张空坐标纸 </summary>
        private void DrawEmptyPaper(double w, double h)
        {
            if (!EcgGrid) return;

            var drawW = w - LeftPad - DrawPadding;
            var drawH = h - BottomBand - TopPad;
            if (drawW < 20 || drawH < 20) return;

            _tMin = 0;
            _viewOffset = 0;
            var lo = double.IsNaN(YMin) ? 0 : YMin;
            var hi = double.IsNaN(YMax) ? lo + 10 : YMax;
            if (hi <= lo) hi = lo + 10;
            _pMin = lo;
            _pRange = hi - lo;

            var span = !double.IsNaN(VisibleSeconds) && VisibleSeconds > 0 ? VisibleSeconds : 12;
            DrawEcgPaper(w, drawW, drawH, 0, span, drawW / span);
        }

        /// <summary>把浮点格号收成能安全迭代的整数：NaN/无穷/溢出都夹到 ±GridIndexLimit</summary>
        private static int ClampGridIndex(double raw)
            => double.IsFinite(raw) ? (int)Math.Clamp(raw, -GridIndexLimit, GridIndexLimit) : 0;

        private static void AddGridLine(PathFigureCollection figures, double x1, double y1, double x2, double y2)
            => figures.Add(new PathFigure
            {
                StartPoint = new Point(x1, y1),
                Segments = { new LineSegment(new Point(x2, y2), true) }
            });

        /// <summary>轴标签：center=true 以 x 为水平中心，false 以 x 为右边界</summary>
        private void AddAxisLabel(string text, Brush fg, double x, double y, bool center)
        {
            var tb = new TextBlock { Text = text, FontSize = 10, Foreground = fg };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, center ? x - tb.DesiredSize.Width / 2 : x - tb.DesiredSize.Width);
            Canvas.SetTop(tb, y);
            AxisCanvas.Children.Add(tb);
        }

        /// <summary>大格间隔取整：从候选里挑第一个不小于需要值的（窗口 12s → 1s，量程 10mV → 1mV）</summary>
        private static double NiceStep(double raw)
        {
            if (raw <= 0 || double.IsNaN(raw) || double.IsInfinity(raw)) return 1;
            foreach (var c in StepCandidates)
                if (c >= raw - 1e-12) return c;
            return StepCandidates[^1];
        }

        /// <summary>轴标签小数位跟着大格走：至少一位（与上位机那张图的 5.0 / 0.0 一致），够表示大格间隔即可</summary>
        private static string FormatValue(double value, double step)
        {
            if (Math.Abs(value) < step * 1e-6) value = 0;
            var decimals = 1;
            while (decimals < 3 && !IsWhole(step * Math.Pow(10, decimals))) decimals++;
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        private static bool IsWhole(double v) => Math.Abs(v - Math.Round(v)) < 1e-9;

        /// <summary>内置横向进度条：轨道 + 滑块</summary>
        private void DrawScrollbar(double w, double drawW, double totalSpan, double windowSpanSec, double maxOffset)
        {
            _hasScrollbar = maxOffset > 0.05;
            if (!_hasScrollbar) return;

            _sbY = EcgGrid ? ActualHeight - 8 : ActualHeight - AxisHeight / 2.0;
            _sbTrackX0 = LeftPad;
            _sbTrackW = Math.Max(drawW, 1);

            // 轨道
            AxisCanvas.Children.Add(new Rectangle
            {
                Width = _sbTrackW, Height = 3, RadiusX = 1.5, RadiusY = 1.5,
                Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                Margin = new Thickness(_sbTrackX0, _sbY - 1.5, 0, 0)
            });

            // 滑块：宽度按窗口/总长比例，位置按偏移比例
            _sbThumbW = Math.Max(_sbTrackW * windowSpanSec / totalSpan, 24);
            _sbThumbX = _sbTrackX0 + (_sbTrackW - _sbThumbW) * (_viewOffset / maxOffset);
            AxisCanvas.Children.Add(new Rectangle
            {
                Width = _sbThumbW, Height = 7, RadiusX = 3.5, RadiusY = 3.5,
                Fill = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                Margin = new Thickness(_sbThumbX, _sbY - 3.5, 0, 0)
            });
        }

        private void SetOffset(double offset, double maxOffset)
        {
            _viewOffset = Math.Clamp(offset, 0, maxOffset);
            _followLatest = _viewOffset >= maxOffset - 0.05;
        }

        private double CurrentMaxOffset()
        {
            var pts = Samples;
            if (pts == null || pts.Count < 2) return 0;
            var totalSpan = Math.Max(0.1, pts[pts.Count - 1].TimeSeconds - pts[0].TimeSeconds);
            var windowSpan = PlotWidth / EffectivePps;
            return Math.Max(0, totalSpan - windowSpan);
        }

        // ── 鼠标交互：绘图区平移 / 底部进度条拖动 / 悬停读值 ────────────────
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!_hasData) return;
            var pos = e.GetPosition(this);
            var maxOffset = CurrentMaxOffset();

            if (_hasScrollbar && InScrollbarBand(pos.Y) && maxOffset > 0)
            {
                // 进度条区：点在滑块外先跳转，再进入滑块拖动
                _dragMode = DragMode.Scrollbar;
                var rel = (pos.X - _sbTrackX0 - _sbThumbW / 2) / Math.Max(_sbTrackW - _sbThumbW, 1);
                SetOffset(rel * maxOffset, maxOffset);
                HideHover();
            }
            else
            {
                _dragMode = DragMode.Pan;
                _dragStartX = pos.X;
                _dragStartOffset = _viewOffset;
                Cursor = Cursors.SizeWE;
            }
            CaptureMouse();
            Redraw();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            _dragMode = DragMode.None;
            Cursor = Cursors.Arrow;
            ReleaseMouseCapture();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pos = e.GetPosition(this);

            if (_dragMode == DragMode.Pan)
            {
                var maxOffset = CurrentMaxOffset();
                SetOffset(_dragStartOffset - (pos.X - _dragStartX) / EffectivePps, maxOffset);
                HideHover();
                Redraw();
                return;
            }
            if (_dragMode == DragMode.Scrollbar)
            {
                var maxOffset = CurrentMaxOffset();
                var rel = (pos.X - _sbTrackX0 - _sbThumbW / 2) / Math.Max(_sbTrackW - _sbThumbW, 1);
                SetOffset(rel * maxOffset, maxOffset);
                Redraw();
                return;
            }

            // 底部进度条区域：手型提示可拖
            Cursor = _hasScrollbar && InScrollbarBand(pos.Y) ? Cursors.Hand : Cursors.Arrow;
            UpdateHover(pos);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            HideHover();
            if (_dragMode == DragMode.None) Cursor = Cursors.Arrow;
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            base.OnPreviewMouseWheel(e);
            if (!_hasData) return;

            // 滚轮缩放
            var pos = e.GetPosition(this);
            var oldPps = EffectivePps;
            var factor = e.Delta > 0 ? 1.25 : 0.8;
            if (UseSpanZoom)
            {
                // 按窗口宽度模式：滚轮就是改“X Width”
                VisibleSeconds = Math.Clamp(VisibleSeconds / factor, MinSpanSeconds, MaxSpanSeconds);
                _zoom = 1.0;
            }
            else
            {
                _zoom = Math.Clamp(_zoom * factor, MinEffectivePps / PixelsPerSecond, MaxEffectivePps / PixelsPerSecond);
            }
            var newPps = EffectivePps;
            if (Math.Abs(newPps - oldPps) > 1e-9)
            {
                var tCursor = _viewOffset + (pos.X - LeftPad) / oldPps;   // 相对数据起点（秒）
                SetOffset(tCursor - (pos.X - LeftPad) / newPps, CurrentMaxOffset());
            }
            HideHover();
            Redraw();
            e.Handled = true;
        }

        // ── 悬停：十字线 + 最近采样点读数 ─────────────────────────────────
        private void UpdateHover(Point pos)
        {
            var pts = Samples;
            if (!_hasData || pts == null || _pRange <= 0)
            {
                HideHover();
                return;
            }
            var drawH = ActualHeight - BottomBand - TopPad;
            if (pos.X < LeftPad || pos.X > ActualWidth - DrawPadding || pos.Y < TopPad || pos.Y > drawH + TopPad)
            {
                HideHover();
                return;
            }

            // 视口横坐标 → 时间（含窗口偏移）→ 最近采样点
            var pps = EffectivePps;
            var t = _tMin + _viewOffset + (pos.X - LeftPad) / pps;
            var idx = PickNearestPoint(pts, t, pps, drawH, pos.Y);
            var sp = pts[idx];
            var contentX = LeftPad + (sp.TimeSeconds - _tMin - _viewOffset) * pps;
            var y = PressureToY(sp.Pressure, drawH);

            if (contentX < LeftPad || contentX > ActualWidth - DrawPadding)
            {
                HideHover();
                return;
            }

            CrosshairV.X1 = CrosshairV.X2 = contentX;
            CrosshairV.Y1 = TopPad;
            CrosshairV.Y2 = ActualHeight - BottomBand;
            CrosshairH.X1 = LeftPad;
            CrosshairH.X2 = ActualWidth - DrawPadding;
            CrosshairH.Y1 = CrosshairH.Y2 = y;
            Canvas.SetLeft(CrosshairDot, contentX - 3.5);
            Canvas.SetTop(CrosshairDot, y - 3.5);

            var unitSuffix = string.IsNullOrEmpty(Unit) ? "" : $" {Unit}";
            var hoverLabels = SeriesLabels;
            var seriesName = hoverLabels != null && sp.Series >= 0 && sp.Series < hoverLabels.Count
                ? hoverLabels[sp.Series] : null;
            var segSuffix = _multiSegment ? $"第{sp.Segment + 1}档" : "";
            if (seriesName != null) segSuffix = segSuffix.Length > 0 ? $"{seriesName} · {segSuffix}" : seriesName;
            else if (_multiSegment) segSuffix = $"第{sp.Segment + 1}点";
            HoverTipText.Text = segSuffix.Length > 0
                ? $"{sp.TimeSeconds:0.0}s，{sp.Pressure:0.0}{unitSuffix}（{segSuffix}）"
                : $"{sp.TimeSeconds:0.0}s，{sp.Pressure:0.0}{unitSuffix}";
            HoverTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var tipX = contentX + 10;
            var tipY = y - HoverTip.DesiredSize.Height - 8;
            if (tipX + HoverTip.DesiredSize.Width > ActualWidth - 4) tipX = contentX - HoverTip.DesiredSize.Width - 10;
            if (tipY < 2) tipY = y + 10;
            Canvas.SetLeft(HoverTip, tipX);
            Canvas.SetTop(HoverTip, tipY);

            CrosshairV.Visibility = CrosshairH.Visibility = CrosshairDot.Visibility = HoverTip.Visibility = Visibility.Visible;
        }

        private void HideHover()
        {
            CrosshairV.Visibility = CrosshairH.Visibility = CrosshairDot.Visibility = HoverTip.Visibility = Visibility.Collapsed;
        }

        private static int BinarySearchNearest(IReadOnlyList<PressureSamplePoint> pts, double t)
        {
            int lo = 0, hi = pts.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (pts[mid].TimeSeconds < t) lo = mid + 1; else hi = mid;
            }
            if (lo > 0 && Math.Abs(pts[lo - 1].TimeSeconds - t) < Math.Abs(pts[lo].TimeSeconds - t)) lo--;
            return lo;
        }

        /// <summary>
        /// 悬停取点
        /// </summary>
        private int PickNearestPoint(IReadOnlyList<PressureSamplePoint> pts, double t, double pps, double drawH, double posY)
        {
            var seed = BinarySearchNearest(pts, t);
            var best = seed;
            var bestDist = Math.Abs(PressureToY(pts[seed].Pressure, drawH) - posY);
            var tol = Math.Max(0.02, 5.0 / pps);

            for (var i = seed - 1; i >= 0 && pts[i].TimeSeconds >= t - tol; i--)
            {
                var d = Math.Abs(PressureToY(pts[i].Pressure, drawH) - posY);
                if (d >= bestDist) continue;
                bestDist = d;
                best = i;
            }
            for (var i = seed + 1; i < pts.Count && pts[i].TimeSeconds <= t + tol; i++)
            {
                var d = Math.Abs(PressureToY(pts[i].Pressure, drawH) - posY);
                if (d >= bestDist) continue;
                bestDist = d;
                best = i;
            }
            return best;
        }
    }
}
