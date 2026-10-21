using System.Windows;
using TORNPISolution.Service;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 单板配置对话框（菜单：配置 → 单板配置）：通道信息与工位仪器两页在同一扇窗，不另建配置副本。
    /// 通道栏直接用单板页那份活配置，仪器栏用 InstrumentConfigViewModel 从盘上读的那份；与「仪器链接配置」窗口各持一份快照不会互抹，落盘按字段合并、只回写本窗口真改过的项。
    /// 本轮跑哪几项属作业定义，在菜单「测试项」单独一扇窗；只有保存成功才算数，✕ / 关闭 / Alt+F4 这些关窗方式一律退回进来时的值。
    /// </summary>
    public partial class BoardConfigWindow : Window
    {
        private readonly BoardChannelViewModel _board;

        /// <summary>进来时各条路的位号与两个开关：没保存就原样填回去</summary>
        private readonly List<(BoardChannelCardViewModel Card, string Name, bool Enabled, bool Round)> _channels = new();

        /// <summary>只有保存成功才不退</summary>
        private bool _saved;

        public BoardConfigWindow(BoardChannelViewModel board, InstrumentConfigViewModel instruments)
        {
            _board = board;
            Instruments = instruments;

            foreach (var card in board.Channels)
                _channels.Add((card, card.Name, card.Enabled, card.Selected));

            // 通道表吃单板页那份，仪器表吃 instruments——所以整窗的 DataContext 是单板页 VM
            DataContext = board;
            InitializeComponent();
        }

        /// <summary>工位仪器那页的数据源：XAML 里按 RelativeSource 到 Window 上取这一项</summary>
        public InstrumentConfigViewModel Instruments { get; }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // 整机方案跑着的时候不落盘：与「仪器链接配置」那句 () => !TestRunState.IsRunning 同一个闸门。
            // 单板自己跑着时根本开不了这扇窗，但整机跑期间这一路是开的，不拦就会改到正在用的判定口径
            if (TestRunState.IsRunning)
            {
                MessageBox.Show("整机测试方案正在跑，等它跑完再改仪器配置（改完本轮判定口径会与结果对不上）",
                    "单板配置", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 仪器先落：通道表右边那列设备落点是按盘上的仪器配置算的，顺序反过来这一轮还看得见旧落点
            if (!Instruments.SaveCore()) return;
            if (!_board.SaveConfigFromDialog()) return;

            _saved = true;
            _board.ApplySavedConfig();
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            if (!_saved)
            {
                foreach (var (card, name, enabled, round) in _channels)
                {
                    card.Name = name;
                    card.Enabled = enabled;
                    card.Selected = round;
                }
            }

            base.OnClosed(e);
        }
    }
}
