using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TORNPISolution.Share.Models;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// ECG Signal窗口
    /// </summary>
    public partial class WaveformOverviewWindow : Window
    {
        /// <summary>按模块 Id 各保一个实例</summary>
        private static readonly Dictionary<string, WaveformOverviewWindow> _byModule = new();

        /// <summary>段之间的留白（秒），给分隔虚线和段名留出落脚地方</summary>
        private const double GapSeconds = 1.0;

        /// <summary>重绘节流间隔</summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(600);

        private readonly string _moduleId;
        private string _moduleTitle = string.Empty;
        private WaveformOverviewProfile _profile;
        private IReadOnlyList<TestItemViewModel> _items = Array.Empty<TestItemViewModel>();
        private readonly DispatcherTimer _throttle;
        private bool _pending;
        private bool _viewDefaultsApplied;

        // ── 底栏三个输入框绑的就是这三个属性（初值由模块在目录里声明）──────

        /// <summary>横轴窗口宽度（秒）：与波形控件的 VisibleSeconds 双向绑，滚轮缩放也会写回这里</summary>
        public static readonly DependencyProperty XWidthProperty = DependencyProperty.Register(
            nameof(XWidth), typeof(double), typeof(WaveformOverviewWindow), new PropertyMetadata(12.0));

        public double XWidth
        {
            get => (double)GetValue(XWidthProperty);
            set => SetValue(XWidthProperty, value);
        }

        /// <summary>纵轴下限（单位同 profile.Unit）</summary>
        public static readonly DependencyProperty YMinValueProperty = DependencyProperty.Register(
            nameof(YMinValue), typeof(double), typeof(WaveformOverviewWindow),
            new PropertyMetadata(-5.0, OnYBoxEdited));

        public double YMinValue
        {
            get => (double)GetValue(YMinValueProperty);
            set => SetValue(YMinValueProperty, value);
        }

        /// <summary>纵轴上限（与下限差值就是量程；±5 即 5mm/mV 灵敏度）</summary>
        public static readonly DependencyProperty YMaxValueProperty = DependencyProperty.Register(
            nameof(YMaxValue), typeof(double), typeof(WaveformOverviewWindow),
            new PropertyMetadata(5.0, OnYBoxEdited));

        public double YMaxValue
        {
            get => (double)GetValue(YMaxValueProperty);
            set => SetValue(YMaxValueProperty, value);
        }

        private static void OnYBoxEdited(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((WaveformOverviewWindow)d).OnYRangeBoxesEdited();

        /// <summary>
        /// 弹出或复用该模块的总览窗口
        /// </summary>
        public static WaveformOverviewWindow ShowOrRefresh(WaveformOverviewRequest request, Window? owner = null)
        {
            if (!_byModule.TryGetValue(request.ModuleId, out var window))
            {
                window = new WaveformOverviewWindow(request);
                if (owner != null && !ReferenceEquals(owner, window))
                    window.Owner = owner;      // Owner 只能在 Show 之前设
                window.Show();
                return window;
            }

            window.Attach(request);
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
            return window;
        }

        private WaveformOverviewWindow(WaveformOverviewRequest request)
        {
            _moduleId = request.ModuleId;
            _moduleTitle = request.ModuleTitle;
            _profile = request.Profile;
            InitializeComponent();

            _byModule[_moduleId] = this;
            _throttle = new DispatcherTimer { Interval = RefreshInterval };
            _throttle.Tick += (_, _) =>
            {
                _throttle.Stop();
                _pending = false;
                Rebuild();
            };

            ApplyProfile();
            AttachItems(request.Items);

            Closed += (_, _) =>
            {
                if (_byModule.TryGetValue(_moduleId, out var w) && ReferenceEquals(w, this))
                    _byModule.Remove(_moduleId);
                _throttle.Stop();
                foreach (var item in _items) item.PropertyChanged -= ItemOnPropertyChanged;
            };

            Rebuild();
        }

        /// <summary>换绑本轮的模块与档位项集合：同一组实例就只重拼，不重复挂接事件</summary>
        public void Attach(WaveformOverviewRequest request)
        {
            _moduleTitle = request.ModuleTitle;
            _profile = request.Profile;
            ApplyProfile();
            AttachItems(request.Items);
        }

        /// <summary>把 profile 里的显示参数灌进控件与工具栏</summary>
        private void ApplyProfile()
        {
            Title = string.IsNullOrWhiteSpace(_profile.Title)
                ? $"{_moduleTitle} 波形（各档位ECG Signal）"
                : _profile.Title;

            Chart.Unit = _profile.Unit;
            Chart.SeriesLabels = _profile.SeriesLabels;
            var seriesNote = string.Join(" ／ ", _profile.SeriesLabels);
            LegendNote.Text = $"同一张图上 {_profile.SeriesLabels.Count} 条曲线：{seriesNote}。"
                + $"各段按累计时间轴首尾相接（段间留 {GapSeconds:0}s），未测到的段不占位。"
                + "滚轮缩放与拖动平移都看当前窗口宽度（底栏 X Width）。";
            StripDc.IsChecked = _profile.StripDcByDefault;
            YMinUnit.Text = YMaxUnit.Text = _profile.Unit;

            if (_viewDefaultsApplied) return;
            _viewDefaultsApplied = true;
            XWidth = _profile.DefaultXWidthSeconds;
            YMinValue = _profile.DefaultYMin;
            YMaxValue = _profile.DefaultYMax;
            ApplyYRange();
        }

        /// <summary>换绑本轮的档位项集合</summary>
        private void AttachItems(IReadOnlyList<TestItemViewModel> items)
        {
            if (items.SequenceEqual(_items))
            {
                Rebuild();
                return;
            }

            foreach (var item in _items) item.PropertyChanged -= ItemOnPropertyChanged;
            _items = items;
            foreach (var item in _items) item.PropertyChanged += ItemOnPropertyChanged;

            // 上一轮的缩放/平移视图与旧长图都丢掉，从零开始逐段生长
            _throttle.Stop();
            _pending = false;
            Chart.ResetView();
            Rebuild();
        }

        /// <summary>任一段的曲线或判定结果变了 → 节流转一圈后重拼长图</summary>
        private void ItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(TestItemViewModel.WaveformSamples)
                    or nameof(TestItemViewModel.ProductWaveformSamples)
                    or nameof(TestItemViewModel.Result)))
                return;

            if (_pending) return;
            _pending = true;
            _throttle.Start();
        }

        /// <summary>
        /// 长图重建
        /// </summary>
        private void Rebuild()
        {
            var instrument = new List<PressureSamplePoint>();
            var product = new List<PressureSamplePoint>();
            var labels = new List<string>();
            var clock = 0.0;
            var segment = 0;

            foreach (var item in _items)
            {
                var inst = item.WaveformSamples;
                var prod = item.ProductWaveformSamples;
                if (inst.Count == 0 && prod.Count == 0) continue;

                var span = Math.Max(
                    inst.Count > 0 ? inst[inst.Count - 1].TimeSeconds : 0,
                    prod.Count > 0 ? prod[prod.Count - 1].TimeSeconds : 0);
                if (span <= 0) continue;

                // 设定侧回调值可能含直流偏置（大偏置档会把小量程顶满屏），默认按段中点平移回零：
                // 波形对称时 min/max 中点就是该段的直流偏置
                var dcShift = StripDc.IsChecked == true ? InstrumentBaseline(inst) : 0.0;

                labels.Add($"{segment + 1:00}  {SegmentLabel(item.Name)}  {item.ResultText}");
                for (var i = 0; i < inst.Count; i++)
                    instrument.Add(new PressureSamplePoint(clock + inst[i].TimeSeconds, inst[i].Pressure - dcShift, segment, 0));
                for (var i = 0; i < prod.Count; i++)
                    product.Add(new PressureSamplePoint(clock + prod[i].TimeSeconds, prod[i].Pressure, segment, 1));

                clock += span + GapSeconds;
                segment++;
            }

            IReadOnlyList<string> labelArray = labels.ToArray();
            // 两条曲线合并送一张图：控件按时间单调递增取可视窗口
            Chart.Samples = MergeByTime(instrument, product);
            Chart.SegmentLabels = labelArray;

            StatusText.Text = segment == 0
                ? "本轮还没有已测档位（跑起来后会自动补进总览）"
                : $"已拼 {segment} 段，总时长 ≈{clock:0}s";
        }

        /// <summary>两条各自有序（时间递增）的曲线归并成一条，供单张图按时间窗口取点</summary>
        private static List<PressureSamplePoint> MergeByTime(
            List<PressureSamplePoint> a, List<PressureSamplePoint> b)
        {
            if (a.Count == 0) return b;
            if (b.Count == 0) return a;

            var merged = new List<PressureSamplePoint>(a.Count + b.Count);
            int i = 0, j = 0;
            while (i < a.Count && j < b.Count)
                merged.Add(a[i].TimeSeconds <= b[j].TimeSeconds ? a[i++] : b[j++]);
            while (i < a.Count) merged.Add(a[i++]);
            while (j < b.Count) merged.Add(b[j++]);
            return merged;
        }

        /// <summary>段内设定侧曲线的直流基线（min/max 中点，即该档设定的直流偏置）</summary>
        private static double InstrumentBaseline(IReadOnlyList<PressureSamplePoint> samples)
        {
            if (samples.Count == 0) return 0.0;
            double lo = double.MaxValue, hi = double.MinValue;
            for (var i = 0; i < samples.Count; i++)
            {
                var p = samples[i].Pressure;
                if (p < lo) lo = p;
                if (p > hi) hi = p;
            }
            return (lo + hi) / 2.0;
        }

        /// <summary>
        /// 段标注文字
        /// </summary>
        private string SegmentLabel(string name)
        {
            var marker = _profile.SegmentLabelAfter;
            if (string.IsNullOrEmpty(marker)) return name;
            var at = name.IndexOf(marker, StringComparison.Ordinal);
            return at >= 0 ? name.Substring(at + marker.Length) : name;
        }

        /// <summary>
        /// 量程应用：底栏 Y Min / Y Max 就是纵轴上下限（坐标纸按这个值画大格）；
        /// 勾了自适应、或填出上限≤下限，就退回按数据幅度自动取量程。
        /// </summary>
        private void ApplyYRange()
        {
            if (Chart == null) return;
            var pinned = AutoY?.IsChecked != true && YMaxValue > YMinValue;
            Chart.YMin = pinned ? YMinValue : double.NaN;
            Chart.YMax = pinned ? YMaxValue : double.NaN;
        }

        /// <summary>手改 Y Min / Y Max：以输入框为准，顺手取消自适应（不然“勾着自适应、框里的数字却没用”）</summary>
        private void OnYRangeBoxesEdited()
        {
            if (AutoY?.IsChecked == true) AutoY.IsChecked = false;
            ApplyYRange();
        }

        /// <summary>自适应开关切换</summary>
        private void AutoY_Changed(object sender, RoutedEventArgs e) => ApplyYRange();

        /// <summary>去直流开关切换：影响每段纵向平移量，整条长图重拼</summary>
        private void StripDc_Changed(object sender, RoutedEventArgs e)
        {
            if (Chart == null) return;
            Rebuild();
        }

        /// <summary>复位视图：取消滚轮缩放、恢复跟随最新段</summary>
        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            Chart.ResetView();
        }
    }
}
