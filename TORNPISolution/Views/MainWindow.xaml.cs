using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;
using TORNPISolution.Share.Enum;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 主窗口。拖放由 gong-wpf-dragdrop 在 XAML 中接管，
    /// 这里仅保留双击加入、菜单等纯视图交互，数据操作均委托给 MainViewModel。
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            DataContext = viewModel;

            EventManager.RegisterClassHandler(typeof(MainWindow),
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(BlankArea_PreviewMouseLeftButtonDown));

            InitializeComponent();

            ViewModel.SavePlanRequested += ViewModel_SavePlanRequested;
            ViewModel.LoadPlanRequested += ViewModel_LoadPlanRequested;
            ViewModel.SelectedPlanModuleChanged += ViewModel_SelectedPlanModuleChanged;
            // 方案里有声明ECG Signal的模块时，每轮开始执行前自动带出该模块的总览窗口（按模块 Id 各一个，非模态不挡执行）
            ViewModel.WaveformOverviewRequested += ViewModel_WaveformOverviewRequested;

            DetailGrid.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(DetailGrid_WaveformToggle_ForceClick), handledEventsToo: true);

            // 启动时自动加载的方案可能已选中模块，同步一次列表明细
            Loaded += (_, _) =>
            {
                SyncPlanListSelection();
                RefreshDetailGrid();
            };
        }

        private MainViewModel ViewModel => (MainViewModel)DataContext;

        /// <summary>关闭前先让五边各自收尾：测试项（退测试模式、泄压、关共享会话）、单板轮次（断设备连接）、烧录轮（杀工具进程、写记录）、加速度充电检查与 ECG 波形采集（各自停推流、收尾 CSV、断自己那一路的产品串口），再走销毁；否则进程一退设备就留在带压的测试模式、综测仪还挂着输出、烧录工具作为孤儿进程继续占着那几路的串口、产品还留在心电测试模式一直推波形</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            ViewModel.StopAndAwaitWrapUp();
            ViewModel.Board.StopAndAwaitWrapUp();
            ViewModel.Flashing.StopAndAwaitWrapUp();
            ViewModel.AccelCheck.StopAndAwaitWrapUp();
            ViewModel.EcgCapture.StopAndAwaitWrapUp();
            base.OnClosing(e);
        }

        /// <summary>
        /// 波形折叠/展开点击
        /// </summary>
        private void DetailGrid_WaveformToggle_ForceClick(object sender, MouseButtonEventArgs e)
        {
            var toggle = FindAncestor<ToggleButton>(e.OriginalSource as DependencyObject);
            if (toggle == null || toggle.DataContext is not TestItemViewModel item)
            {
                return;
            }

            item.IsWaveformExpanded = !item.IsWaveformExpanded;
            e.Handled = true;

            // 延迟强制行重新布局
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var row = DetailGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                row?.InvalidateMeasure();
            }));
        }

        // ==================== 中间方案列表：选中同步 ====================

        /// <summary>程序正在同步列表选中</summary>
        private bool _syncingPlanSelection;

        /// <summary>方案列表空白区域点击从源头吞掉</summary>
        private void BlankArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;

            if (FindAncestor<ListBox>(source) != PlanList)
            {
                return;
            }

            if (FindAncestor<ListBoxItem>(source) != null
                || FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) != null)
            {
                return;
            }

            e.Handled = true;
        }

        /// <summary>用户点击模块卡片 → 更新 VM 选中</summary>
        private void PlanList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingPlanSelection)
            {
                return;
            }

            if (PlanList.SelectedItem is TestModuleViewModel current)
            {
                ViewModel.SelectedPlanModule = current;
                return;
            }

            var keep = ViewModel.SelectedPlanModule;
            if (keep == null || !ViewModel.PlanModules.Contains(keep))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_syncingPlanSelection)
                {
                    return;
                }

                if (PlanList.SelectedItem == null && ReferenceEquals(ViewModel.SelectedPlanModule, keep))
                {
                    SyncPlanListSelection();
                }
            }));
        }

        /// <summary>VM 选中模块变化（运行/移除/清空/加载等）→ 同步列表高亮与明细表</summary>
        private void ViewModel_SelectedPlanModuleChanged(object? sender, TestModuleViewModel? module)
        {
            SyncPlanListSelection();
            RefreshDetailGrid();
        }

        /// <summary>本轮方案里有声明ECG Signal的模块：把各自的长图窗口带出来（窗口按模块 Id 保证一个模块只一个，与手动按钮同一条路径）</summary>
        private void ViewModel_WaveformOverviewRequested(WaveformOverviewRequest request)
            => WaveformOverviewWindow.ShowOrRefresh(request, this);

        /// <summary>上次绑进明细表的集合与它当时的分组状态</summary>
        private ObservableCollection<TestItemViewModel>? _detailItems;
        private bool _detailGrouped;

        /// <summary>
        /// 切换选中模块
        /// </summary>
        private void RefreshDetailGrid()
        {
            var items = ViewModel.SelectedModuleItems;

            var grouped = items.Any(i => !string.IsNullOrEmpty(i.GroupLabel));

            if (items.Count > 0 && ReferenceEquals(_detailItems, items) && _detailGrouped == grouped)
            {
                return;
            }

            _detailItems = items;
            _detailGrouped = grouped;

            DetailGrid.ItemsSource = null;
            if (grouped)
            {
                var view = new ListCollectionView(items);
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TestItemViewModel.GroupLabel)));
                DetailGrid.ItemsSource = view;
            }
            else
            {
                DetailGrid.ItemsSource = items;
            }
        }

        /// <summary>把列表选中同步为 VM 当前模块（带重入保护）</summary>
        private void SyncPlanListSelection()
        {
            var target = ViewModel.SelectedPlanModule;
            if (ReferenceEquals(PlanList.SelectedItem, target))
            {
                return;
            }

            _syncingPlanSelection = true;
            try
            {
                PlanList.SelectedItem = target;
            }
            finally
            {
                _syncingPlanSelection = false;
            }
        }

        // ==================== 左侧模块库：双击加入 ====================

        private void LibraryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var module = GetModuleUnderMouse(e);
            if (module != null)
            {
                ViewModel.AddModuleToPlan(module);
            }
        }

        /// <summary>点击收起态窄条，展开模块库</summary>
        private void LibraryStrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ViewModel.IsLibraryVisible = true;
        }

        // ==================== 顶部菜单 ====================

        /// <summary>配置 → 仪器链接配置：打开串口参数配置对话框</summary>
        private void MenuInstrumentConfig_Click(object sender, RoutedEventArgs e)
        {
            var window = new InstrumentConfigWindow(App.Services.GetRequiredService<InstrumentConfigViewModel>()) { Owner = this };
            window.ShowDialog();
        }

        /// <summary>配置 → 单板配置：通道信息 / 工位仪器两页收在一扇窗里（与烧录配置同形）</summary>
        private void MenuBoardConfig_Click(object sender, RoutedEventArgs e)
        {
            // 跑着的时候不开：本轮范围与勾选项在开跑那一刻就定下来了，中途改只会让界面和结果对不上
            if (!ViewModel.Board.TryOpenConfig()) return;

            new BoardConfigWindow(ViewModel.Board, App.Services.GetRequiredService<InstrumentConfigViewModel>())
                { Owner = this }.ShowDialog();
        }

        /// <summary>配置 → 烧录配置：打开两颗芯片的全局参数对话框（直接吃烧录页那份活配置，不另存一份）</summary>
        private void MenuFlashingConfig_Click(object sender, RoutedEventArgs e)
        {
            new FlashConfigWindow(ViewModel.Flashing) { Owner = this }.ShowDialog();
        }

        /// <summary>测试项 → 勾选本轮要跑哪几项：项数会长到几十，所以开弹窗而不做菜单下拉；本窗只管勾选，通道与仪器归「配置→单板配置」，两处各管一处</summary>
        private void MenuBoardTestItems_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.Board.TryOpenConfig()) return;

            new BoardTestItemsWindow(ViewModel.Board) { Owner = this }.ShowDialog();
        }

        /// <summary>配置 → 血压模拟仪测试配置：打开 BPA700 / 康伟达 测试参数配置对话框（外层页签区分设备）</summary>
        private void MenuBpaParamConfig_Click(object sender, RoutedEventArgs e)
        {
            var window = new BpaParamConfigWindow(App.Services.GetRequiredService<BpaParamConfigViewModel>()) { Owner = this };
            window.ShowDialog();
        }

        /// <summary>配置 → ECG参数配置：打开 AECG100 ECG 测试参数配置对话框</summary>
        private void MenuAecg100ParamConfig_Click(object sender, RoutedEventArgs e)
        {
            var window = new Aecg100ParamConfigWindow(App.Services.GetRequiredService<Aecg100ParamConfigViewModel>()) { Owner = this };
            window.ShowDialog();
        }

        /// <summary>配置 → 协议命令配置：打开产品串口协议命令码配置对话框（需密码验证）</summary>
        private void MenuProtocolCommandConfig_Click(object sender, RoutedEventArgs e)
        {
            var window = new ProtocolCommandConfigWindow(App.Services.GetRequiredService<ProtocolCommandConfigViewModel>()) { Owner = this };
            window.ShowDialog();
        }

        /// <summary>明细头部 → 波形窗口：弹出（或复用已开的）当前模块的ECG Signal窗口，非模态、可最大化/最小化</summary>
        private void OpenWaveformOverview_Click(object sender, RoutedEventArgs e)
        {
            var module = ViewModel.SelectedPlanModule;
            var request = module == null ? null : ViewModel.BuildOverviewRequest(module.Id);
            if (request == null || request.Items.Count == 0)
            {
                MessageBox.Show(this, $"当前方案里没有可用于ECG Signal的波形测试项（模块「{module?.Name ?? "未选中"}」未在目录声明该模式）。",
                    "波形窗口", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            WaveformOverviewWindow.ShowOrRefresh(request, this);
        }

        /// <summary>文件 → 保存方案</summary>
        private void MenuSavePlan_Click(object sender, RoutedEventArgs e)
        {
            ViewModel_SavePlanRequested(sender, e);
        }

        /// <summary>文件 → 加载方案</summary>
        private void MenuLoadPlan_Click(object sender, RoutedEventArgs e)
        {
            ViewModel_LoadPlanRequested(sender, e);
        }

        /// <summary>文件 → 历史数据：回看已入库的轮次，选中旧轮次可补导 PDF（数据自动入库，这里只回看不重测）</summary>
        private void MenuHistory_Click(object sender, RoutedEventArgs e)
        {
            new HistoryWindow(App.Services.GetRequiredService<HistoryViewModel>()) { Owner = this }.ShowDialog();
        }

        /// <summary>文件 → 退出：关闭主窗口退出程序</summary>
        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ==================== 方案保存/加载对话框 ====================

        /// <summary>保存方案：弹出输入框让用户输入方案名称</summary>
        private void ViewModel_SavePlanRequested(object? sender, EventArgs e)
        {
            var defaultName = ViewModel.CurrentPlanName;
            var input = ShowInputDialog("保存测试方案", "请输入方案名称:", defaultName);
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            // 方案名就是文件名，同名直接覆盖：另一类工位已有这个名时拦一下（两面界面看着毫不相关，撞了很难查）
            if (ViewModel.IsPlanNameTakenByOtherCategory(input)
                && MessageBox.Show(this,
                    $"「{input}」已经是另一类工位的方案名（当前在：{ViewModel.LibraryCategoryText}），保存会把它覆盖掉。\n仍要保存吗？",
                    "方案名重名", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            ViewModel.SavePlanWithName(input);
        }

        /// <summary>加载方案：弹出选择框让用户选择要加载的方案（只列当前工位的那几份）</summary>
        private void ViewModel_LoadPlanRequested(object? sender, EventArgs e)
        {
            var plans = ViewModel.GetAvailablePlanNames();
            if (plans.Count == 0)
            {
                MessageBox.Show($"{ViewModel.LibraryCategoryText}还没有已保存的方案", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selected = ShowPlanSelectionDialog("加载测试方案", "请选择要加载的方案:", plans);
            if (selected != null)
            {
                ViewModel.LoadPlanWithName(selected);
            }
        }

        /// <summary>显示方案名称输入对话框</summary>
        private static string? ShowInputDialog(string title, string prompt, string defaultValue)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 360,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = new FontFamily("Microsoft YaHei UI")
            };

            var panel = new StackPanel { Margin = new Thickness(16) };
            var label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) };
            var textBox = new TextBox
            {
                Text = defaultValue,
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 0, 8, 0)
            };
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var okButton = new Button { Content = "确定", Width = 70, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancelButton = new Button { Content = "取消", Width = 70, Height = 28, IsCancel = true };

            string? result = null;
            okButton.Click += (_, _) => { result = textBox.Text.Trim(); dialog.DialogResult = true; };
            cancelButton.Click += (_, _) => { dialog.DialogResult = false; };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(label);
            panel.Children.Add(textBox);
            panel.Children.Add(buttonPanel);

            dialog.Content = panel;
            return dialog.ShowDialog() == true ? result : null;
        }

        private static string? ShowPlanSelectionDialog(string title, string prompt, IReadOnlyList<string> plans)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 360,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = new FontFamily("Microsoft YaHei UI")
            };

            var panel = new StackPanel { Margin = new Thickness(16) };
            var label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) };
            var listBox = new ListBox { Height = 160 };
            foreach (var plan in plans)
            {
                listBox.Items.Add(plan);
            }
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var okButton = new Button { Content = "加载", Width = 70, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancelButton = new Button { Content = "取消", Width = 70, Height = 28, IsCancel = true };

            string? result = null;
            okButton.Click += (_, _) => { result = listBox.SelectedItem as string; dialog.DialogResult = true; };
            cancelButton.Click += (_, _) => { dialog.DialogResult = false; };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(label);
            panel.Children.Add(listBox);
            panel.Children.Add(buttonPanel);

            dialog.Content = panel;
            return dialog.ShowDialog() == true ? result : null;
        }

        // ==================== 辅助方法 ====================

        private static TestModuleViewModel? GetModuleUnderMouse(MouseEventArgs e)
        {
            var element = e.OriginalSource as DependencyObject;

            // 点在移除按钮等交互控件上时不触发
            if (FindAncestor<Button>(element) != null)
            {
                return null;
            }

            var item = FindAncestor<ListBoxItem>(element);
            return item?.DataContext as TestModuleViewModel;
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
