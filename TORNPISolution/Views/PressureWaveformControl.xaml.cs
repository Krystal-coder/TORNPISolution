using System;
using System.Collections.Generic;
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
    /// 轻量自绘压力波形：固定时间刻度（像素/秒），只渲染当前可视时间窗口；
    /// 进度条内置在图表底部（折叠行时整个面板隐藏）；
    /// 滚轮以光标为锚点缩放，绘图区左键拖动/底部进度条平移查看历史波形；
    /// 多点扫描项按测试点段号分色绘制，悬停提示附测试点序号。
    /// </summary>
    public partial class PressureWaveformControl : UserControl
    {
        /// <summary>固定时间刻度：像素/秒（可在 XAML 中按测试项绑定）</summary>
        public static readonly DependencyProperty PixelsPerSecondProperty = DependencyProperty.Register(
            nameof(PixelsPerSecond), typeof(double), typeof(PressureWaveformControl),
            new PropertyMetadata(24.0, OnPixelsPerSecondChanged));

        /// <summary>底部轴区高度（时间刻度 + 内置进度条）</summary>
        private const double AxisHeight = 22;
        /// <summary>绘图区内边距</summary>
        private const double DrawPadding = 10;
        /// <summary>时间刻度候选间隔（秒）</summary>
        private static readonly double[] TickCandidates = { 1, 2, 5, 10, 15, 30, 60, 120 };

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
        /// <summary>当前数据是否含多个测试点段（多段按段着色，悬停提示附段号）</summary>
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
        private double EffectivePps => Math.Clamp(PixelsPerSecond * _zoom, MinEffectivePps, MaxEffectivePps);

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

        private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PressureWaveformControl)d).Redraw();

        private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PressureWaveformControl)d).Redraw();

        private static void OnPixelsPerSecondChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PressureWaveformControl)d).Redraw();

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

        /// <summary>渲染 [_viewOffset, _viewOffset + 窗口时长] 内的点，并更新刻度与进度条</summary>
        private void Redraw()
        {
            var pts = Samples;
            var w = ActualWidth;
            var h = ActualHeight;
            AxisCanvas.Children.Clear();
            WaveLayer.Children.Clear();

            if (pts == null || pts.Count < 2 || w < DrawPadding * 2 + 20 || h < AxisHeight + 20)
            {
                _hasData = false;
                _multiSegment = false;
                EmptyText.Visibility = Visibility.Visible;
                DurationLabel.Text = "";
                HideHover();
                return;
            }
            _hasData = true;
            EmptyText.Visibility = Visibility.Collapsed;

            var drawW = w - DrawPadding * 2;
            var drawH = h - AxisHeight - DrawPadding;

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

            // 可视窗口与偏移（实时采样默认贴最新端；缩放后按有效刻度换算窗口时长）
            var pps = EffectivePps;
            var windowSpan = drawW / pps;
            var maxOffset = Math.Max(0, totalSpan - windowSpan);
            if (_followLatest) _viewOffset = maxOffset;
            _viewOffset = Math.Clamp(_viewOffset, 0, maxOffset);
            var t0 = _tMin + _viewOffset;          // 可视窗口起点（数据时间）
            var t1 = t0 + windowSpan;

            MaxLabel.Text = Math.Round(pMax, 1).ToString("0.#");
            MinLabel.Text = Math.Round(_pMin + pad, 1).ToString("0.#");
            DurationLabel.Text = $"{totalSpan:0.#}s";

            DrawGridAndTicks(w, drawW, drawH, t0, t1);

            // 波形：按测试点段号分组绘制（同段同色，单段保持原蓝色）；
            // 窗口两侧各扩 2px 对应时长，保证曲线在视窗边缘连续
            _multiSegment = false;
            var collections = new Dictionary<int, PointCollection>();
            var margin = 2.0 / pps;
            for (var i = 0; i < pts.Count; i++)
            {
                var seg = pts[i].Segment;
                if (seg > 0) _multiSegment = true;
                var t = pts[i].TimeSeconds;
                if (t < t0 - margin || t > t1 + margin) continue;
                if (!collections.TryGetValue(seg, out var col))
                {
                    col = new PointCollection();
                    collections[seg] = col;
                }
                col.Add(new Point(
                    DrawPadding + (t - t0) * pps,
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

            DrawScrollbar(w, drawW, totalSpan, windowSpan, maxOffset);
        }

        private double PressureToY(double p, double drawH)
            => DrawPadding + (1 - (p - _pMin) / _pRange) * drawH;

        /// <summary>横向网格线 + 可视窗口内时间刻度</summary>
        private void DrawGridAndTicks(double w, double drawW, double drawH, double t0, double t1)
        {
            var h = ActualHeight;
            var gridBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
            var axisBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
            for (var i = 0; i <= 4; i++)
            {
                var y = DrawPadding + drawH * i / 4.0;
                AxisCanvas.Children.Add(new Line
                { X1 = DrawPadding, Y1 = y, X2 = w - DrawPadding, Y2 = y, Stroke = gridBrush, StrokeThickness = 1 });
            }
            AxisCanvas.Children.Add(new Line
            {
                X1 = DrawPadding, Y1 = h - AxisHeight, X2 = w - DrawPadding, Y2 = h - AxisHeight,
                Stroke = axisBrush, StrokeThickness = 1
            });

            var tickSec = PickTick();
            var rel = Math.Ceiling((t0 - _tMin) / tickSec) * tickSec;   // 相对数据起点的刻度位置
            while (rel <= t1 - _tMin + 1e-9)
            {
                var x = DrawPadding + (rel - _viewOffset) * PixelsPerSecond;
                if (x >= DrawPadding - 1 && x <= w - DrawPadding + 1)
                {
                    AxisCanvas.Children.Add(new Line
                    {
                        X1 = x, Y1 = h - AxisHeight, X2 = x, Y2 = h - AxisHeight + 4,
                        Stroke = axisBrush, StrokeThickness = 1
                    });
                    AxisCanvas.Children.Add(new TextBlock
                    {
                        Text = $"{rel:0.#}s", FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                        Margin = new Thickness(x - 12, h - AxisHeight + 4, 0, 0)
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

        /// <summary>内置横向进度条：轨道 + 滑块</summary>
        private void DrawScrollbar(double w, double drawW, double totalSpan, double windowSpanSec, double maxOffset)
        {
            _hasScrollbar = maxOffset > 0.05;
            if (!_hasScrollbar) return;

            _sbY = ActualHeight - AxisHeight / 2.0;
            _sbTrackX0 = DrawPadding;
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
            var windowSpan = (ActualWidth - DrawPadding * 2) / EffectivePps;
            return Math.Max(0, totalSpan - windowSpan);
        }

        // ── 鼠标交互：绘图区平移 / 底部进度条拖动 / 悬停读值 ────────────────
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!_hasData) return;
            var pos = e.GetPosition(this);
            var maxOffset = CurrentMaxOffset();

            if (_hasScrollbar && pos.Y >= ActualHeight - AxisHeight && maxOffset > 0)
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
            Cursor = _hasScrollbar && pos.Y >= ActualHeight - AxisHeight ? Cursors.Hand : Cursors.Arrow;
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

            // 滚轮缩放（锚定光标下的时刻：缩放前后该时刻仍停留在光标处）
            var pos = e.GetPosition(this);
            var oldPps = EffectivePps;
            var factor = e.Delta > 0 ? 1.25 : 0.8;
            _zoom = Math.Clamp(_zoom * factor, MinEffectivePps / PixelsPerSecond, MaxEffectivePps / PixelsPerSecond);
            var newPps = EffectivePps;
            if (Math.Abs(newPps - oldPps) > 1e-9)
            {
                var tCursor = _viewOffset + (pos.X - DrawPadding) / oldPps;   // 相对数据起点（秒）
                SetOffset(tCursor - (pos.X - DrawPadding) / newPps, CurrentMaxOffset());
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
            var drawW = ActualWidth - DrawPadding * 2;
            var drawH = ActualHeight - AxisHeight - DrawPadding;
            if (pos.X < DrawPadding || pos.X > ActualWidth - DrawPadding || pos.Y < 0 || pos.Y > drawH + DrawPadding)
            {
                HideHover();
                return;
            }

            // 视口横坐标 → 时间（含窗口偏移）→ 最近采样点
            var pps = EffectivePps;
            var t = _tMin + _viewOffset + (pos.X - DrawPadding) / pps;
            var idx = BinarySearchNearest(pts, t);
            var sp = pts[idx];
            var contentX = DrawPadding + (sp.TimeSeconds - _tMin - _viewOffset) * pps;
            var y = PressureToY(sp.Pressure, drawH);

            if (contentX < DrawPadding || contentX > ActualWidth - DrawPadding)
            {
                HideHover();
                return;
            }

            CrosshairV.X1 = CrosshairV.X2 = contentX;
            CrosshairV.Y1 = DrawPadding;
            CrosshairV.Y2 = ActualHeight - AxisHeight;
            CrosshairH.X1 = DrawPadding;
            CrosshairH.X2 = ActualWidth - DrawPadding;
            CrosshairH.Y1 = CrosshairH.Y2 = y;
            Canvas.SetLeft(CrosshairDot, contentX - 3.5);
            Canvas.SetTop(CrosshairDot, y - 3.5);

            var unitSuffix = string.IsNullOrEmpty(Unit) ? "" : $" {Unit}";
            var segSuffix = _multiSegment ? $"（第{sp.Segment + 1}点）" : "";
            HoverTipText.Text = $"{sp.TimeSeconds:0.0}s，{sp.Pressure:0.0}{unitSuffix}{segSuffix}";
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
    }
}
