using GongSolutions.Wpf.DragDrop;
using HandyControl.Controls;
using HandyControl.Data;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TORNPISolution.Common;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Service.Devices;
using TORNPISolution.Service.Flashing;
using TORNPISolution.Share.Enum;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel：启动先停在工位选择页，点哪个进哪个（整机测试 / 单板测试 / 烧录），各页自带返回键。
    /// 整机页三栏：模块库（左）→ 拖拽组建测试方案（中）→ 查看模块测试明细（右）。
    /// 类别以 TestModuleModel.Category 为准（Spec 目录逐个声明），界面只按它过滤，不按模块 Id 前缀猜。
    /// </summary>
    public class MainViewModel : ViewModelBase, IDropTarget
    {
        private readonly ILogService _log = new LogService(nameof(MainViewModel));
        private readonly ITestItemService _testItemService;
        private readonly ITestPlanService _testPlanService;
        private readonly ITestModuleService _moduleService;
        private readonly IAppStateService _appState;
        // 仪器链接配置：入库快照要带本轮量测时间（配置改了不改历史轮次对账）
        private readonly IInstrumentConfigService _configService;
        // 烧录工位的界面数据：与测试方案共用一个窗口但各跑各的一轮，切页与开方案时互当闸门
        private readonly FlashingViewModel _flashing;
        // 单板工位的界面数据：按通道跑固定几项，不走模块库与方案
        private readonly BoardChannelViewModel _board;
        // 主窗口停在哪个页面：启动永远先落在工位选择页，让人自己点进去
        private WorkbenchPage _selectedPage = WorkbenchPage.Entry;
        // 上一次进的工位：只在入口页念一句“上次在…”，不替人决定进哪一页
        private WorkbenchPage _lastStation = WorkbenchPage.WholeMachine;
        // 模块库当前停在哪个类别：单板页按通道跑，这一项恒为整机
        private ModuleCategory _libraryCategory = ModuleCategory.WholeMachine;
        // 全量目录快照（整机+单板都在这）：按 Id 查模块只认这份，界面上的过滤清单另开一份
        private readonly List<TestModuleViewModel> _catalog = new();
        private TestModuleViewModel? _selectedPlanModule;
        private bool _isLibraryVisible = false;
        private bool _isRunning;
        private string _currentPlanName = string.Empty;
        // 运行期间的取消令牌：点"停止"时 Cancel
        private CancellationTokenSource? _runCts;
        // 一轮 RunPlan 收尾（含 finally 里的退测试模式/泄压/关共享会话）完成后置位，供关窗口时等待
        private TaskCompletionSource? _runDone;
        // 关窗口时已在等收尾：避免重复点关闭叠多层消息泵
        private bool _awaitingWrapUp;
        // 自动入库：测试完整跑完即写库（单事务 3 张表）；入库失败时快照留内存，等「重试保存」
        private readonly ITestDataService _testDataService;
        // 报告导出：数据已自动入库，「导出报告」只负责按编号出 PDF（QuestPDF）
        private readonly ITestReportService _testReportService;
        // 一轮方案的开始时刻（入库快照的 StartedAt）
        private DateTime _runStartedAt;
        // 大项用时起点（派发前）与逐项耗时锚点：测试线程算、UI 线程在人工提示返回时前移，两边都要拿到完整值，读写统一在 _clockLock 下
        private DateTime _moduleClockStart;
        private DateTime _itemClockAnchor;
        private readonly object _clockLock = new();
        // 入库失败滞留的快照：数据还在内存，用户点「重试保存」可重放；下一轮开跑时被覆盖（先记日志）
        private TestRunSnapshot? _pendingSaveSnapshot;
        // 最近一次成功入库的报告编号：「导出报告」按钮据此判断可否导出（null=本轮还没存上）
        private string? _lastSavedReportNo;

        /// <summary>血压模拟仪方案级共享会话（单例）：RunPlan 开始建立、结束关闭，模块间不复关复开串口</summary>
        private readonly BloodPressureSimulatorSession _simSession;

        public MainViewModel(ITestModuleService moduleService, ITestItemService testItemService,
                             ITestPlanService testPlanService, IAppStateService appState,
                             FlashingViewModel flashing, BoardChannelViewModel board,
                             BloodPressureSimulatorSession simSession, ITestDataService testDataService,
                             ITestReportService testReportService, IInstrumentConfigService configService)
        {
            _testItemService = testItemService;
            _moduleService = moduleService;
            _testPlanService = testPlanService;
            _appState = appState;
            _configService = configService;
            _flashing = flashing;
            _board = board;
            _simSession = simSession;
            _testDataService = testDataService;
            _testReportService = testReportService;

            // 启动一律先回工位选择页；上一次在哪个工位只当一句提示，不再直接把人带进那一页
            _lastStation = appState.LoadLastPage();
            if (_lastStation == WorkbenchPage.Entry) _lastStation = WorkbenchPage.WholeMachine;
            RefreshModuleCatalog();

            AddToPlanCommand = new RelayCommand<TestModuleViewModel>(m => AddModuleToPlan(m));
            RemoveFromPlanCommand = new RelayCommand<TestModuleViewModel>(RemoveModuleFromPlan, _ => !IsRunning);
            ClearPlanCommand = new RelayCommand(ClearPlan, () => PlanModules.Count > 0 && !IsRunning);
            // 运行/停止共用一个命令：空闲时启动方案，运行中取消（按钮文字随 IsRunning 切换）
            RunOrStopCommand = new RelayCommand(RunOrStop, () => PlanModules.Count > 0 || IsRunning);
            ResetPlanCommand = new RelayCommand(ResetPlan, () => PlanModules.Count > 0 && !IsRunning);
            ToggleLibraryCommand = new RelayCommand(() => IsLibraryVisible = !IsLibraryVisible);
            // 三个入口各一个命令（入口页上点），各工位页左上角一个返回键；任一边在跑就不让走
            OpenWholeMachinePageCommand = new RelayCommand(() => SwitchPage(WorkbenchPage.WholeMachine), CanSwitchPage);
            OpenBoardPageCommand = new RelayCommand(() => SwitchPage(WorkbenchPage.Board), CanSwitchPage);
            OpenFlashingPageCommand = new RelayCommand(() => SwitchPage(WorkbenchPage.Flashing), CanSwitchPage);
            BackToEntryCommand = new RelayCommand(BackToEntry, CanSwitchPage);
            SavePlanCommand = new RelayCommand(SavePlan, () => PlanModules.Count > 0 && !IsRunning);
            LoadPlanCommand = new RelayCommand(LoadPlan, () => !IsRunning);
            // 入库失败后的重试：快照留内存，CanExecute 由 _pendingSaveSnapshot 是否有值决定（WPF 空闲自动刷新）
            RetrySaveCommand = new RelayCommand(RetrySave, () => _pendingSaveSnapshot != null);
            // 「导出报告」：数据已自动入库，这颗按钮只负责导 PDF；没存上（入库失败/全未测）时置灰
            SaveReportCommand = new RelayCommand(SaveReport, () => _lastSavedReportNo != null && !IsRunning);

            // 方案增删/移动时统一刷新执行序号，保证序号始终跟随数据顺序
            PlanModules.CollectionChanged += (_, _) =>
            {
                RefreshSequences();
                OnPropertyChanged(nameof(ShowOverviewWaveformButton));
            };

            // BP-001 压力采样点实时上抛 → 收集后节流刷新波形图
            _testItemService.PressureSampled += OnPressureSampled;

            // 设备连接失败 → 弹出 HandyControl Growl 通知
            _testItemService.DeviceConnectionFailed += OnDeviceConnectionFailed;

            // 测试项需要人工操作（如短按产品物理键）→ 弹模态提示框，测试员点确定后测试线程才继续跑
            _testItemService.OperatorPromptRequested += OnOperatorPromptRequested;

            // 启动时自动加载原有使用的方案
            AutoLoadLastPlan();
        }

        // ==================== 测试项波形（每个带波形的测试项独立采集/展开，一项可上两条曲线） ====================

        /// <summary>曲线编号：产品回传曲线（与 TestItem.WaveformSeriesProduct 同一约定）</summary>
        private const int ProductWaveformSeries = 1;

        private readonly object _waveformLock = new();
        /// <summary>采样缓存：键=(测试项名, 曲线号)，0=仪器输出曲线、1=产品回传曲线（仅 A10 用 1）</summary>
        private readonly Dictionary<(string Item, int Series), List<PressureSamplePoint>> _waveformBuffers = new();
        /// <summary>本轮新增过采样点的缓冲键：刷新 UI 只复制这些键，长采集下不随全部缓冲点数放大</summary>
        private readonly HashSet<(string Item, int Series)> _waveformDirty = new();
        private DateTime _lastWaveformFlush = DateTime.MinValue;

        /// <summary>采样点回调（后台线程，带测试项名与曲线号）：入对应缓存并标脏，最多 150ms 节流刷一次 UI</summary>
        private void OnPressureSampled(string itemName, PressureSamplePoint point)
        {
            var key = (itemName, point.Series);
            lock (_waveformLock)
            {
                if (!_waveformBuffers.TryGetValue(key, out var list))
                {
                    list = new List<PressureSamplePoint>();
                    _waveformBuffers[key] = list;
                }

                list.Add(point);
                _waveformDirty.Add(key);
            }

            var now = DateTime.UtcNow;
            if ((now - _lastWaveformFlush).TotalMilliseconds < 150)
            {
                return;
            }

            _lastWaveformFlush = now;
            FlushWaveform();
        }

        /// <summary>把本轮变化的采样点快照刷到对应 TestItemViewModel（调度到主线程，按项名+曲线号匹配）</summary>
        private void FlushWaveform()
        {
            List<(string Item, int Series, PressureSamplePoint[] Points)> snapshots;
            lock (_waveformLock)
            {
                if (_waveformDirty.Count == 0)
                {
                    return;
                }

                snapshots = new List<(string Item, int Series, PressureSamplePoint[] Points)>(_waveformDirty.Count);
                foreach (var key in _waveformDirty)
                {
                    if (_waveformBuffers.TryGetValue(key, out var list))
                        snapshots.Add((key.Item, key.Series, list.ToArray()));
                }
                _waveformDirty.Clear();
            }

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                foreach (var module in PlanModules)
                {
                    foreach (var item in module.Items)
                    {
                        if (!item.HasWaveform) continue;
                        foreach (var (name, series, points) in snapshots)
                        {
                            if (name != item.Name) continue;
                            if (series == ProductWaveformSeries) item.ProductWaveformSamples = points;
                            else item.WaveformSamples = points;
                        }
                    }
                }
            });
        }

        /// <summary>清空波形数据（每轮运行开始前调用：缓存与各测试项的采样点快照，含产品回传曲线）</summary>
        private void ClearWaveform()
        {
            lock (_waveformLock)
            {
                _waveformBuffers.Clear();
                _waveformDirty.Clear();
            }

            foreach (var module in PlanModules)
            {
                foreach (var item in module.Items)
                {
                    if (item.HasWaveform)
                    {
                        item.WaveformSamples = Array.Empty<PressureSamplePoint>();
                        item.ProductWaveformSamples = Array.Empty<PressureSamplePoint>();
                    }
                }
            }
        }

        /// <summary>组装某个模块的ECG Signal请求：同一模块多次加入方案时，各次加入的档位项合并在同一条长图上；模块没声明总览（目录里 Overview 为空）则返回 null。</summary>
        public WaveformOverviewRequest? BuildOverviewRequest(string moduleId)
        {
            var matched = PlanModules.Where(m => m.Id == moduleId && m.Model.Overview != null).ToList();
            if (matched.Count == 0) return null;
            var items = matched.SelectMany(m => m.Items.Where(i => i.HasOverviewWaveform)).ToList();
            return new WaveformOverviewRequest(moduleId, matched[0].Name, matched[0].Model.Overview!, items);
        }

        /// <summary>右侧明细头部是否显示“波形窗口”按钮：当前选中模块声明了ECG Signal即显示</summary>
        public bool ShowOverviewWaveformButton => SelectedPlanModule?.Model.Overview != null;

        /// <summary>ECG Signal窗口请求（按模块发起，View 层按模块 Id 各开一个窗口）</summary>
        public event Action<WaveformOverviewRequest>? WaveformOverviewRequested;

        /// <summary>方案里排到了声明总览的模块，就把它们各自的长图窗口带上来（同一模块只开一个）</summary>
        private void RaiseWaveformOverviewRequested()
        {
            foreach (var id in PlanModules.Where(m => m.Model.Overview != null).Select(m => m.Id).Distinct())
            {
                var request = BuildOverviewRequest(id);
                if (request != null)
                    WaveformOverviewRequested?.Invoke(request);
            }
        }

        /// <summary>设备连接失败的气泡留几秒：够读完两行字，又不至于跑几轮就攒一屏要点关闭的框（HandyControl 内部最低 2 秒）</summary>
        private const int DeviceFailGrowlSeconds = 5;

        /// <summary>入库成功/滞留提示气泡留几秒</summary>
        private const int SaveGrowlSeconds = 6;

        /// <summary>入库失败滞留提示留几秒：这条要人去点「重试保存」，比成功提示给更长的停留</summary>
        private const int PendingSaveGrowlSeconds = 20;

        /// <summary>设备连接失败通知：Growl 错误气泡，几秒后自己收，鼠标停在气泡上 HandyControl 会把计时清零。Error 档在 !IsCustom 时硬置 StaysOpen=true（就是不自动关，8 路一起报错就得挨个点关闭）；走 IsCustom=true 才会上启动关闭的计时器，而图标与红色仍按 Error 默认给（源码那边是 ??= 兜底），外观不变；同一份内容在页底状态条、明细表与日志里都还在。</summary>
        private void OnDeviceConnectionFailed(string moduleName, string message)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                Growl.ErrorGlobal(new GrowlInfo
                {
                    Message = $"【{moduleName}】{message}",
                    WaitTime = DeviceFailGrowlSeconds,
                    IsCustom = true,
                });
            });
        }

        /// <summary>人工操作提示：弹模态框告知测试员该按哪个键，用户点确定前测试线程挂在这里等。MessageBox 自己的模态消息循环会继续泵消息，UI 线程不会冻住；点确定后 InvokeAsync 的 Task 完成。模态框期间主窗口不可操作，“停止”按钮要等点完确定才点得到。框存活多久就是测试员动手多久，这段从用时里扣掉。</summary>
        private async Task OnOperatorPromptRequested(string moduleName, string message)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            var waited = await dispatcher.InvokeAsync(() =>
            {
                const string caption = "测试操作提示";
                var text = $"【{moduleName}】\n\n{message}";
                var owner = Application.Current?.MainWindow;
                var shownAt = DateTime.Now;
                if (owner != null && owner.IsLoaded)
                    System.Windows.MessageBox.Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    System.Windows.MessageBox.Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Information);
                return DateTime.Now - shownAt;
            }).Task;

            ShiftClockForHumanWait(waited);
            _log.Info($"{moduleName}：人工操作等待 {waited.TotalSeconds:0.0}s，不计入大项与逐项用时");
        }

        /// <summary>把大项起点与逐项锚点整体前移人工等待时长：换塞子、封堵毛细孔这类动手操作不该算进测试用时</summary>
        private void ShiftClockForHumanWait(TimeSpan waited)
        {
            if (waited <= TimeSpan.Zero) return;
            lock (_clockLock)
            {
                _moduleClockStart += waited;
                _itemClockAnchor += waited;
            }
        }

        // ==================== gong-wpf-dragdrop 落点处理 ====================

        /// <summary>拖拽悬停：库→方案为复制，方案内部为移动，均显示插入位置指示线；运行中禁止拖放</summary>
        void IDropTarget.DragOver(IDropInfo dropInfo)
        {
            if (IsRunning || dropInfo.Data is not TestModuleViewModel)
            {
                return;
            }

            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            dropInfo.Effects = ReferenceEquals(dropInfo.DragInfo?.SourceCollection, LibraryModules)
                ? DragDropEffects.Copy
                : DragDropEffects.Move;
        }

        /// <summary>拖拽释放：从模块库拖入按落点插入；方案内拖拽调整执行顺序；运行中禁止拖放</summary>
        void IDropTarget.Drop(IDropInfo dropInfo)
        {
            if (IsRunning || dropInfo.Data is not TestModuleViewModel module)
            {
                return;
            }

            var insertIndex = dropInfo.InsertIndex;

            if (ReferenceEquals(dropInfo.DragInfo?.SourceCollection, LibraryModules))
            {
                AddModuleToPlan(module, insertIndex);
                return;
            }

            var oldIndex = PlanModules.IndexOf(module);
            if (oldIndex >= 0 && oldIndex < insertIndex)
            {
                insertIndex--;
            }

            MovePlanModule(module, insertIndex);
        }

        /// <summary>按当前顺序刷新方案内所有模块的执行序号（从 1 开始）</summary>
        private void RefreshSequences()
        {
            for (var i = 0; i < PlanModules.Count; i++)
            {
                PlanModules[i].Sequence = i + 1;
            }
        }

        /// <summary>模块库显示清单：只放当前工位类别的模块，XAML 绑定与拖拽源都吃它；全量清单在 _catalog——方案加载/重建要按 Id 查任意类别的模块，拿这份过滤后的清单查会静默查不着、方案变空。</summary>
        public ObservableCollection<TestModuleViewModel> LibraryModules { get; } = new();

        /// <summary>当前工位类别显示名（模块库副标题）</summary>
        public string LibraryCategoryText => _libraryCategory.ToDisplay();

        /// <summary>模块库标题：直接写清这一页是哪类工位，不用卡片翻面表达</summary>
        public string LibraryTitleText => $"{_libraryCategory.ToShortDisplay()}模块库";

        /// <summary>这一页有没有模块：单板目录项少，空了就在列表位置显示占位提示</summary>
        public bool HasLibraryModules => LibraryModules.Count > 0;

        /// <summary>烧录工位的界面数据：XAML 把 FlashingView 的 DataContext 接到它上</summary>
        public FlashingViewModel Flashing => _flashing;

        /// <summary>单板工位的界面数据：XAML 把 BoardChannelView 的 DataContext 接到它上</summary>
        public BoardChannelViewModel Board => _board;

        /// <summary>主窗口当前停在哪个页面（只能由入口页的四个命令改，绑定不回写）</summary>
        public WorkbenchPage SelectedPage
        {
            get => _selectedPage;
            private set
            {
                if (!SetProperty(ref _selectedPage, value)) return;

                OnPropertyChanged(nameof(IsEntryPage));
                OnPropertyChanged(nameof(IsWholeMachinePage));
                OnPropertyChanged(nameof(IsBoardPage));
                OnPropertyChanged(nameof(IsFlashingPage));
                OnPropertyChanged(nameof(ShowBackButton));
                OnPropertyChanged(nameof(LastStationHintText));
            }
        }

        /// <summary>停在工位选择页：三个入口都在这，进去了就不该再看见另外两摊</summary>
        public bool IsEntryPage => SelectedPage == WorkbenchPage.Entry;

        /// <summary>停在整机页：只有这一页还走模块库 + 方案那三栏</summary>
        public bool IsWholeMachinePage => SelectedPage == WorkbenchPage.WholeMachine;

        /// <summary>停在单板页：按通道跑的界面，与模块库无关</summary>
        public bool IsBoardPage => SelectedPage == WorkbenchPage.Board;

        /// <summary>停在烧录页</summary>
        public bool IsFlashingPage => SelectedPage == WorkbenchPage.Flashing;

        /// <summary>菜单行最右侧那枚返回键要不要给（入口页上没有自己可返）</summary>
        public bool ShowBackButton => !IsEntryPage;

        /// <summary>入口页注释</summary>
        public string LastStationHintText => $"上次这台机器用的是：{_lastStation.ToDisplay()}";

        /// <summary>重建模块库快照：A05/A06/A21 等目录项按当前仪器配置动态生成（BP-003 一点一项、BP-004 一组一轮一项 + 每组重复性、BP-005 一点一轮一项 + 每点重复性）；配置改点后无需重启，但只有走到本方法才重建快照：展开模块库、加载方案、点运行三处，关掉配置对话框不触发，改完直接拖模块拿到的仍是上一份快照。</summary>
        private void RefreshModuleCatalog()
        {
            BuildCatalog();
            FillLibraryModules();
        }

        /// <summary>全量快照：两类工位的目录项都收进来，按 Id 查模块（方案加载、方案内重建）</summary>
        private void BuildCatalog()
        {
            _catalog.Clear();
            foreach (var m in _moduleService.GetAllModules())
                _catalog.Add(new TestModuleViewModel(m));
        }

        private void FillLibraryModules()
        {
            LibraryModules.Clear();
            foreach (var m in _catalog.Where(x => x.Category == _libraryCategory))
                LibraryModules.Add(m);

            OnPropertyChanged(nameof(HasLibraryModules));
        }

        /// <summary>按最新目录重建方案内模块实例，保证运行判定与界面项清单一致</summary>
        private void RebuildPlanModulesFromCatalog()
        {
            var selectedId = SelectedPlanModule?.Id;
            for (var i = 0; i < PlanModules.Count; i++)
            {
                var fresh = _catalog.FirstOrDefault(m => m.Id == PlanModules[i].Id);
                if (fresh != null)
                    PlanModules[i] = fresh.CreateInstance();
            }
            SelectedPlanModule = selectedId == null
                ? null
                : PlanModules.FirstOrDefault(m => m.Id == selectedId);
        }

        /// <summary>整体测试方案：已选入的测试模块</summary>
        public ObservableCollection<TestModuleViewModel> PlanModules { get; } = new();

        /// <summary>方案列表中当前选中的模块，右侧明细面板据此显示</summary>
        public TestModuleViewModel? SelectedPlanModule
        {
            get => _selectedPlanModule;
            set
            {
                if (SetProperty(ref _selectedPlanModule, value))
                {
                    HasSelectedPlanModule = _selectedPlanModule != null;
                    OnPropertyChanged(nameof(SelectedModuleItems));
                    OnPropertyChanged(nameof(ShowOverviewWaveformButton));
                    SelectedPlanModuleChanged?.Invoke(this, value);
                }
            }
        }

        /// <summary>选中模块变化事件（View 层据此同步方案列表的选中高亮）</summary>
        public event EventHandler<TestModuleViewModel?>? SelectedPlanModuleChanged;

        public ObservableCollection<TestItemViewModel> SelectedModuleItems =>SelectedPlanModule?.Items ?? new ObservableCollection<TestItemViewModel>();

        /// <summary>模块库面板是否展开；展开时按最新配置重建目录</summary>
        public bool IsLibraryVisible
        {
            get => _isLibraryVisible;
            set
            {
                if (value && !_isLibraryVisible)
                    RefreshModuleCatalog();
                SetProperty(ref _isLibraryVisible, value);
            }
        }

        /// <summary>测试方案是否正在运行（运行中禁止再次触发/复位/清空）</summary>
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (!SetProperty(ref _isRunning, value)) return;

                // 方案跑完是后台回调落回 UI 线程，没有输入事件，不补这一句那一排按钮会一直灰着等人去点别处
                RequeryCommands();
            }
        }

        public TestResultStatus PlanResult
        {
            get
            {
                if (PlanModules.Any(m => m.ModuleResult == TestResultStatus.Fail))
                {
                    return TestResultStatus.Fail;
                }

                return PlanModules.Count > 0 &&
                       PlanModules.All(m => m.ModuleResult == TestResultStatus.Pass)
                    ? TestResultStatus.Pass
                    : TestResultStatus.NotTested;
            }
        }

        public string PlanResultText => PlanResult switch
        {
            TestResultStatus.Pass => "PASS",
            TestResultStatus.Fail => "NG",
            _ => "未测试"
        };

        /// <summary>右侧明细面板是否有内容可显示</summary>
        private bool _hasSelectedPlanModule;
        public bool HasSelectedPlanModule
        {
            get => _hasSelectedPlanModule;
            private set => SetProperty(ref _hasSelectedPlanModule, value);
        }

        public string PlanSummaryText => $"共 {PlanModules.Count} 个模块";

        public ICommand AddToPlanCommand { get; }
        public ICommand RemoveFromPlanCommand { get; }
        public ICommand ClearPlanCommand { get; }
        public ICommand RunOrStopCommand { get; }
        public ICommand ResetPlanCommand { get; }
        public ICommand ToggleLibraryCommand { get; }
        public ICommand OpenWholeMachinePageCommand { get; }
        public ICommand OpenBoardPageCommand { get; }
        public ICommand OpenFlashingPageCommand { get; }
        public ICommand BackToEntryCommand { get; }
        public ICommand SavePlanCommand { get; }
        public ICommand LoadPlanCommand { get; }
        public ICommand SaveReportCommand { get; }

        /// <summary>入库失败重试命令（快照在内存里重放；没有滞留快照时不可用）</summary>
        public ICommand RetrySaveCommand { get; }

        /// <summary>是否有滞留的未入库快照（入库失败过）：界面上「重试保存」按钮的可见性绑它</summary>
        public bool HasPendingSave => _pendingSaveSnapshot != null;

        /// <summary>当前加载的方案名称</summary>
        public string CurrentPlanName
        {
            get => _currentPlanName;
            private set
            {
                if (SetProperty(ref _currentPlanName, value))
                {
                    OnPropertyChanged(nameof(PlanTitleText));
                }
            }
        }

        /// <summary>方案标题显示（直接显示方案名，未保存/加载方案时显示未命名方案）</summary>
        public string PlanTitleText => string.IsNullOrEmpty(CurrentPlanName)? "未命名方案": CurrentPlanName;

        /// <summary>将模块库中的模块加入测试方案（拖拽 / 双击共用入口）；同一模块只允许加入一次。</summary>
        public void AddModuleToPlan(TestModuleViewModel? libraryModule, int insertIndex = -1)
        {
            _log.Info($"{DateTime.Now} Start calling the MainViewModel.AddModuleToPlan() method . {Environment.NewLine}");
            try
            {
                // 运行中不允许修改方案
                if (libraryModule == null || IsRunning)
                {
                    return;
                }

                // 已在方案中则只做选中定位，不重复添加
                var existing = PlanModules.FirstOrDefault(m => m.Id == libraryModule.Id);
                if (existing != null)
                {
                    SelectedPlanModule = existing;
                    return;
                }

                var instance = libraryModule.CreateInstance();
                if (insertIndex < 0 || insertIndex > PlanModules.Count)
                {
                    PlanModules.Add(instance);
                }
                else
                {
                    PlanModules.Insert(insertIndex, instance);
                }

                SelectedPlanModule = instance;
                OnPropertyChanged(nameof(PlanSummaryText));
                NotifyPlanResultChanged();
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in MainViewModel.class AddModuleToPlan method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the MainViewModel.AddModuleToPlan() method . {Environment.NewLine}");
            }
        }

        /// <summary>调整方案内模块顺序</summary>
        public void MovePlanModule(TestModuleViewModel module, int newIndex)
        {
            _log.Info($"{DateTime.Now} Start calling the MainViewModel.MovePlanModule() method . {Environment.NewLine}");
            try
            {
                if (IsRunning)
                {
                    return;
                }

                var oldIndex = PlanModules.IndexOf(module);
                if (oldIndex < 0)
                {
                    return;
                }

                newIndex = Math.Clamp(newIndex, 0, PlanModules.Count - 1);
                if (oldIndex != newIndex)
                {
                    PlanModules.Move(oldIndex, newIndex);
                }

                SelectedPlanModule = module;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in MainViewModel.class MovePlanModule method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the MainViewModel.MovePlanModule() method . {Environment.NewLine}");
            }
        }

        /// <summary>把模块从测试方案中移除</summary>
        private void RemoveModuleFromPlan(TestModuleViewModel? module)
        {
            _log.Info($"{DateTime.Now} Start calling the MainViewModel.RemoveModuleFromPlan() method . {Environment.NewLine}");
            try
            {
                if (module == null)
                {
                    return;
                }

                if (ReferenceEquals(SelectedPlanModule, module))
                {
                    SelectedPlanModule = null;
                }

                PlanModules.Remove(module);
                OnPropertyChanged(nameof(PlanSummaryText));
                NotifyPlanResultChanged();
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in MainViewModel.class RemoveModuleFromPlan method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the MainViewModel.RemoveModuleFromPlan() method . {Environment.NewLine}");
            }
        }

        /// <summary>清空整个测试方案</summary>
        private void ClearPlan()
        {
            _log.Info($"{DateTime.Now} Start calling the MainViewModel.ClearPlan() method . {Environment.NewLine}");
            try
            {
                SelectedPlanModule = null;
                PlanModules.Clear();
                OnPropertyChanged(nameof(PlanSummaryText));
                NotifyPlanResultChanged();
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in MainViewModel.class ClearPlan method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the MainViewModel.ClearPlan() method . {Environment.NewLine}");
            }
        }

        /// <summary>运行/停止切换入口：空闲时启动方案，运行中触发停止（取消令牌）</summary>
        private void RunOrStop()
        {
            _log.Info($"{DateTime.Now} Start calling the MainViewModel.RunOrStop() method . {Environment.NewLine}");
            try
            {
                if (IsRunning)
                {
                    _log.Info("===== 收到停止请求，正在中断测试方案 =====");
                    _runCts?.Cancel();
                    return;
                }

                // 烧录轮跑着时不开测试方案：两边都是现场人手一直在盯着的活，叠着跑只会两边都看不清结果
                if (FlashRunState.IsRunning)
                {
                    _log.Warn("烧录轮正在跑，忽略启动测试方案的请求");
                    return;
                }

                // 单板那一轮也在抢同一批仪器（综测仪/读码器/网关），它跑着时同样不开方案；上面已挡过自己的 IsRunning，所以 TestRunState 这里为真只可能是单板那一轮占着
                if (TestRunState.IsRunning)
                {
                    _log.Warn("单板轮次正在跑，忽略启动测试方案的请求");
                    return;
                }

                RunPlan();
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in MainViewModel.class RunOrStop method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the MainViewModel.RunOrStop() method . {Environment.NewLine}");
            }
        }

        /// <summary>全部测完后统一汇总：任一小项失败即整体 NG；运行中点"停止"会取消令牌，当前模块尽快退出、后续模块不再执行。</summary>
        private async void RunPlan()
        {
            if (IsRunning)
            {
                return;
            }

            _log.Info($"{DateTime.Now} Start calling the MainViewModel.RunPlan() method . {Environment.NewLine}");
            IsRunning = true;
            TestRunState.SetRunning(true);
            _runStartedAt = DateTime.Now;   // 入库快照的 StartedAt
            // 上一轮入库失败滞留的快照会被本轮快照覆盖：先记一笔，别让数据无声消失
            if (_pendingSaveSnapshot != null)
                _log.Warn($"开新一轮将覆盖上一轮未入库的快照（{_pendingSaveSnapshot.Items.Count} 项）——需要留档请先点「重试保存」");
            SetLastSavedReportNo(null);     // 开跑即作废上一轮编号：本轮没入库前「导出报告」必须是灰的，否则导的是上一轮
            _runCts = new CancellationTokenSource();
            _runDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                var token = _runCts.Token;

                // 动态项跟随最新配置
                RefreshModuleCatalog();
                RebuildPlanModulesFromCatalog();

                _testItemService.BeginPlanRun();   // 复位方案级产品通讯状态
                ClearWaveform();   // 新一轮测试清空上一轮波形
                RaiseWaveformOverviewRequested();   // 声明了ECG Signal的模块，每轮开始执行前自动带出长图窗口
                _log.Info($"===== 开始执行测试方案，共 {PlanModules.Count} 个模块 =====");

                // 新一轮测试前先清除上一轮结果
                foreach (var module in PlanModules)
                {
                    module.Reset();
                }
                NotifyPlanResultChanged();

                var snapshot = PlanModules.ToList();
                for (var i = 0; i < snapshot.Count; i++)
                {
                    // 每个模块开始前检查停止：已停止则后续模块不再执行
                    token.ThrowIfCancellationRequested();

                    var module = snapshot[i];
                    module.Sequence = i + 1;   // 执行前再次确认序号 = 实际执行顺序（TestItemxx 的 xx）
                    SelectedPlanModule = module;

                    // 大项用时起点（派发前，含上一项之后的所有动作）；_itemClockAnchor 是逐项耗时锚点，本项耗时 = 本项上报 − 上一项上报（首项即派发）
                    // 人工操作提示的等待时长不算进用时：OnOperatorPromptRequested 返回时把这两个基准一起前移
                    // 批量上报的模块（采完全部轮次再逐项上报）时刻差不可用，由执行器填 TestItemResult.MeasuredSeconds
                    lock (_clockLock)
                    {
                        _moduleClockStart = DateTime.Now;
                        _itemClockAnchor = _moduleClockStart;
                    }

                    async Task OnItemCompleted(string itemName, TestItemResult result)
                    {
                        var reportedAt = DateTime.Now;
                        double seconds;
                        lock (_clockLock)
                        {
                            seconds = (reportedAt - _itemClockAnchor).TotalSeconds;
                            _itemClockAnchor = reportedAt;
                        }
                        var item = module.Items.FirstOrDefault(it => it.Name == itemName);
                        if (item == null) return;
                        // 用可等的 InvokeAsync，不用阻塞式 Invoke：UI 线程忙（正画波形）时会把上报线程硬按住且取消不下来
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            item.ApplyResult(result);
                            item.RunSeconds = result.MeasuredSeconds ?? seconds;
                        });
                        if (item.Result == TestResultStatus.Fail)
                            _log.Warn($"[TestItem{module.Sequence} {module.Name}] {item.Name} = {item.MeasuredValueText} 超规格 {item.SpecText} -> FAIL");
                        // 逐项之间留一拍：让测试员看得见结果是一条条刷出来的，也压住高频上报对 UI 队列的冲击
                        await Task.Delay(100, token);
                    }

                    // 在后台线程执行整个测试模块，UI 线程完全空闲处理渲染
                    var results = await Task.Run(() =>
                        _testItemService.TestItemxx(module.Sequence, module.Model, OnItemCompleted, token), token);
                    FlushWaveform();   // 模块跑完立刻刷出最后一批采样点，波形显示完整

                    // 回调未覆盖的项 判 FAIL
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var item in module.Items)
                        {
                            // 不判定项（预留/仅统计）保持“未测试”，不强制 FAIL
                            if (item.IsReportedNotJudged || item.IsDisplayOnly) continue;
                            if (results.ContainsKey(item.Name) && item.Result != TestResultStatus.NotTested) continue;
                            if (item.Result == TestResultStatus.NotTested)
                            {
                                item.MarkNotReportedAsFail();
                                _log.Warn($"[TestItem{module.Sequence} {module.Name}] {item.Name} 未报告结果，强制判 FAIL");
                            }
                        }
                    });

                    double duration;
                    lock (_clockLock) duration = (DateTime.Now - _moduleClockStart).TotalSeconds;
                    module.RunDurationSeconds = duration;
                    _log.Info($"[TestItem{module.Sequence} {module.Name}] 模块结果：{module.ModuleResultText}（用时 {module.RunDurationSeconds:0.0}s）");
                }

                NotifyPlanResultChanged();
                _log.Info($"===== 方案执行完成，整体判定：{PlanResultText} =====");

                // 完整跑完才自动入库：中止（OperationCanceledException）与半路异常都走另一个分支，不写库，避免半份数据
                await AutoSaveRunAsync();
            }
            catch (OperationCanceledException)
            {
                NotifyPlanResultChanged();
                _log.Warn("===== 测试方案已停止 =====");
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in MainViewModel.class RunPlan method, message: {ex}", ex);
            }
            finally
            {
                if (_simSession is { IsOpen: true })
                {
                    try
                    {
                        var closeTask = _simSession.CloseAsync();
                        if (await Task.WhenAny(closeTask, Task.Delay(5000)).ConfigureAwait(true) != closeTask)
                            _log.Warn("血压模拟仪共享会话关闭超时（5s），已跳过");
                        else
                            await closeTask.ConfigureAwait(true);
                    }
                    catch (Exception ex) { _log.Warn($"血压模拟仪共享会话关闭异常：{ex.Message}"); }
                }

                _runCts?.Dispose();
                _runCts = null;
                IsRunning = false;
                TestRunState.SetRunning(false);
                _log.Info($"{DateTime.Now} Ended calling the MainViewModel.RunPlan() method . {Environment.NewLine}");

                // 收尾全部做完才放行关窗口的等待（前面任何一步抛异常也不能漏这一句）
                _runDone?.TrySetResult();
            }
        }

        // ==================== 测试数据自动入库 ====================

        /// <summary>方案完整跑完后自动入库：采快照 → 单事务写 3 张表；全未测不入库，失败则快照留内存待「重试保存」。</summary>
        private async Task AutoSaveRunAsync()
        {
            try
            {
                var snapshot = BuildRunSnapshot();

                // 整轮没有已测项就不入库：历史列表与区间统计不该出现空轮次
                if (!snapshot.Items.Any(i => !i.NotJudged && !i.DisplayOnly && i.Result != TestResultStatus.NotTested))
                {
                    _log.Warn("本轮没有任何已测项（全部未测试/跳过），不入库");
                    return;
                }

                try
                {
                    // SQLite 没有真异步，整段写库是同步 I/O：不挪出 UI 线程，收尾这几秒整个窗口点不动
                    var result = await Task.Run(() => _testDataService.SaveRunAsync(snapshot));
                    ClearPendingSave();
                    SetLastSavedReportNo(result.ReportNo);
                    Growl.SuccessGlobal(new GrowlInfo
                    {
                        Message = $"已自动保存 {result.PassCount + result.FailCount + result.NotTestedCount} 个测试项，编号 {result.ReportNo}",
                        WaitTime = SaveGrowlSeconds,
                        IsCustom = true,
                    });
                }
                catch (Exception ex)
                {
                    // 这里在收尾链上：弹模态框会把会话释放与 IsRunning 复位一起卡在操作员那一指上，所以只留气泡
                    // ——快照已经留在内存里，操作员什么时候点「重试保存」都行
                    _log.Error($"测试数据入库失败（快照留内存，可点「重试保存」补存）：{ex.Message}", ex);
                    _pendingSaveSnapshot = snapshot;
                    OnPropertyChanged(nameof(HasPendingSave));
                    Growl.ErrorGlobal(new GrowlInfo
                    {
                        Message = $"测试数据入库失败：{ex.Message}\n本轮数据留在内存，点「重试保存」补存（开新一轮会被覆盖）。",
                        WaitTime = PendingSaveGrowlSeconds,
                        IsCustom = true,
                    });
                }
            }
            catch (Exception ex)
            {
                // 采快照阶段出错：只记日志，不再弹框打断收尾流程
                _log.Error($"自动入库采快照异常：{ex.Message}", ex);
            }
        }

        /// <summary>「重试保存」命令：把滞留的快照重新写库，成功即清除滞留态；仍失败则气泡报错、快照继续留着</summary>
        private async void RetrySave()
        {
            var snapshot = _pendingSaveSnapshot;
            if (snapshot == null) return;
            try
            {
                var result = await Task.Run(() => _testDataService.SaveRunAsync(snapshot));
                ClearPendingSave();
                SetLastSavedReportNo(result.ReportNo);
                Growl.SuccessGlobal(new GrowlInfo
                {
                    Message = $"补存成功：编号 {result.ReportNo}，{result.PassCount + result.FailCount + result.NotTestedCount} 个测试项",
                    WaitTime = SaveGrowlSeconds,
                    IsCustom = true,
                });
            }
            catch (Exception ex)
            {
                _log.Error($"补存失败（快照仍留内存）：{ex.Message}", ex);
                Growl.ErrorGlobal(new GrowlInfo
                {
                    Message = $"补存失败：{ex.Message}（可再点「重试保存」）",
                    WaitTime = SaveGrowlSeconds,
                    IsCustom = true,
                });
            }
        }

        private void ClearPendingSave()
        {
            _pendingSaveSnapshot = null;
            OnPropertyChanged(nameof(HasPendingSave));
        }

        /// <summary>最近一次成功入库的报告编号：「导出报告」按钮的可用条件（null=本轮还没存上，按钮置灰）</summary>
        public string? LastSavedReportNo => _lastSavedReportNo;

        /// <summary>入库编号的唯一写入口：编号一变，按钮可用性跟着变（按钮文案固定，不带编号）</summary>
        private void SetLastSavedReportNo(string? reportNo)
        {
            _lastSavedReportNo = reportNo;
            OnPropertyChanged(nameof(LastSavedReportNo));
            RequeryCommands();
        }

        /// <summary>「导出报告」：按最近一次入库的编号导 PDF（数据已在库里，这里只产文档）</summary>
        private async void SaveReport()
        {
            if (_lastSavedReportNo == null) return;
            try
            {
                // 取数段（本轮 + 环比共三次查询）也是同步 SQLite I/O，整段挪出 UI 线程
                var path = await Task.Run(() => _testReportService.ExportRunReportAsync(_lastSavedReportNo));
                Growl.SuccessGlobal(new GrowlInfo
                {
                    Message = $"报告已导出：{path}",
                    WaitTime = SaveGrowlSeconds,
                    IsCustom = true,
                });
            }
            catch (Exception ex)
            {
                _log.Error($"导出测试报告失败（编号 {_lastSavedReportNo}）：{ex.Message}", ex);
                Growl.ErrorGlobal(new GrowlInfo
                {
                    Message = $"报告导出失败：{ex.Message}",
                    WaitTime = SaveGrowlSeconds,
                    IsCustom = true,
                });
            }
        }

        /// <summary>从方案模块树采入库快照：模块/项名/判定/双值/原始文本/规格快照/逐点子表/大项用时全带上（含未测项）；
        /// 保存服务只认快照，不认识 ViewModel。</summary>
        private TestRunSnapshot BuildRunSnapshot()
        {
            var snapshot = new TestRunSnapshot
            {
                Station = "整机",
                PlanName = string.IsNullOrEmpty(CurrentPlanName) ? "未命名方案" : CurrentPlanName,
                ModuleIds = PlanModules.Select(m => m.Id).ToList(),
                ModuleTimings = PlanModules.Where(m => m.RunDurationSeconds.HasValue)
                    .Select(m => new TestModuleTiming { Sequence = m.Sequence, Seconds = m.RunDurationSeconds!.Value })
                    .ToList(),
                MeasureSeconds = ResolveMeasureSeconds(),
                StartedAt = _runStartedAt,
                FinishedAt = DateTime.Now,
            };

            foreach (var module in PlanModules)
            {
                foreach (var item in module.Items)
                {
                    snapshot.Items.Add(new TestItemSnapshot
                    {
                        ModuleId = module.Id,
                        ModuleName = module.Name,
                        Sequence = module.Sequence,
                        ItemName = item.Name,
                        Unit = item.Unit,
                        ValueSource = item.Model.ValueSource,
                        Value = item.MeasuredValue,
                        InstrumentValue = item.InstrumentValue,
                        ProductValue = item.ProductValue,
                        InstrumentText = item.InstrumentRawText,
                        ProductText = item.ProductRawText,
                        // 规格快照：动态规格已解析进上下限（血压类按仪器读回现算），配置变了历史数据仍对得上
                        SpecMin = item.LowerLimit,
                        SpecMax = item.UpperLimit,
                        SpecText = item.SpecText,
                        Result = item.Result,
                        DisplayOnly = item.IsDisplayOnly,
                        NotJudged = item.IsReportedNotJudged,
                        DurationSeconds = item.RunSeconds,
                        Points = item.PointResults.Select(p => new TestPointSnapshot
                        {
                            Name = p.Name,
                            Schema = p.Schema,
                            Unit = p.Unit,
                            TargetPressure = p.TargetPressure,
                            InstrumentValue = p.InstrumentValue,
                            ProductValue = p.ProductValue,
                            Error = p.Error,
                            Pass = p.Pass,
                            Remark = p.Remark,
                            Average = p.Average,
                            Max = p.Max,
                            Min = p.Min,
                            Range = p.Range,
                            Cells = p.Cells,
                            ShowVerdict = p.ShowVerdict,
                        }).ToList(),
                    });
                }
            }

            return snapshot;
        }

        /// <summary>量测时间快照：只有 BPA700 且本轮排了血压静态压力精度测试才有效（康伟达按收敛判据稳压、不读该参数）</summary>
        private int? ResolveMeasureSeconds()
        {
            if (!PlanModules.Any(m => m.Id == TestModuleIds.BpStaticAccuracyTest)) return null;
            var config = _configService.Load();
            return config.ActiveSimulator == SimulatorType.BPA700
                ? config.ActiveBpTestParams.StaticPressure.MeasureSeconds
                : null;
        }

        /// <summary>关窗口前调用：运行中先请求停止</summary>
        public void StopAndAwaitWrapUp(int timeoutMs = 10_000)
        {
            if (!IsRunning || _awaitingWrapUp) return;   // 消息泵期间再进来一次（比如又点一次关闭）直接返回，不叠消息泵
            _awaitingWrapUp = true;

            try
            {
                _log.Info("===== 关闭窗口：先请求停止，等测试项收尾 =====");
                _runCts?.Cancel();

                var pending = _runDone;
                if (pending == null || pending.Task.IsCompleted) return;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                // 收尾靠 UI 线程继续跑，只能泵消息等它；两个回调都排回 UI 线程执行，因为 UI 线程没进泵循环之前 Invoke 不会返回，顺带避开“退出标志比 PushFrame 先生效”的抢时间问题
                var frame = new DispatcherFrame();
                pending.Task.ContinueWith(_ => dispatcher.Invoke(() => frame.Continue = false), TaskScheduler.Default);
                _ = Task.Delay(timeoutMs).ContinueWith(_ => dispatcher.Invoke(() => frame.Continue = false), TaskScheduler.Default);
                Dispatcher.PushFrame(frame);   // 静态方法：当前就在 UI 线程上泵消息

                if (IsRunning)
                    _log.Warn($"测试项收尾等待 {timeoutMs / 1000}s 仍未完成，不阻拦退出；设备可能还停在测试模式，建议手动停气/断电");
            }
            finally
            {
                _awaitingWrapUp = false;
            }
        }

        /// <summary>清除方案内所有模块的测试值</summary>
        private void ResetPlan()
        {
            _log.Info($"{DateTime.Now} Start calling the MainViewModel.ResetPlan() method . {Environment.NewLine}");
            try
            {
                foreach (var module in PlanModules)
                {
                    module.Reset();
                }

                NotifyPlanResultChanged();
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in MainViewModel.class ResetPlan method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the MainViewModel.ResetPlan() method . {Environment.NewLine}");
            }
        }

        /// <summary>刷新整体判定显示</summary>
        private void NotifyPlanResultChanged()
        {
            OnPropertyChanged(nameof(PlanResult));
            OnPropertyChanged(nameof(PlanResultText));
        }

        // ==================== 页面切换（工位选择页 ↔ 三个工位） ====================


        private bool CanSwitchPage() => !IsRunning && !_flashing.IsRunning && !_board.IsRunning;

        private void SwitchPage(WorkbenchPage target)
        {
            _log.Info($"{DateTime.Now} Start calling the MainViewModel.SwitchPage() method . {Environment.NewLine}");
            try
            {
                if (target == SelectedPage || target == WorkbenchPage.Entry) return;

                if (!CanSwitchPage())
                {
                    _log.Warn($"测试方案、单板轮次或烧录轮正在跑，忽略进入{target.ToDisplay()}的请求");
                    return;
                }

                SelectedPage = target;
                _lastStation = target;
                _appState.SaveLastPage(target);
                OnPropertyChanged(nameof(LastStationHintText));

                _log.Info($"已进入{target.ToDisplay()}：模块库 {LibraryModules.Count} 项，方案 {PlanModules.Count} 个模块（各页东西互不清）");
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the MainViewModel.SwitchPage() method . {Environment.NewLine}");
            }
        }

        /// <summary>从工位页回选择页</summary>
        private void BackToEntry()
        {
            if (SelectedPage == WorkbenchPage.Entry) return;

            if (!CanSwitchPage())
            {
                _log.Warn("这一轮还在跑，先停下再回工位选择页");
                return;
            }

            var from = SelectedPage;
            SelectedPage = WorkbenchPage.Entry;
            _log.Info($"已从{from.ToDisplay()}页回到工位选择页");
        }

        /// <summary>加载方案后确实换了一层界面：这里只刷标题与列表</summary>
        private void ApplyPlanCategory(ModuleCategory category)
        {
            if (category == ModuleCategory.Board)
                _log.Warn("这仍是一份单板方案：单板页现在按通道跑固定几项，这份方案里的模块仍会在整机页里整批跑");

            OnPropertyChanged(nameof(LibraryCategoryText));
            OnPropertyChanged(nameof(LibraryTitleText));
        }

        // ==================== 方案保存/加载 ====================

        /// <summary>保存当前方案为 JSON</summary>
        private void SavePlan()
        {
            SavePlanRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>方案保存请求事件</summary>
        public event EventHandler? SavePlanRequested;

        /// <summary>按指定名称保存当前方案</summary>
        public void SavePlanWithName(string planName)
        {
            _log.Info($"{DateTime.Now} Start calling the MainViewModel.SavePlanWithName() method . {Environment.NewLine}");
            try
            {
                if (string.IsNullOrWhiteSpace(planName) || PlanModules.Count == 0)
                {
                    return;
                }

                var config = new TestPlanConfig
                {
                    PlanName = planName,
                    // 存下所属工位：下次从另一面看到这名字也知道不是自己这一摊，方案列表也按它过滤
                    Category = _libraryCategory,
                    ModuleIds = PlanModules.Select(m => m.Id).ToList()
                };

                _testPlanService.SavePlan(config);
                CurrentPlanName = planName;
                _log.Info($"方案 [{planName}] 已保存，共 {PlanModules.Count} 个模块");
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in MainViewModel.class SavePlanWithName method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the MainViewModel.SavePlanWithName() method . {Environment.NewLine}");
            }
        }

        /// <summary>加载方案</summary>
        private void LoadPlan()
        {
            LoadPlanRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>方案加载请求事件</summary>
        public event EventHandler? LoadPlanRequested;

        public IReadOnlyList<string> GetAvailablePlanNames()
        {
            // 要知道每份方案属于哪一类就得拿全量目录查，这时还没走到加载那一步，先把快照建好
            BuildCatalog();
            return _testPlanService.GetAllPlans()
                .Where(kv => ResolvePlanCategory(kv.Value) == _libraryCategory)
                .Select(kv => kv.Key)
                .OrderBy(name => name)
                .ToList();
        }

        /// <summary>方案属于哪个工位：存盘字段优先，缺失就按模块在全量目录里查到的类别推断，还查不出来就算当前这一面</summary>
        private ModuleCategory ResolvePlanCategory(TestPlanConfig config)
            => config.Category
               ?? config.ModuleIds.Select(TestModuleIds.NormalizeLegacyId)
                                  .Select(id => _catalog.FirstOrDefault(m => m.Id == id)?.Category)
                                  .FirstOrDefault(c => c.HasValue)
               ?? _libraryCategory;

        /// <summary>别的工位类别是否已占用这个名字（View 保存前问一句）：方案名就是文件名，整机/单板同名会互相覆盖、两边界面看不出关联，撞名很难查，所以拦一下。</summary>
        public bool IsPlanNameTakenByOtherCategory(string planName)
        {
            if (string.IsNullOrWhiteSpace(planName)) return false;

            BuildCatalog();
            return _testPlanService.GetAllPlans()
                .Where(kv => ResolvePlanCategory(kv.Value) != _libraryCategory)
                .Any(kv => string.Equals(kv.Key, planName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(kv.Value.PlanName, planName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>按名称加载方案</summary>
        public void LoadPlanWithName(string planName)
        {
            _log.Info($"{DateTime.Now} Start calling the MainViewModel.LoadPlanWithName() method . {Environment.NewLine}");
            try
            {
                if (string.IsNullOrWhiteSpace(planName))
                {
                    return;
                }

                var config = _testPlanService.LoadPlan(planName);
                if (config == null)
                {
                    _log.Warn($"方案 [{planName}] 加载失败：文件不存在或格式错误");
                    return;
                }

                // 按最新配置重建全量目录（推断方案类别、按 Id 取模块都靠它）
                BuildCatalog();

                // 老方案文件里存的是已弃用的 M 编号：读入时映射成同尾号的 A，下次保存自然升级
                var moduleIds = config.ModuleIds.Select(TestModuleIds.NormalizeLegacyId).ToList();
                var planCategory = ResolvePlanCategory(config);

                // 加载触发的换面走程序路径
                ApplyPlanCategory(planCategory);
                FillLibraryModules();

                // 清空当前方案
                SelectedPlanModule = null;
                PlanModules.Clear();

                // 按配置顺序从全量目录中查找并加入方案
                foreach (var moduleId in moduleIds)
                {
                    var module = _catalog.FirstOrDefault(m => m.Id == moduleId);
                    if (module == null)
                    {
                        _log.Warn($"模块 [{moduleId}] 在模块库中不存在，已跳过");
                        continue;
                    }

                    // 手改文件塞进了另一类工位的模块：只装本类的，其余跳过，免得一份方案跨两套治具
                    if (module.Category != planCategory)
                    {
                        _log.Warn($"模块 [{moduleId}] 属于{module.Category.ToDisplay()}，与方案所属的{planCategory.ToDisplay()}不是同一工位，已跳过");
                        continue;
                    }

                    PlanModules.Add(module.CreateInstance());
                }

                CurrentPlanName = planName;
                _log.Info($"方案 [{planName}] 已加载（{planCategory.ToDisplay()}），共 {PlanModules.Count} 个模块");
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in MainViewModel.class LoadPlanWithName method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the MainViewModel.LoadPlanWithName() method . {Environment.NewLine}");
            }
        }

        /// <summary>启动时自动加载原有使用的方案</summary>
        private void AutoLoadLastPlan()
        {
            _log.Info($"{DateTime.Now} Start calling the MainViewModel.AutoLoadLastPlan() method . {Environment.NewLine}");
            try
            {
                var plans = _testPlanService.GetPlanNames();
                if (plans.Count == 1)
                {
                    // 只有一个方案时直接加载
                    LoadPlanWithName(plans[0]);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in MainViewModel.class AutoLoadLastPlan method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the MainViewModel.AutoLoadLastPlan() method . {Environment.NewLine}");
            }
        }
    }
}
