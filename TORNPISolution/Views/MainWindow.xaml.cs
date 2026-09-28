using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 主窗口。拖放由 gong-wpf-dragdrop 在 XAML 中接管，
    /// 这里仅保留双击加入、菜单等纯视图交互，数据操作均委托给 MainViewModel。
    /// </summary>
    public partial class MainWindow : Window
    {
        // 临时诊断：定位“波形按钮无响应”用，确认后移除
        private readonly ILogService _diagLog = new LogService(nameof(MainWindow));

        public MainWindow()
        {
            // 方案列表空白区域的左键点击在类级预览阶段直接吞掉：
            // 类处理器先于所有实例处理器（含 gong-wpf-dragdrop 注册的处理）
            // 执行，从源头阻止拖拽库清空列表选中
            EventManager.RegisterClassHandler(typeof(MainWindow),
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(BlankArea_PreviewMouseLeftButtonDown));

            InitializeComponent();

            ViewModel.SavePlanRequested += ViewModel_SavePlanRequested;
            ViewModel.LoadPlanRequested += ViewModel_LoadPlanRequested;
            ViewModel.SelectedPlanModuleChanged += ViewModel_SelectedPlanModuleChanged;

            // 波形折叠按钮点击保险：DataGrid 会接管非当前单元格的首次点击（选中行/
            // 设为当前单元格），导致单元格内 ToggleButton 收不到 Click。
            // 用 handledEventsToo 保证即使被 DataGrid 标记 Handled 也能执行：
            // 在 Preview 阶段直接翻转属性并 Handled，每次点击恰好翻转一次
            DetailGrid.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(DetailGrid_WaveformToggle_ForceClick), handledEventsToo: true);

            // 临时诊断：观察按钮自身的 Click 事件是否能到达（定位用，确认后移除）
            DetailGrid.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(WaveformDiag_ButtonClick));

            // 启动时自动加载的方案可能已选中模块，同步一次列表高亮与明细表
            Loaded += (_, _) =>
            {
                SyncPlanListSelection();
                RefreshDetailGrid();
            };
        }

        private MainViewModel ViewModel => (MainViewModel)DataContext;

        /// <summary>
        /// 波形折叠/展开点击保险：命中单元格内 ToggleButton 时直接翻转
        /// IsWaveformExpanded 并 Handled（绕过 DataGrid 对单元格点击的接管，
        /// 也避免按钮自身逻辑再翻转一次造成双重翻转）。
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
            _diagLog.Info($"[波形诊断] Preview 直接翻转：行={item.Name} → IsWaveformExpanded={item.IsWaveformExpanded}");

            // 延迟检查详情区实际状态（诊断）并强制行重新布局（保险）
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var row = DetailGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                if (row == null)
                {
                    return;
                }

                row.InvalidateMeasure();
                var presenter = FindVisualChild<DataGridDetailsPresenter>(row);
                _diagLog.Info($"[波形诊断] 翻转后状态：DetailsVisibility={row.DetailsVisibility}, " +
                    $"详情区={(presenter == null ? "未找到" : $"Visibility={presenter.Visibility}, 高度={presenter.ActualHeight:0.#}")}, " +
                    $"行高={row.ActualHeight:0.#}");
            }));
        }

        /// <summary>临时诊断：记录按钮自身 Click 事件是否到达，确认后移除</summary>
        private void WaveformDiag_ButtonClick(object sender, RoutedEventArgs e)
        {
            var item = (e.OriginalSource as FrameworkElement)?.DataContext as TestItemViewModel;
            _diagLog.Info($"[波形诊断] Click 事件到达：{e.OriginalSource?.GetType().Name}，行数据={item?.Name ?? "未知"}");
        }

        // ==================== 中间方案列表：选中同步 ====================

        /// <summary>程序正在同步列表选中（防止 SelectionChanged 回写重入）</summary>
        private bool _syncingPlanSelection;

        /// <summary>
        /// 方案列表空白区域点击从源头吞掉（类级处理器，先于 gong-wpf-dragdrop
        /// 等实例处理器执行）：点在模块卡片或滚动条上时放行（选中/拖拽/滚动
        /// 不受影响），点在其他空白处直接 Handled，选中不会被清空。
        /// </summary>
        private void BlankArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;

            // 只拦截方案列表内的空白点击，其余控件（模块库、表格等）不受影响
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

        /// <summary>
        /// 用户点击模块卡片 → 更新 VM 选中（右侧面板随之刷新）。
        /// 选中被清空时（gong-wpf-dragdrop 拖拽库接管鼠标事件，点空白
        /// 或特定交互会意外清空选中）不回写 null，并把高亮恢复到
        /// VM 当前模块，保证右侧明细面板内容不丢失。
        /// </summary>
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

            // 延迟到本次点击处理结束后恢复：若点击实际选中了新模块则无需
            // 恢复，避免同步写选中与 ListBox 选中流程重入互相打断
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

        /// <summary>
        /// 切换选中模块时先置空 ItemsSource 再赋新集合，强制 DataGrid 整表重建。
        /// 若直接把 ItemsSource 从一个集合切到另一个集合，DataGrid 会复用行容器，
        /// 出现视觉残留/命中测试错位（点单元格实际命中旧模块的单元格，
        /// 面板跳到别的模块甚至崩溃）。
        /// </summary>
        private void RefreshDetailGrid()
        {
            DetailGrid.ItemsSource = null;
            DetailGrid.ItemsSource = ViewModel.SelectedModuleItems;
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
            var window = new InstrumentConfigWindow { Owner = this };
            window.ShowDialog();
        }

        /// <summary>配置 → 血压模拟仪测试配置：打开 BPA700 / 康伟达 测试参数配置对话框（外层页签区分设备）</summary>
        private void MenuBpaParamConfig_Click(object sender, RoutedEventArgs e)
        {
            var window = new BpaParamConfigWindow { Owner = this };
            window.ShowDialog();
        }

        /// <summary>配置 → ECG参数配置：打开 AECG100 ECG 测试参数配置对话框</summary>
        private void MenuAecg100ParamConfig_Click(object sender, RoutedEventArgs e)
        {
            var window = new Aecg100ParamConfigWindow { Owner = this };
            window.ShowDialog();
        }

        /// <summary>配置 → 协议命令配置：打开产品串口协议命令码配置对话框（需密码验证）</summary>
        private void MenuProtocolCommandConfig_Click(object sender, RoutedEventArgs e)
        {
            var window = new ProtocolCommandConfigWindow { Owner = this };
            window.ShowDialog();
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
            if (input != null)
            {
                ViewModel.SavePlanWithName(input);
            }
        }

        /// <summary>加载方案：弹出选择框让用户选择要加载的方案</summary>
        private void ViewModel_LoadPlanRequested(object? sender, EventArgs e)
        {
            var plans = ViewModel.GetAvailablePlanNames();
            if (plans.Count == 0)
            {
                MessageBox.Show("暂无已保存的方案", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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

        /// <summary>显示方案选择对话框</summary>
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

        /// <summary>获取鼠标事件命中的模块（ListBoxItem 的 DataContext）</summary>
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

        private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is T match)
                {
                    return match;
                }

                var found = FindVisualChild<T>(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
