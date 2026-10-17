using System.Windows;
using TORNPISolution.Share.Models;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 测试项对话框（菜单：测试项）：勾出本轮要跑哪几项，八条通道共用这一组。
    /// 与「单板配置」同一个口径——只有按了"保存"才算数，✕ / Alt+F4 这类关窗一律退回进来时的勾法。
    /// 必须退回是因为勾一项就写一次盘（勾法是这台机器的作业定义，靠人记得点保存迟早会丢），
    /// 不退回到下一轮开跑前那句自动落盘就被坐实了。
    /// </summary>
    public partial class BoardTestItemsWindow : Window
    {
        private readonly BoardChannelViewModel _board;

        /// <summary>进来时勾着的那几项：没保存就按这份填回去</summary>
        private readonly List<BoardTestItemKind> _items;

        /// <summary>只有保存成功才不退</summary>
        private bool _saved;

        public BoardTestItemsWindow(BoardChannelViewModel board)
        {
            _board = board;
            _items = board.SelectedItems.Where(o => o.Selected).Select(o => o.Kind).ToList();

            // 吃单板页那份活配置，不另建一份清单——两本迟早对不上
            DataContext = board;
            InitializeComponent();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e) => _board.SetAllItemsSelected(true);

        private void SelectNone_Click(object sender, RoutedEventArgs e) => _board.SetAllItemsSelected(false);

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _saved = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            if (!_saved) _board.RestoreItemSelection(_items);

            base.OnClosed(e);
        }
    }
}
