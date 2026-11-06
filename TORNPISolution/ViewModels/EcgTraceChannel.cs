using System.Globalization;
using System.Windows.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using TORNPISolution.Common;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>一路波形的显示口径：标题、单位、读数与轴标签格式、线色、纵轴默认量程与纵轴可缩到的最小跨度</summary>
    public sealed record EcgTraceStyle(string Title, string UnitLabel, string ReadoutFormat, string AxisLabelFormat,
                                      SKColor Stroke, double YMin, double YMax, double MinYSpan);

    /// <summary>
    /// 波形页一路通道的视图状态：坐标轴、显示序列、跟随/框选与读数文案。
    /// ADC 与电压两路共用宿主里的一份采样缓冲和一条采样序号，两路只是各取一个字段、各调各的量程。
    /// 横轴单位是采样序号（÷ 采样率 = 秒），改窗口宽度只换可视区间，不动数据本身。
    /// </summary>
    public sealed class EcgTraceChannel : ViewModelBase
    {
        /// <summary>默认横轴窗口宽度（秒）：初始与「重置缩放」都回到这里</summary>
        private const double DefaultWindowSeconds = 12;

        /// <summary>视图内最多画多少个点：超过就走 min-max 降采样，保留波峰波谷</summary>
        private const int MaxDisplayPoints = 3_000;

        /// <summary>缩放后横轴跨度的下限与上限（采样点）：再小看不清单个波，再大等于把整段历史塞进一屏</summary>
        private const double MinXSpanPoints = 25;
        private const double MaxXSpanPoints = 900_000;

        /// <summary>缩小后纵轴跨度上限：超过这一档认为量程已经没有意义</summary>
        private const double MaxYSpan = 10_000_000;

        /// <summary>每按一次放大/缩小的跨度系数：0.8 收、1.25 放，互为倒数所以连按能回到原样</summary>
        private const double ZoomInFactor = 0.8;
        private const double ZoomOutFactor = 1.25;

        private readonly EcgCaptureViewModel _host;
        private readonly EcgTraceStyle _style;
        private readonly int _sampleRateHz;
        private double _lastAppliedMinLimit = double.NaN;
        private double _lastAppliedMaxLimit = double.NaN;

        /// <summary>下一次过泵跳过漂移检测：轴键刚把横轴拨回尾部，此时轴限与上次记录的差是自己造成的</summary>
        private bool _skipDriftCheck;
        private double _lastViewMin = double.NaN;
        private double _lastViewMax = double.NaN;
        private int _lastViewCount = -1;
        private long _lastViewFirst = -1;

        public EcgTraceChannel(EcgCaptureViewModel host, EcgTraceStyle style, int sampleRateHz,
                               Func<EcgWavePoint, double> value)
        {
            _host = host;
            _style = style;
            _sampleRateHz = sampleRateHz;
            Value = value;

            Title = style.Title;
            UnitLabel = style.UnitLabel;

            Points = new RangeObservableCollection<ObservablePoint>();
            Series = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = Points,
                    Stroke = new SolidColorPaint(style.Stroke) { StrokeThickness = 1.5f },
                    Fill = null,
                    GeometrySize = 0,
                    AnimationsSpeed = TimeSpan.Zero
                }
            };

            XAxes = new[]
            {
                BuildAxis("时间 (s)", 0.2,
                          v => (v / sampleRateHz).ToString("F1", CultureInfo.InvariantCulture) + " s",
                          0, DefaultWindowSeconds * sampleRateHz)
            };
            YAxes = new[]
            {
                BuildAxis(style.Title, 0.5,
                          v => v.ToString(style.AxisLabelFormat, CultureInfo.InvariantCulture),
                          style.YMin, style.YMax)
            };

            WindowText = DefaultWindowSeconds.ToString("F1", CultureInfo.InvariantCulture);
            WindowSeconds = DefaultWindowSeconds;
            YMinText = style.YMin.ToString(style.AxisLabelFormat, CultureInfo.InvariantCulture);
            YMaxText = style.YMax.ToString(style.AxisLabelFormat, CultureInfo.InvariantCulture);

            ApplyAxisCommand = new RelayCommand(ApplyAxis);
            ZoomInCommand = new RelayCommand(() => ApplyZoom(ZoomInFactor));
            ZoomOutCommand = new RelayCommand(() => ApplyZoom(ZoomOutFactor));
            ResetZoomCommand = new RelayCommand(ResetZoom);
            FollowLatestCommand = new RelayCommand(FollowLatest);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
        }

        public string Title { get; }

        public string UnitLabel { get; }

        /// <summary>本通道取缓冲点里的哪一路（界面层按它做降采样与区间统计）</summary>
        public Func<EcgWavePoint, double> Value { get; }

        public RangeObservableCollection<ObservablePoint> Points { get; }

        public ISeries[] Series { get; }

        public Axis[] XAxes { get; }

        public Axis[] YAxes { get; }

        /// <summary>窗口宽度（秒）的界面文本：与纵轴上下限一样按字符串收，填错能在「应用」里说清楚，缩放键回写时也走这里</summary>
        public string WindowText
        {
            get => _windowText;
            set => SetProperty(ref _windowText, value);
        }

        private string _windowText = "12";

        /// <summary>生效中的窗口宽度（秒）：只有「应用」解析通过或缩放键算完才写这里</summary>
        public double WindowSeconds { get; private set; } = DefaultWindowSeconds;

        public string YMinText
        {
            get => _yMinText;
            set => SetProperty(ref _yMinText, value);
        }

        private string _yMinText = string.Empty;

        public string YMaxText
        {
            get => _yMaxText;
            set => SetProperty(ref _yMaxText, value);
        }

        private string _yMaxText = string.Empty;

        /// <summary>是否贴着最新数据走：滚轮缩放、拖动平移或按过缩放键就置假，「回到最新」与「应用」再拨回来</summary>
        public bool IsFollowing { get; set; } = true;

        /// <summary>本路是否已经框出区间（界面据此在提示与统计之间切换）</summary>
        public bool HasSelection { get; private set; }

        private string _selectionRangeText = NoSelectionHint;

        public string SelectionRangeText
        {
            get => _selectionRangeText;
            private set => SetProperty(ref _selectionRangeText, value);
        }

        private string _selectionValueText = string.Empty;

        public string SelectionValueText
        {
            get => _selectionValueText;
            private set => SetProperty(ref _selectionValueText, value);
        }

        private const string NoSelectionHint = "按住 Shift + 左键在波形上拖出一段，松开后在这里看这段的波峰波谷与平均";

        /// <summary>鼠标十字标钉住的采样点（绝对序号 + 本路数值），null 表示鼠标已出绘图区：界面按这两个值画十字线与圆点</summary>
        public double? CursorSample { get; private set; }

        public double? CursorValue { get; private set; }

        /// <summary>十字标读数文案（鼠标不在绘图区时为空）</summary>
        public string CursorText { get; private set; } = string.Empty;

        public ICommand ApplyAxisCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ResetZoomCommand { get; }
        public ICommand FollowLatestCommand { get; }
        public ICommand ClearSelectionCommand { get; }

        public string FormatValue(double value) => value.ToString(_style.ReadoutFormat, CultureInfo.InvariantCulture);

        /// <summary>波形面板回灌区间统计与十字标读数要走宿主（数据与算法只有一份）</summary>
        internal EcgCaptureViewModel Host => _host;

        /// <summary>粉色主网格 + 每格再切 4 份的小网格、黑字 14 号、MinZoomDelta 0.1、MinStep 由调用方给</summary>
        private static Axis BuildAxis(string name, double minStep, Func<double, string> labeler,
                                      double minLimit, double maxLimit) => new()
        {
            Name = name,
            NamePaint = new SolidColorPaint(SKColors.Black),
            LabelsPaint = new SolidColorPaint(SKColors.Black),
            TextSize = 14,
            MinStep = minStep,
            Labeler = labeler,
            MinZoomDelta = 0.1,
            MinLimit = minLimit,
            MaxLimit = maxLimit,
            SeparatorsPaint = new SolidColorPaint(new SKColor(255, 182, 193)) { StrokeThickness = 1 },
            SubseparatorsCount = 4,
            SubseparatorsPaint = new SolidColorPaint(new SKColor(255, 182, 193, 100)) { StrokeThickness = 0.75f }
        };

        // ==================== 本路的轴键 ====================

        /// <summary>「应用」：把界面上填的窗口宽度与纵轴上下限落成轴限，顺带把横轴拨回贴着最新数据；填错只提示，不改旧值</summary>
        private void ApplyAxis()
        {
            if (!TryReadAxisInputs(out var window, out var min, out var max))
            {
                _host.SetHint($"{Title}：X宽度要填大于 0.1 的秒数，Y上下限要填数字且上限大于下限");
                return;
            }

            ApplyWindowAndYRange(window, min, max);
            _host.SetHint($"{Title}：X宽度 {window:0.##} s，Y 固定 {min.ToString(_style.AxisLabelFormat, CultureInfo.InvariantCulture)}"
                        + $" ~ {max.ToString(_style.AxisLabelFormat, CultureInfo.InvariantCulture)} {UnitLabel}");
        }

        /// <summary>横纵两轴按同一系数缩放，并把新的窗口宽度与纵轴上下限回写文本框，界面上的数字始终等于屏幕上看到的量程</summary>
        private void ApplyZoom(double factor)
        {
            var zoomingIn = factor < 1;
            IsFollowing = false;

            var xAxis = XAxes[0];
            var xMin = xAxis.MinLimit ?? 0;
            var xMax = xAxis.MaxLimit ?? DefaultWindowSeconds * _sampleRateHz;
            var xSpan = (xMax - xMin) * factor;
            if (zoomingIn ? xSpan >= MinXSpanPoints : xSpan <= MaxXSpanPoints)
            {
                var xCenter = (xMin + xMax) / 2;
                xAxis.MinLimit = xCenter - xSpan / 2;
                xAxis.MaxLimit = xCenter + xSpan / 2;
                WindowSeconds = xSpan / _sampleRateHz;
                WindowText = WindowSeconds.ToString("F1", CultureInfo.InvariantCulture);
            }

            var yAxis = YAxes[0];
            if (yAxis.MinLimit.HasValue && yAxis.MaxLimit.HasValue)
            {
                var yMin = yAxis.MinLimit.Value;
                var yMax = yAxis.MaxLimit.Value;
                var ySpan = (yMax - yMin) * factor;
                if (zoomingIn ? ySpan >= _style.MinYSpan : ySpan <= MaxYSpan)
                {
                    var yCenter = (yMin + yMax) / 2;
                    yAxis.MinLimit = yCenter - ySpan / 2;
                    yAxis.MaxLimit = yCenter + ySpan / 2;
                    YMinText = yAxis.MinLimit.Value.ToString(_style.AxisLabelFormat, CultureInfo.InvariantCulture);
                    YMaxText = yAxis.MaxLimit.Value.ToString(_style.AxisLabelFormat, CultureInfo.InvariantCulture);
                }
            }

            _host.SetHint($"{Title}：X宽度 {WindowSeconds:0.##} s，Y {YMinText} ~ {YMaxText} {UnitLabel}");
            _host.PumpViews();
        }

        /// <summary>「重置缩放」：窗口宽度与纵轴量程一起回到默认，横轴重新贴着最新数据</summary>
        private void ResetZoom()
        {
            ResetAxis();
            _host.SetHint($"{Title}：已重置缩放，X宽度 {DefaultWindowSeconds:0.##} s，Y {YMinText} ~ {YMaxText} {UnitLabel}");
            _host.PumpViews();
        }

        /// <summary>「回到最新」：只把横轴拨回贴着尾部，纵轴量程按现场设置保留</summary>
        private void FollowLatest()
        {
            RearmFollow();
            _host.SetHint($"{Title}：横轴回到贴着最新数据");
            _host.PumpViews();
        }

        /// <summary>轴设置回到默认（「重置缩放」与「清空显示」共用），量程数字也一并回写文本框</summary>
        public void ResetAxis()
        {
            WindowSeconds = DefaultWindowSeconds;
            WindowText = DefaultWindowSeconds.ToString("F1", CultureInfo.InvariantCulture);
            YMinText = _style.YMin.ToString(_style.AxisLabelFormat, CultureInfo.InvariantCulture);
            YMaxText = _style.YMax.ToString(_style.AxisLabelFormat, CultureInfo.InvariantCulture);
            YAxes[0].MinLimit = _style.YMin;
            YAxes[0].MaxLimit = _style.YMax;

            // 横轴走 ApplyFollowWindow：直接写轴限会被漂移检测当成用户拖动，刚重置完就再也贴不上最新数据
            ApplyFollowWindow(0, DefaultWindowSeconds * _sampleRateHz);
            IsFollowing = true;
        }

        /// <summary>开始采集时把界面上已填的窗口宽度与纵轴量程落一次；输入不合法则保持现有轴限</summary>
        public void LockAxisFromInputs()
        {
            if (TryReadAxisInputs(out var window, out var min, out var max)) ApplyWindowAndYRange(window, min, max);
        }

        private void ApplyWindowAndYRange(double window, double min, double max)
        {
            WindowSeconds = window;
            YAxes[0].MinLimit = min;
            YAxes[0].MaxLimit = max;
            RearmFollow();
            _host.PumpViews();
        }

        /// <summary>横轴重新贴上尾部；上一次缩放留下的轴限与记录的差是自己造成的，所以先跳过一次漂移检测</summary>
        private void RearmFollow()
        {
            IsFollowing = true;
            _skipDriftCheck = true;
        }

        private bool TryReadAxisInputs(out double window, out double min, out double max)
        {
            window = ParseOrNull(WindowText) ?? 0;
            min = ParseOrNull(YMinText) ?? double.NaN;
            max = ParseOrNull(YMaxText) ?? double.NaN;
            return window > 0.1 && min < max;
        }

        private static double? ParseOrNull(string? text)
            => double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

        // ==================== 视图重建 ====================

        /// <summary>降采样重建显示序列：点数不超过上限时原样给，超过就按桶取 min-max；视图与数据都没变则跳过</summary>
        public void BuildDisplay(IReadOnlyList<EcgWavePoint> data, long firstIndex, double vxMin, double vxMax)
        {
            if (vxMin == _lastViewMin && vxMax == _lastViewMax
                && data.Count == _lastViewCount && firstIndex == _lastViewFirst) return;

            _lastViewMin = vxMin;
            _lastViewMax = vxMax;
            _lastViewCount = data.Count;
            _lastViewFirst = firstIndex;

            var start = IndexOf(data, firstIndex, vxMin);
            var end = IndexOf(data, firstIndex, vxMax + 1);
            var count = end - start;
            if (count <= 0)
            {
                Points.Clear();
                return;
            }

            if (count <= MaxDisplayPoints)
            {
                var raw = new List<ObservablePoint>(count);
                for (var i = start; i < end; i++)
                    raw.Add(new ObservablePoint(firstIndex + i, Value(data[i])));
                Points.ReplaceRange(raw);
                return;
            }

            var buckets = MaxDisplayPoints / 2;
            var decimated = new List<ObservablePoint>(MaxDisplayPoints + 2);
            for (var b = 0; b < buckets; b++)
            {
                var i0 = start + (int)((long)count * b / buckets);
                var i1 = start + (int)((long)count * (b + 1) / buckets);
                if (i1 <= i0) continue;

                var iMin = i0;
                var iMax = i0;
                for (var i = i0; i < i1; i++)
                {
                    var y = Value(data[i]);
                    if (y < Value(data[iMin])) iMin = i;
                    else if (y > Value(data[iMax])) iMax = i;
                }

                var first = Math.Min(iMin, iMax);
                var second = Math.Max(iMin, iMax);
                decimated.Add(new ObservablePoint(firstIndex + first, Value(data[first])));
                if (second != first)
                    decimated.Add(new ObservablePoint(firstIndex + second, Value(data[second])));
            }

            Points.ReplaceRange(decimated);
        }

        /// <summary>界面层写完轴限后调用：记下这是自己设的，后续漂移检测才不会把程序当成用户操作</summary>
        public void ApplyFollowWindow(double vxMin, double vxMax)
        {
            XAxes[0].MinLimit = vxMin;
            XAxes[0].MaxLimit = vxMax;
            _lastAppliedMinLimit = vxMin;
            _lastAppliedMaxLimit = vxMax;
        }

        /// <summary>轴限被图表自己改过（滚轮缩放或拖动）就不再强行跟随，否则用户拉不动波形；轴键刚拨回尾部的那一次跳过检测</summary>
        public bool IsViewDrifted()
        {
            if (!IsFollowing) return false;
            if (_skipDriftCheck)
            {
                _skipDriftCheck = false;
                return false;
            }

            var min = XAxes[0].MinLimit ?? double.NaN;
            var max = XAxes[0].MaxLimit ?? double.NaN;
            return Math.Abs(min - _lastAppliedMinLimit) > 1 || Math.Abs(max - _lastAppliedMaxLimit) > 1;
        }

        /// <summary>区间统计与十字标用的下标范围：[start,end) —— 与 BuildDisplay 同一套二分</summary>
        public (int Start, int End) RangeOf(IReadOnlyList<EcgWavePoint> data, long firstIndex, double x1, double x2)
            => (IndexOf(data, firstIndex, x1), IndexOf(data, firstIndex, x2 + 1));

        /// <summary>框选区间统计结果落成本路文案</summary>
        public void ShowSelection(long count, double startSeconds, double endSeconds,
                                  double max, double min, double mean)
        {
            if (!HasSelection)
            {
                HasSelection = true;
                OnPropertyChanged(nameof(HasSelection));
            }

            SelectionRangeText = $"{startSeconds:F3} ~ {endSeconds:F3} s（{endSeconds - startSeconds:F3} s，{count:N0} 点）";
            SelectionValueText = $"最大 {FormatValue(max)} ｜ 最小 {FormatValue(min)} ｜ "
                               + $"峰峰 {FormatValue(max - min)} ｜ 平均 {FormatValue(mean)}（{UnitLabel}）";
        }

        public void ClearSelection()
        {
            if (!HasSelection) return;

            HasSelection = false;
            SelectionRangeText = NoSelectionHint;
            SelectionValueText = string.Empty;
            OnPropertyChanged(nameof(HasSelection));
        }

        /// <summary>十字标钉住的采样点：null 表示鼠标已出绘图区；序号换算成秒给文案，像素位置由界面按同一对值算</summary>
        public void ShowCursor(double? sampleIndex, double? value)
        {
            CursorSample = sampleIndex;
            CursorValue = value;
            CursorText = sampleIndex == null || value == null
                ? string.Empty
                : $"{sampleIndex.Value / _sampleRateHz:F3} s ｜ {FormatValue(value.Value)} {UnitLabel}";
            OnPropertyChanged(nameof(CursorText));
        }

        /// <summary>换段：显示序列、框选、十字标和视图缓存一起归零；轴量程保留现场设置不动</summary>
        public void ResetView()
        {
            ClearSelection();
            ShowCursor(null, null);
            Points.Clear();
            RearmFollow();
            _lastViewMin = double.NaN;
            _lastViewMax = double.NaN;
            _lastViewCount = -1;
            _lastViewFirst = -1;
        }

        /// <summary>第一个序号 >= x 的下标（数据按序号升序；越界返回 0..Count）</summary>
        private static int IndexOf(IReadOnlyList<EcgWavePoint> data, long firstIndex, double x)
        {
            var lo = 0;
            var hi = data.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (firstIndex + mid < x) lo = mid + 1; else hi = mid;
            }

            return lo;
        }
    }
}
