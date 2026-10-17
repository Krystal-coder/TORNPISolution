using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 烧录页视图
    /// </summary>
    public partial class FlashingView : UserControl
    {
        public FlashingView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => HookAutoScroll();
        }

        private FlashingViewModel? ViewModel => DataContext as FlashingViewModel;

        
        private void HookAutoScroll()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnViewModelPropertyChanged;
            _hooked = ViewModel;
            if (_hooked != null) _hooked.PropertyChanged += OnViewModelPropertyChanged;
        }

        private FlashingViewModel? _hooked;
        private FlashChannelCardViewModel? _hookedCard;

        /// <summary>选中通道或它的时间线变化 → 滚到底；只重挂一次，卡片实例在轮次之间不变</summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FlashingViewModel.SelectedCard)) return;

            if (_hookedCard != null) _hookedCard.PropertyChanged -= OnCardPropertyChanged;
            _hookedCard = ViewModel?.SelectedCard;
            if (_hookedCard != null) _hookedCard.PropertyChanged += OnCardPropertyChanged;

            ScrollTraceToEnd();
        }

        private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FlashChannelCardViewModel.Trace))
                ScrollTraceToEnd();
        }

        private void ScrollTraceToEnd()
        {
            // 布局算完才有实际滚动范围，刚拿到快照就滚会滚不到最新一行
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TraceScroll.ScrollToEnd();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ==================== 卡片右键：只跑这一路 / 只停这一路（参数不在这里改，走菜单「配置 → 烧录配置」）====================

        /// <summary>开始测试</summary>
        private void ChannelStart_Click(object sender, RoutedEventArgs e) => ViewModel?.StartChannel(CardUnderMenu(sender));

        /// <summary>停止测试：只停被右键的那一路，其余路照常跑完</summary>
        private void ChannelStop_Click(object sender, RoutedEventArgs e) => ViewModel?.StopChannel(CardUnderMenu(sender));

        /// <summary>
        /// 从菜单项拿回被右键的卡片
        /// </summary>
        private static FlashChannelCardViewModel? CardUnderMenu(object sender)
        {
            var menu = sender switch
            {
                MenuItem item => item.Parent as ContextMenu,
                ContextMenu own => own,
                _ => null,
            };

            return (menu?.PlacementTarget as FrameworkElement)?.DataContext as FlashChannelCardViewModel;
        }
    }
}
