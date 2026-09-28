using GongSolutions.Wpf.DragDrop;
using HandyControl.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using TORNPISolution.Common;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel：
    /// 模块库（左） → 拖拽组建测试方案（中） → 查看模块测试明细（右）
    /// 拖放由 gong-wpf-dragdrop 接管，本类实现 IDropTarget 处理落点逻辑。
    /// 规格在 Spec 类、测量在 TestItem 类，本类只负责调度与结果展示。
    /// </summary>
    public class MainViewModel : ViewModelBase, IDropTarget
    {
        private readonly ILogService _log = new LogService(nameof(MainViewModel));
        private readonly ITestItemService _testItemService;
        private readonly ITestPlanService _testPlanService;
        private readonly ITestModuleService _moduleService;
        private TestModuleViewModel? _selectedPlanModule;
        private bool _isLibraryVisible = false;
        private bool _isRunning;
        private string _currentPlanName = string.Empty;
        // 运行期间的取消令牌：点"停止"时 Cancel
        private CancellationTokenSource? _runCts;

        /// <summary>血压模拟仪方案级共享会话（单例）：RunPlan 开始建立、结束关闭，模块间不复关复开串口</summary>
        private readonly BloodPressureSimulatorSession? _simSession;

        // XAML <vm:MainViewModel/> 要求无参构造：依赖从 App 组合根解析（Spec/TestItem 均为单例，事件订阅同一实例）
        public MainViewModel()
            : this(App.Services.GetRequiredService<ITestModuleService>(),
                   App.Services.GetRequiredService<ITestItemService>(),
                   App.Services.GetRequiredService<ITestPlanService>())
        {
        }

        public MainViewModel(ITestModuleService moduleService, ITestItemService testItemService, ITestPlanService? testPlanService = null)
        {
            _testItemService = testItemService;
            _moduleService = moduleService;
            _testPlanService = testPlanService ?? App.Services.GetRequiredService<ITestPlanService>();
            try { _simSession = App.Services.GetRequiredService<BloodPressureSimulatorSession>(); }
            catch { /* 设计期上下文等容器不可用时忽略，RunPlan 不建共享会话 */ }

            AvailableModules = new ObservableCollection<TestModuleViewModel>(
                moduleService.GetAllModules().Select(m => new TestModuleViewModel(m)));

            AddToPlanCommand = new RelayCommand<TestModuleViewModel>(m => AddModuleToPlan(m));
            RemoveFromPlanCommand = new RelayCommand<TestModuleViewModel>(RemoveModuleFromPlan, _ => !IsRunning);
            ClearPlanCommand = new RelayCommand(ClearPlan, () => PlanModules.Count > 0 && !IsRunning);
            // 运行/停止共用一个命令：空闲时启动方案，运行中取消（按钮文字随 IsRunning 切换）
            RunOrStopCommand = new RelayCommand(RunOrStop, () => PlanModules.Count > 0 || IsRunning);
            ResetPlanCommand = new RelayCommand(ResetPlan, () => PlanModules.Count > 0 && !IsRunning);
            ToggleLibraryCommand = new RelayCommand(() => IsLibraryVisible = !IsLibraryVisible);
            SavePlanCommand = new RelayCommand(SavePlan, () => PlanModules.Count > 0 && !IsRunning);
            LoadPlanCommand = new RelayCommand(LoadPlan, () => !IsRunning);

            // 方案增删/移动时统一刷新执行序号，保证序号始终跟随数据顺序
            PlanModules.CollectionChanged += (_, _) => RefreshSequences();

            // BP-001 压力采样点实时上抛 → 收集后节流刷新波形图
            _testItemService.PressureSampled += OnPressureSampled;

            // 设备连接失败 → 弹出 HandyControl Growl 通知
            _testItemService.DeviceConnectionFailed += OnDeviceConnectionFailed;

            // 启动时自动加载上次使用的方案
            AutoLoadLastPlan();
        }

        // ==================== 测试项压力波形（每个带波形的测试项独立采集/展开） ====================

        private readonly object _waveformLock = new();
        private readonly Dictionary<string, List<PressureSamplePoint>> _waveformBuffers = new();
        private DateTime _lastWaveformFlush = DateTime.MinValue;

        /// <summary>压力采样点回调（后台线程，带测试项名）：按项名入缓存，最多 150ms 节流刷一次 UI</summary>
        private void OnPressureSampled(string itemName, PressureSamplePoint point)
        {
            lock (_waveformLock)
            {
                if (!_waveformBuffers.TryGetValue(itemName, out var list))
                {
                    list = new List<PressureSamplePoint>();
                    _waveformBuffers[itemName] = list;
                }

                list.Add(point);
            }

            var now = DateTime.UtcNow;
            if ((now - _lastWaveformFlush).TotalMilliseconds < 150)
            {
                return;
            }

            _lastWaveformFlush = now;
            FlushWaveform();
        }

        /// <summary>把各测试项的采样点快照刷到对应 TestItemViewModel（调度到主线程，按项名匹配）</summary>
        private void FlushWaveform()
        {
            Dictionary<string, PressureSamplePoint[]> snapshots;
            lock (_waveformLock)
            {
                snapshots = _waveformBuffers.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
            }

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                foreach (var module in PlanModules)
                {
                    foreach (var item in module.Items)
                    {
                        if (item.HasWaveform && snapshots.TryGetValue(item.Name, out var pts))
                        {
                            item.WaveformSamples = pts;
                        }
                    }
                }
            });
        }

        /// <summary>清空波形数据（每轮运行开始前调用：缓存与各测试项的采样点快照）</summary>
        private void ClearWaveform()
        {
            lock (_waveformLock)
            {
                _waveformBuffers.Clear();
            }

            foreach (var module in PlanModules)
            {
                foreach (var item in module.Items)
                {
                    if (item.HasWaveform)
                    {
                        item.WaveformSamples = Array.Empty<PressureSamplePoint>();
                    }
                }
            }
        }

        /// <summary>设备连接失败通知：弹出 HandyControl Growl 错误提示</summary>
        private void OnDeviceConnectionFailed(string moduleName, string message)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                Growl.ErrorGlobal($"【{moduleName}】{message}");
            });
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
            dropInfo.Effects = ReferenceEquals(dropInfo.DragInfo?.SourceCollection, AvailableModules)
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

            if (ReferenceEquals(dropInfo.DragInfo?.SourceCollection, AvailableModules))
            {
                AddModuleToPlan(module, insertIndex);
                return;
            }

            var oldIndex = PlanModules.IndexOf(module);
            if (oldIndex >= 0 && oldIndex < insertIndex)
            {
                // 先移除后插入的语义下，向后移动时目标位置需回退一位
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

        /// <summary>模块库：所有可用的测试模块（模板）</summary>
        public ObservableCollection<TestModuleViewModel> AvailableModules { get; }

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
                    SelectedPlanModuleChanged?.Invoke(this, value);
                }
            }
        }

        /// <summary>选中模块变化事件（View 层据此同步方案列表的选中高亮）</summary>
        public event EventHandler<TestModuleViewModel?>? SelectedPlanModuleChanged;

        /// <summary>
        /// 当前选中模块的测试项集合（右侧明细表直接绑定此拍平属性，
        /// 不用 SelectedPlanModule.Items 深层路径，避免选中切换时路径绑定失效）
        /// </summary>
        public ObservableCollection<TestItemViewModel> SelectedModuleItems =>
            SelectedPlanModule?.Items ?? new ObservableCollection<TestItemViewModel>();

        /// <summary>模块库面板是否展开（仅配置方案时需要展开）</summary>
        public bool IsLibraryVisible
        {
            get => _isLibraryVisible;
            set => SetProperty(ref _isLibraryVisible, value);
        }

        /// <summary>测试方案是否正在运行（运行中禁止再次触发/复位/清空）</summary>
        public bool IsRunning
        {
            get => _isRunning;
            private set => SetProperty(ref _isRunning, value);
        }

        /// <summary>
        /// 整体方案判定：任一模块 FAIL 则 NG，全部 PASS 则 PASS，否则未测试
        /// </summary>
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

        /// <summary>整体判定显示文本</summary>
        public string PlanResultText => PlanResult switch
        {
            TestResultStatus.Pass => "PASS",
            TestResultStatus.Fail => "NG",
            _ => "未测试"
        };

        /// <summary>
        /// 右侧明细面板是否有内容可显示：独立标志手动控制，仅在选中模块
        /// 变化时更新，UI 重新求值时直接读标志，不再依赖计算属性
        /// </summary>
        private bool _hasSelectedPlanModule;
        public bool HasSelectedPlanModule
        {
            get => _hasSelectedPlanModule;
            private set => SetProperty(ref _hasSelectedPlanModule, value);
        }

        /// <summary>方案概要文本</summary>
        public string PlanSummaryText => $"共 {PlanModules.Count} 个模块";

        public ICommand AddToPlanCommand { get; }
        public ICommand RemoveFromPlanCommand { get; }
        public ICommand ClearPlanCommand { get; }
        public ICommand RunOrStopCommand { get; }
        public ICommand ResetPlanCommand { get; }
        public ICommand ToggleLibraryCommand { get; }
        public ICommand SavePlanCommand { get; }
        public ICommand LoadPlanCommand { get; }

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
        public string PlanTitleText => string.IsNullOrEmpty(CurrentPlanName)
            ? "未命名方案"
            : CurrentPlanName;

        /// <summary>
        /// 将模块库中的模块加入测试方案（拖拽 / 双击共用入口）。
        /// 同一模块只允许加入一次；insertIndex 为 -1 时追加到末尾。
        /// </summary>
        public void AddModuleToPlan(TestModuleViewModel? libraryModule, int insertIndex = -1)
        {
            _log.Info($"{DateTime.Now} Start calling the MainViewModel.AddModuleToPlan() method . {Environment.NewLine}");
            try
            {
                // 运行中不允许修改方案（拖入/双击共用此入口）
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

        /// <summary>调整方案内模块顺序（方案列表内拖拽排序）</summary>
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

        /// <summary>
        /// 运行整体测试方案（异步）：按方案顺序逐个模块执行，失败不中断，
        /// 全部测完后统一汇总：任一小项失败即整体 NG。
        /// 异步执行期间 UI 不卡顿，可实时看到每个模块的结果逐个刷新。
        /// 具体测量逻辑在 TestItem 类里，按方案拖拽排序的执行序号逐模块调用（TestItemxx）。
        /// 运行中点"停止"会取消令牌：当前模块尽快退出，后续模块不再执行。
        /// </summary>
        private async void RunPlan()
        {
            if (IsRunning)
            {
                return;
            }

            _log.Info($"{DateTime.Now} Start calling the MainViewModel.RunPlan() method . {Environment.NewLine}");
            IsRunning = true;
            TestRunState.SetRunning(true);   // 全局运行态：配置页运行期锁定编辑/保存
            _runCts = new CancellationTokenSource();
            var token = _runCts.Token;
            _testItemService.BeginPlanRun();   // 复位方案级产品通讯状态：本轮产品可用性由 M16 初始化模块记录
            ClearWaveform();   // 新一轮测试清空上一轮波形
            _log.Info($"===== 开始执行测试方案，共 {PlanModules.Count} 个模块 =====");

            // 血压模拟仪共享会话为懒建立：第一个真正需要它的血压模块借用时才打开，
            // 纯 NFC/ECG 等方案不碰这台设备；此处不主动 Open，只保证结束时 Close

            try
            {
                // 新一轮测试前先清除上一轮结果
                foreach (var module in PlanModules)
                {
                    module.Reset();
                }
                NotifyPlanResultChanged();

                //集合被意外修改也不会打断执行：取快照按当前位置逐模块执行，
                // 执行序号 = 快照中的位置（与界面显示序号强绑定，不会错乱）
                var snapshot = PlanModules.ToList();
                for (var i = 0; i < snapshot.Count; i++)
                {
                    // 每个模块开始前检查停止：已停止则后续模块不再执行
                    token.ThrowIfCancellationRequested();

                    var module = snapshot[i];
                    module.Sequence = i + 1;   // 执行前再次确认序号 = 实际执行顺序（TestItemxx 的 xx）
                    SelectedPlanModule = module;

                    async Task OnItemCompleted(string itemName, TestItemResult result)
                    {
                        var item = module.Items.FirstOrDefault(it => it.Name == itemName);
                        if (item == null) return;   // 不在 Spec 中的项忽略
                        Application.Current.Dispatcher.Invoke(() => item.ApplyResult(result));
                        if (item.Result == TestResultStatus.Fail)
                            _log.Warn($"[TestItem{module.Sequence} {module.Name}] {item.Name} = {item.MeasuredValueText} 超规格 {item.SpecText} -> FAIL");
                        // 关键：给 UI 线程留出渲染时间，确保 DataGrid 显示完当前结果再开始下一项
                        await Task.Delay(300, token);
                    }

                    // 在后台线程执行整个测试模块，UI 线程完全空闲处理渲染
                    var results = await Task.Run(() =>
                        _testItemService.TestItemxx(module.Sequence, module.Model, OnItemCompleted, token), token);
                    FlushWaveform();   // 模块跑完立刻刷出最后一批采样点，波形显示完整

                    // 回调未覆盖的项（模块方法未写入结果的项）强制判 FAIL（需回 UI 线程）
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var item in module.Items)
                        {
                            if (item.IsReportedNotJudged) continue;   // 不判定项（演示/预留）保持“未测试”，不强制 FAIL
                            if (results.ContainsKey(item.Name) && item.Result != TestResultStatus.NotTested) continue;
                            if (item.Result == TestResultStatus.NotTested)
                            {
                                item.MarkNotReportedAsFail();
                                _log.Warn($"[TestItem{module.Sequence} {module.Name}] {item.Name} 未报告结果，强制判 FAIL");
                            }
                        }
                    });

                    _log.Info($"[TestItem{module.Sequence} {module.Name}] 模块结果：{module.ModuleResultText}");
                }

                NotifyPlanResultChanged();
                _log.Info($"===== 方案执行完成，整体判定：{PlanResultText} =====");
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
                // 方案结束才关闭共享会话（本方案若从未懒建立过则是空操作；此时康伟达发 0x00 归还本地控制）；限时 5s 防读循环收尾卡死。
                // 必须 await（不能用 .GetAwaiter().GetResult() 同步阻塞）——finally 在 UI 线程上执行，同步阻塞会冻结界面导致点不动
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

                _runCts.Dispose();
                _runCts = null;
                IsRunning = false;
                TestRunState.SetRunning(false);
                _log.Info($"{DateTime.Now} Ended calling the MainViewModel.RunPlan() method . {Environment.NewLine}");
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

        // ==================== 方案保存/加载 ====================

        /// <summary>保存当前方案为 JSON（由 View 层提供方案名称输入）</summary>
        private void SavePlan()
        {
            // 实际保存逻辑由 SavePlanWithName 方法完成，View 层调用
            // 这里触发一个事件让 View 弹出输入框
            SavePlanRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>方案保存请求事件（View 层监听并弹出名称输入对话框）</summary>
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

        /// <summary>加载方案（由 View 层提供方案名称选择）</summary>
        private void LoadPlan()
        {
            // 实际加载逻辑由 LoadPlanWithName 方法完成，View 层调用
            LoadPlanRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>方案加载请求事件（View 层监听并弹出选择对话框）</summary>
        public event EventHandler? LoadPlanRequested;

        /// <summary>获取所有已保存的方案名称</summary>
        public IReadOnlyList<string> GetAvailablePlanNames()
        {
            return _testPlanService.GetPlanNames();
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

                // 清空当前方案
                SelectedPlanModule = null;
                PlanModules.Clear();

                // 按配置顺序从模块库中查找并加入方案
                foreach (var moduleId in config.ModuleIds)
                {
                    var module = AvailableModules.FirstOrDefault(m => m.Id == moduleId);
                    if (module != null)
                    {
                        var instance = module.CreateInstance();
                        PlanModules.Add(instance);
                    }
                    else
                    {
                        _log.Warn($"模块 [{moduleId}] 在模块库中不存在，已跳过");
                    }
                }

                CurrentPlanName = planName;
                _log.Info($"方案 [{planName}] 已加载，共 {PlanModules.Count} 个模块");
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

        /// <summary>启动时自动加载上次使用的方案</summary>
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
                // 多个方案时由用户手动选择加载
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
