using System.Windows;
using System.Windows.Controls;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 单板测试页视图：通道卡与各项结果的数据都在 <see cref="BoardChannelViewModel"/> 里，本类只做卡片右键菜单的两件事（点名跑一路、停当前轮）。
    /// 菜单项走 code-behind 而不是绑定：ContextMenu 开在自己的弹出窗口里、不在这张卡片的视觉树内，靠 AncestorType 找页面 VM 会取到空；PlacementTarget 的 DataContext 才一定是被右键的那张卡片。
    /// </summary>
    public partial class BoardChannelView : UserControl
    {
        public BoardChannelView()
        {
            InitializeComponent();
        }

        private BoardChannelViewModel? ViewModel => DataContext as BoardChannelViewModel;

        private void ChannelStart_Click(object sender, RoutedEventArgs e) => ViewModel?.StartChannel(CardUnderMenu(sender));

        private void ChannelStop_Click(object sender, RoutedEventArgs e) => ViewModel?.StopChannel(CardUnderMenu(sender));

        private static BoardChannelCardViewModel? CardUnderMenu(object sender)
        {
            var menu = sender switch
            {
                MenuItem item => item.Parent as ContextMenu,
                ContextMenu own => own,
                _ => null,
            };

            return (menu?.PlacementTarget as FrameworkElement)?.DataContext as BoardChannelCardViewModel;
        }
    }
}
