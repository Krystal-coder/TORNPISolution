using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 加速度充电检查页的界面侧逻辑
    /// </summary>
    public partial class AccelCheckView : UserControl
    {
        /// <summary>贴底判定余量（行）：ScrollUnit=Item 时偏移按行计，差一行以内仍算贴着最新一条</summary>
        private const double TailToleranceItems = 1.0;

        private AccelCheckViewModel? _viewModel;
        private double _lastOffset = -1;
        private bool _scrollQueued;

        public AccelCheckView()
        {
            InitializeComponent();

            SampleGrid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnGridScrollChanged));
            DataContextChanged += (_, _) => AttachViewModel();
            AttachViewModel();
        }

        private void AttachViewModel()
        {
            if (DataContext is not AccelCheckViewModel vm || ReferenceEquals(_viewModel, vm)) return;

            if (_viewModel != null)
            {
                _viewModel.Rows.CollectionChanged -= OnRowsAppended;
                _viewModel.PropertyChanged -= OnChartToggled;
            }
            _viewModel = vm;
            vm.Rows.CollectionChanged += OnRowsAppended;
            vm.PropertyChanged += OnChartToggled;
            ApplyChartRowHeight();
        }

        /// <summary>曲线行高只能由代码给：RowDefinition 不在视觉树里，XAML 那条绑定会先撞上主窗口的视图模型报一条错误</summary>
        private void OnChartToggled(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AccelCheckViewModel.IsChartVisible)) ApplyChartRowHeight();
        }

        private void ApplyChartRowHeight()
            => ChartRow.Height = _viewModel?.IsChartVisible == true
                ? new GridLength(1, GridUnitType.Star)
                : GridLength.Auto;

        private void OnGridScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var moved = Math.Abs(e.VerticalOffset - _lastOffset) > 0.01;
            _lastOffset = e.VerticalOffset;

            // 只加长、视口没动
            if (e.ExtentHeightChange != 0 && !moved) return;

            _viewModel?.SetTailFollow(e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - TailToleranceItems);
        }

        private void OnRowsAppended(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            // 补历史时一批能插满 500 行，一条一次派发会把滚动请求排成队；本轮派发只滚一次
            if (_scrollQueued) return;
            _scrollQueued = true;

            // 本轮布局跑完才有最后一行的容器，直接滚会照旧 extent 算位置
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _scrollQueued = false;
                if (SampleGrid.Items.Count > 0) SampleGrid.ScrollIntoView(SampleGrid.Items[^1]);
            }), DispatcherPriority.Background);
        }
    }
}
