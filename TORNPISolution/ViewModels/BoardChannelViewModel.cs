using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TORNPISolution.Common;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Service.Flashing;
using TORNPISolution.Share.Enum;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 单板测试页：八条通道各一张卡，跑哪几项在菜单「测试项」里勾（勾一次八条一起跑），也可右键单跑一路。
    /// 一轮只跑一遍：读码器/网关/综测仪是整批共用的设备，两路各跑一半会互抢连接；不在本轮范围里的通道既不驱动也不上报。
    /// 勾选项一改就写 board-test.json：这组勾选是本机的作业定义，不自动落盘会丢。
    /// </summary>
    public class BoardChannelViewModel : ViewModelBase
    {
        /// <summary>一轮里模块的执行顺序：先给板子供电，再取码，最后测蓝牙（顺序错了蓝牙根本起不来）</summary>
        private static readonly string[] ModuleOrder =
        {
            TestModuleIds.BoardPowerOn,
            TestModuleIds.BoardBarcodeRead,
            TestModuleIds.BoardBluetoothTest,
        };

        private readonly IBoardTestConfigService _configService;
        private readonly ITestModuleService _moduleService;
        private readonly ITestItemService _testItem;
        private readonly IInstrumentConfigService _instrumentConfig;
        private readonly ILogService _log = new LogService(nameof(BoardChannelViewModel));

        private readonly Dictionary<int, BoardChannelCardViewModel> _cardsByChannel = new();

        /// <summary>项名 → 落在哪张卡的哪一行，执行端只报项名（带"-槽位号"后缀）时靠这张表回查；Card 为 null = 整批级的那几项（项名无后缀），行放在 <see cref="BatchItems"/> 里。</summary>
        private readonly Dictionary<string, (BoardChannelCardViewModel? Card, TestItemViewModel Item)> _rowsByName = new();

        private readonly Dictionary<int, Stopwatch> _cardWatches = new();

        private BoardTestConfig _config = new();
        private CancellationTokenSource? _runCts;
        private TaskCompletionSource? _runDone;
        private bool _awaitingWrapUp;
        private Stopwatch? _roundWatch;

        /// <summary>本轮点名了哪几路（右键单跑就一个）；空 = 还没开过轮。没点名的路不吃“没设备”那句提示</summary>
        private List<int> _roundChannels = new();

        private bool _isRunning;
        private BoardChannelCardViewModel? _selectedCard;
        private string _roundId = string.Empty;
        private string _statusText = string.Empty;
        private int _successCount;
        private int _failedCount;
        private int _idleCount;

        public BoardChannelViewModel(IBoardTestConfigService configService, ITestModuleService moduleService,
                                     ITestItemService testItem, IInstrumentConfigService instrumentConfig)
        {
            _configService = configService;
            _moduleService = moduleService;
            _testItem = testItem;
            _instrumentConfig = instrumentConfig;

            LoadConfig();

            RunAllCommand = new RelayCommand(RunAll, () => !IsRunning && !TestRunState.IsRunning && !FlashRunState.IsRunning);
            StopAllCommand = new RelayCommand(StopRun, () => IsRunning);
            ResetResultsCommand = new RelayCommand(ResetResults, () => !IsRunning);
            CopyTraceCommand = new RelayCommand(CopyTraceToClipboard, () => SelectedCard != null);
            CheckAllChannelsCommand = new RelayCommand(() => SetAllChannelsSelected(true), () => !IsRunning);
            UncheckAllChannelsCommand = new RelayCommand(() => SetAllChannelsSelected(false), () => !IsRunning);

            // 执行端的设备连接失败与人工提示在这一页同样要落到底下那一行，不然现场只看到卡片变红不知道卡在哪
            _testItem.DeviceConnectionFailed += (module, message) =>
                OnUi(() => StatusText = $"【{module}】{message}");
        }

        // ==================== 配置与通道清单 ====================

        /// <summary>菜单「测试项」里的勾选项：显示名与是否勾选，改一下就落盘</summary>
        public ObservableCollection<BoardTestItemOption> SelectedItems { get; } = new();

        /// <summary>整批级项目的行：这类项按整批判，报出来的名没有"-槽位号"后缀、挂不到任何一张通道卡上，所以单独摆一行。</summary>
        public ObservableCollection<TestItemViewModel> BatchItems { get; } = new();

        public bool HasBatchItems => BatchItems.Count > 0;

        /// <summary>通道卡片清单：行数就是配置里的通道行数</summary>
        public ObservableCollection<BoardChannelCardViewModel> Channels { get; } = new();

        /// <summary>勾选项汇总，页头显示本轮跑几项</summary>
        public string SelectedItemsText =>
            SelectedItems.Count(o => o.Selected) == 0
                ? "没勾任何测试项"
                : $"本轮跑 {SelectedItems.Count(o => o.Selected)} 项：{string.Join("、", SelectedItems.Where(o => o.Selected).Select(o => o.Name))}";

        /// <summary>从盘上读回勾选项与通道清单：只在构造时走一次，之后改配置由 ApplySavedConfig 就地刷新（重建卡片会把不落盘的「本轮跑」抹回全跑）</summary>
        public void LoadConfig()
        {
            _config = _configService.Load();

            SelectedItems.Clear();
            // 项名背后再念一句属于哪个模块：项数一多（几十项）没有这句就看不出哪几项是同一次采集带出来的
            var modules = _moduleService.GetAllModules();
            foreach (var kind in Enum.GetValues<BoardTestItemKind>())
            {
                var moduleId = kind.ModuleId();
                var moduleName = modules.FirstOrDefault(m => m.Id == moduleId)?.Name ?? moduleId;
                SelectedItems.Add(new BoardTestItemOption(kind, _config.SelectedItems.Contains(kind), OnItemCheckedChanged, moduleName));
            }

            Channels.Clear();
            _cardsByChannel.Clear();
            _selectedCard = null;
            // 设备落点按槽位算，整份配置只读一次：逐路 Load 就是八路各反序列化一遍同样的东西
            var instrumentConfig = SafeLoadInstruments();
            for (var i = 0; i < _config.Channels.Count; i++)
            {
                var card = new BoardChannelCardViewModel(i + 1, _config.Channels[i])
                {
                    DeviceText = DescribeChannelDevices(i + 1, instrumentConfig),
                };
                Channels.Add(card);
                _cardsByChannel[card.Channel] = card;
            }

            SelectedCard = Channels.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedItemsText));
            // 一进来就把每条通道将要跑的那几项摆出来：开跑前下方不该是一片空白
            RebuildItemRows();
        }

        /// <summary>勾选项变化：立刻落盘，下次开机还是这一组</summary>
        private void OnItemCheckedChanged(BoardTestItemOption option)
        {
            if (IsRunning)
            {
                // 跑一半改勾项，本轮结果与界面上的勾就对不上了；先把这一轮跑完再说
                option.SetSelectedWithoutSave(!option.Selected);
                StatusText = "本轮还在跑，等它跑完再改测试项";
                return;
            }

            SaveConfig(quiet: true);
            OnPropertyChanged(nameof(SelectedItemsText));
            // 勾一项就是改一次本轮清单：右边那几行跟着重建，当场能看到这一路会跑什么
            RebuildItemRows();
        }

        /// <summary>「单板配置」（通道 / 工位仪器）与「测试项」两扇窗开框前的闸门：跑着的时候不给改任何东西</summary>
        public bool TryOpenConfig()
        {
            if (IsRunning)
            {
                StatusText = "本轮还在跑，等它跑完再改配置（本轮跑哪几项以开跑那一刻的勾选为准）";
                return false;
            }

            return true;
        }

        /// <summary>「测试项」窗口里的"全选/全不选"：一次改完只落一次盘，不然二十项就是二十次写文件</summary>
        public void SetAllItemsSelected(bool value)
        {
            if (IsRunning)
            {
                StatusText = "本轮还在跑，等它跑完再改测试项";
                return;
            }

            var changed = false;
            foreach (var option in SelectedItems)
                if (option.Selected != value)
                {
                    option.SetSelectedWithoutSave(value);
                    changed = true;
                }

            if (!changed) return;

            SaveConfig(quiet: true);
            OnPropertyChanged(nameof(SelectedItemsText));
            RebuildItemRows();
        }

        /// <summary>「测试项」窗口没按保存就关了：按进来的那份勾法退回（同样只落一次盘）</summary>
        public void RestoreItemSelection(IReadOnlyCollection<BoardTestItemKind> selected)
        {
            var changed = false;
            foreach (var option in SelectedItems)
            {
                var want = selected.Contains(option.Kind);
                if (option.Selected == want) continue;

                option.SetSelectedWithoutSave(want);
                changed = true;
            }

            if (!changed) return;

            SaveConfig(quiet: true);
            OnPropertyChanged(nameof(SelectedItemsText));
            RebuildItemRows();
        }

        /// <summary>当前勾着的测试项（保持 Enum 顺序，方便按模块分组）</summary>
        private List<BoardTestItemKind> SelectedKinds => SelectedItems.Where(o => o.Selected).Select(o => o.Kind).ToList();

        /// <summary>本轮要跑的那几路：勾了“本轮跑”且这条道接着的（与烧录页同一口径）。两个开关不能合一个：“这条道接没接”是工装状态要落盘，“本轮跑不跑”是当下这一下、不落盘。</summary>
        public List<int> TargetChannels => Channels.Where(c => c.Selected && c.Enabled).Select(c => c.Channel).ToList();

        /// <summary>全选/清空只动已接着的那几道：没接的路本来就不该被勾出来跑</summary>
        private void SetAllChannelsSelected(bool value)
        {
            foreach (var card in Channels.Where(c => c.Enabled)) card.Selected = value;
        }

        /// <summary>把勾选项与通道清单写回 board-test.json</summary>
        private bool SaveConfig(bool quiet)
        {
            try
            {
                _config.SelectedItems = SelectedKinds;
                _configService.Save(_config);
                if (!quiet) StatusText = "单板配置已保存（勾选项与通道参与），下一轮按这份跑";
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"保存单板配置失败：{ex.Message}", ex);
                StatusText = $"保存失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>「单板配置」窗口点保存时走这里</summary>
        public bool SaveConfigFromDialog() => SaveConfig(quiet: false);

        /// <summary>「单板配置」窗口保存成功后调用：按新仪器配置重算每路的设备落点并按新目录重建明细行（会清掉本轮已跑结果）；这里不重建卡片对象，因为「本轮跑」不落盘，重建会把它抹回全跑。</summary>
        public void ApplySavedConfig()
        {
            var config = SafeLoadInstruments();
            foreach (var card in Channels)
                card.DeviceText = DescribeChannelDevices(card.Channel, config);

            // 没跑过就不提“结果已清空”，免得跟 SaveConfig 那句“已保存”叠成一句没着落的话
            var hadResults = Channels.Any(c => c.Success != null);
            RebuildItemRows();
            if (hadResults) StatusText = "已按新配置重建明细行，本轮已跑结果一并清空";
        }

        // ==================== 页面状态 ====================

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (!SetProperty(ref _isRunning, value)) return;

                // 页头那句在“跑法说明”与“测试中”之间来回切，靠的就是 IsRunning 与本轮范围，两边都得推一下
                RaiseRoundScopeChanged();
                // 这一页三个按钮的可用性全吃 IsRunning，而收轮是后台回调落回 UI 线程、没有输入事件；不补这一句就得等用户点一下别处，清空结果与开始测试才肯亮
                RequeryCommands();
            }
        }

        public BoardChannelCardViewModel? SelectedCard
        {
            get => _selectedCard;
            set
            {
                if (!SetProperty(ref _selectedCard, value)) return;

                OnPropertyChanged(nameof(HasSelectedCard));
            }
        }

        public bool HasSelectedCard => SelectedCard != null;

        public string RoundId
        {
            get => _roundId;
            private set => SetProperty(ref _roundId, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (!SetProperty(ref _statusText, value)) return;

                OnPropertyChanged(nameof(HasStatusText));
            }
        }

        public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

        public int SuccessCount
        {
            get => _successCount;
            private set => SetProperty(ref _successCount, value);
        }

        public int FailedCount
        {
            get => _failedCount;
            private set => SetProperty(ref _failedCount, value);
        }

        public int IdleCount
        {
            get => _idleCount;
            private set => SetProperty(ref _idleCount, value);
        }

        public string RoundElapsedText => _roundWatch?.IsRunning == true
            ? $"已用 {_roundWatch.Elapsed.TotalSeconds:0.#}s"
            : _roundWatch != null ? $"用时 {_roundWatch.Elapsed.TotalSeconds:0.#}s" : string.Empty;

        /// <summary>本轮范围的一句话写法：一路就报“通道 6”，全部就说“全部 8 条”</summary>
        public string RoundScopeText => _roundChannels.Count switch
        {
            0 => string.Empty,
            1 => $"通道 {_roundChannels[0]}",
            _ when _roundChannels.Count == Channels.Count => $"全部 {Channels.Count} 条通道",
            _ => $"通道 {string.Join("、", _roundChannels)}",
        };

        /// <summary>页头那一句：没开过时讲跑法，开跑后讲这一轮跑的是哪几路；单跑一路时不能念“八条通道共用同一组”，那句会让人怀疑其余几路没配好设备。</summary>
        public string RoundStatusText => IsRunning
            ? _roundChannels.Count == 1
                ? $"单通道测试中：{RoundScopeText}，其余几路本轮不跑"
                : $"整批测试中：{RoundScopeText}"
            : _roundChannels.Count == 0
                ? "顺序固定：供电 → 条码 → 蓝牙；跑哪几项在菜单「测试项」里勾"
                : $"上一轮跑的是{RoundScopeText}";

        /// <summary>没被点名的那几路下方详情区该说什么：说清是“本轮没跑它”而不是“它配不上设备”</summary>
        public string NotInRoundHint => _roundChannels.Count == 0
            ? "这一路本轮不跑"
            : $"这一路本轮不跑（本轮只跑了{RoundScopeText}）；要看它跑哪几项，在上方通道格上右键「开始测试」单独跑一路";

        // ==================== 命令 ====================

        public ICommand RunAllCommand { get; }
        public ICommand StopAllCommand { get; }
        public ICommand ResetResultsCommand { get; }
        public ICommand CopyTraceCommand { get; }
        public ICommand CheckAllChannelsCommand { get; }
        public ICommand UncheckAllChannelsCommand { get; }

        // ==================== 开轮与收轮 ====================

        /// <summary>整批：把勾了“本轮跑”且这条道接着的那些通道一起跑</summary>
        private void RunAll() => RunCore(null);

        /// <summary>卡片右键"开始测试"：只跑这一路（哪怕它没勾参与整批）</summary>
        public void StartChannel(BoardChannelCardViewModel? card)
        {
            if (card == null) return;

            if (IsRunning)
            {
                StatusText = $"本轮还在跑：要停{card.Title}用右键「停止测试」，不要再开一轮（读码器、网关、综测仪都是共用的，同时开两班会互抢设备）";
                return;
            }

            // 跟着把人带到这一路：不切的话右边还停在原来选中的那一张，跑着了也看不见它在跑，只能看见一句“这一路没有可跑的项”
            SelectedCard = card;
            RunCore(new[] { card.Channel });
        }

        /// <summary>卡片右键"停止测试"：单板的三项测试是整批一轮跑完的（一台读码器一次触发收多条、一台网关一次扫描），停这一路就是把当前这一轮停下来；只有这一路单独在跑时它才等于只停它自己。</summary>
        public void StopChannel(BoardChannelCardViewModel? card)
        {
            if (card == null) return;

            if (!IsRunning)
            {
                StatusText = $"{card.Title} 现在没在跑，不用停";
                return;
            }

            if (card.IsBusy)
            {
                _log.Info($"===== 收到停止{card.Title}的请求：停掉当前这一轮 =====");
                StopRun();
                StatusText = $"已请求停止：{card.Title} 参与这一轮，所以整轮一起收尾（单板的设备是共用的，拆不出单独一路）";
            }
            else
            {
                StatusText = $"{card.Title} 没在这一轮里（本轮跑的是其余通道），不用停";
            }
        }

        /// <summary>开一轮：先把勾选项落盘再校验——勾选项与通道参与都写进 board-test.json，界面上改完不点也已生效，落一次盘是为了下次开机看到的就是刚才跑的这一组。</summary>
        /// <param name="only">null = 整批（参与的那些通道）；否则只跑点名的这几路。</param>
        private void RunCore(IReadOnlyList<int>? only)
        {
            _log.Info($"{DateTime.Now} Start calling the BoardChannelViewModel.RunCore() method . {Environment.NewLine}");
            try
            {
                // 右键"开始测试"与命令按钮走的是两条路，运行期闸门只能开在这里：叠两轮会把同一批读码器/网关抢成两半，且 _runCts 被后一轮覆盖后前一轮就再也停不下来
                if (IsRunning)
                {
                    StatusText = "本轮还在跑，不要再开一轮（读码器、网关、综测仪都是共用的）";
                    return;
                }

                var kinds = SelectedKinds;
                if (kinds.Count == 0)
                {
                    StatusText = "没勾任何测试项：在菜单「测试项」里勾上要跑的项（所有通道共用同一组）";
                    return;
                }

                var targets = only?.ToList() ?? TargetChannels;
                if (targets.Count == 0)
                {
                    // 条数从配置来，不写死“八条”：接几路报几路，改档后这句不会变成假话
                    StatusText = Channels.All(c => !c.Enabled)
                        ? $"本轮没有可跑的通道：{Channels.Count} 条通道都没接入整批（在菜单「配置→单板配置」的通道表里勾回来）"
                        : "本轮没有可跑的通道：卡片上的「本轮跑」全被取消了，勾上要跑的那几路再开始";
                    return;
                }

                if (TestRunState.IsRunning)
                {
                    StatusText = "整机测试方案正在跑，这一页不开新一轮（两边都在抢同一批仪器）";
                    return;
                }
                if (FlashRunState.IsRunning)
                {
                    StatusText = "烧录轮正在跑，这一页不开新一轮（板子在烧录器上，测试结果不作数）";
                    return;
                }

                if (!SaveConfig(quiet: true)) return;

                // 先清空上一轮，再按最新配置建本轮的行：BuildPlan 是往卡片上加行，反过来会被它刚建好的行清掉
                ClearResults();

                // 目录按最新仪器配置重建：读码器/网关/综测仪的启用台数决定槽位项清单，用旧快照会报出对不上名的项
                var plan = BuildPlan(kinds, targets);
                if (plan.Count == 0)
                {
                    StatusText = "勾的这几项在当前仪器配置里都没有可用设备（菜单「配置→单板配置」的工位仪器里把对应台数启用）";
                    return;
                }

                RoundId = $"{DateTime.Now:yyyyMMdd-HHmmss}-单板";
                // 先记下本轮范围再开 IsRunning：IsRunning 里那一下通知要读到新的范围，页头才不会报上一轮的
                _roundChannels = targets;
                IsRunning = true;
                TestRunState.SetRunning(true);
                _roundWatch = Stopwatch.StartNew();
                _runCts = new CancellationTokenSource();
                _runDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                StatusText = $"本轮：{RoundScopeText} × {kinds.Count} 项，按 供电 → 条码 → 蓝牙 的顺序跑";

                foreach (var channel in targets)
                {
                    if (_cardsByChannel.TryGetValue(channel, out var card)) card.BeginRun();
                }

                _ = RunAsync(plan, targets, _runCts.Token);
            }
            catch (Exception ex)
            {
                _log.Error($"开单板轮次失败：{ex.Message}", ex);
                StatusText = $"开跑失败：{ex.Message}";
                // 只有真开过轮才需要收尾：校验阶段就抛回来时 IsRunning 还是 false，这时去清 TestRunState 会把整机方案占着的闸门一并抹开
                if (IsRunning) FinishRun();
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the BoardChannelViewModel.RunCore() method . {Environment.NewLine}");
            }
        }

        /// <summary>一个模块在本轮要跑的那几项</summary>
        private sealed record ModulePlan(TestModuleModel Module, IReadOnlyList<BoardTestItemKind> Kinds);

        /// <summary>按固定顺序把勾选项分到模块上：执行端要这个顺序，界面建行要的也是它给出的项名；目录里没有这个模块（这类设备在仪器配置里全没启用）就跳过并记一句，不拖垮其余模块。</summary>
        private List<ModulePlan> ComposePlan(IReadOnlyList<BoardTestItemKind> kinds)
        {
            var catalog = _moduleService.GetAllModules().Where(m => m.Category == ModuleCategory.Board).ToList();
            var plan = new List<ModulePlan>();

            foreach (var moduleId in ModuleOrder)
            {
                var here = kinds.Where(k => k.ModuleId() == moduleId).ToList();
                if (here.Count == 0) continue;

                var module = catalog.FirstOrDefault(m => m.Id == moduleId);
                if (module == null)
                {
                    _log.Warn($"单板目录里没有模块 {moduleId}（仪器配置里这类设备可能全部没启用），它下面的项建不出行");
                    continue;
                }

                plan.Add(new ModulePlan(module, here));
            }

            return plan;
        }

        /// <summary>开一轮：把勾选项分到模块上并为本轮那些通道建好行对象。建行用的就是目录里的同一批项名（"供电电压-3"这种带槽位后缀的），执行端上报的名与界面上的行同源，不会"报了名界面找不到"地静默丢结果。</summary>
        private List<ModulePlan> BuildPlan(IReadOnlyList<BoardTestItemKind> kinds, List<int> targets)
        {
            var plan = ComposePlan(kinds);
            LayOutRows(plan, targets);

            // 这一轮一条通道一项都没建出来，八成是这类设备在仪器配置里没启用或台数不够
            foreach (var card in Channels.Where(c => targets.Contains(c.Channel) && c.Items.Count == 0))
                _log.Warn($"通道 {card.Channel} 本轮没有可跑的项：勾的项在这条槽位上没有对上的设备");

            return plan;
        }

        /// <summary>把计划摊成界面上的行：项名里带"-槽位号"的进对应通道卡，不带的算整批级项单独一行；slots 之外的通道卡撤掉行并标灰的"未参与"，挂着几行未测试反而像是跑坏了。</summary>
        private void LayOutRows(IReadOnlyList<ModulePlan> plan, IReadOnlyCollection<int> slots)
        {
            _rowsByName.Clear();
            BatchItems.Clear();

            // 没点名的路撤行，同时标上“不在本轮”：只撤行不标，界面会把“没点名”当成“没有可对上的设备”报
            foreach (var card in Channels)
            {
                card.InRound = slots.Contains(card.Channel);
                if (!card.InRound) card.Items.Clear();
            }

            foreach (var entry in plan)
            {
                foreach (var definition in entry.Module.Items)
                {
                    // 项名里没有"-槽位号"的就是整批级的那几项，它不进任何一张卡
                    if (!TrySplitSlot(definition.Name, out var prefix, out var slot))
                    {
                        if (entry.Kinds.Any(k => definition.Name.StartsWith(k.ItemPrefix(), StringComparison.Ordinal)))
                        {
                            // 照样建行：不建行这一项报上来的结果就没人接，界面只能看见 8 路红、不知道整批那项过没过
                            var batchRow = new TestItemViewModel(definition);
                            BatchItems.Add(batchRow);
                            // Card 位置空着：它的结论只上在整批那一栏，不算进任何一路的判定
                            _rowsByName[definition.Name] = (null, batchRow);
                        }

                        continue;
                    }

                    if (!slots.Contains(slot) || !entry.Kinds.Any(k => k.ItemPrefix() == prefix)) continue;
                    if (!_cardsByChannel.TryGetValue(slot, out var card)) continue;

                    var row = new TestItemViewModel(definition);
                    card.Items.Add(row);
                    _rowsByName[definition.Name] = (card, row);
                }
            }

            OnPropertyChanged(nameof(HasBatchItems));
        }

        /// <summary>不开轮也把清单摆出来：勾了哪几项、每条通道就跑哪几项，结论全是"未测试"；跑着的时候不动手，免得把已经上来的结果抹掉。</summary>
        private void RebuildItemRows()
        {
            if (IsRunning) return;

            ClearResults();
            LayOutRows(ComposePlan(SelectedKinds), Channels.Select(c => c.Channel).ToList());
        }

        /// <summary>把"供电电压-3"拆成（供电电压, 3）；拆不出槽位号返回 false</summary>
        private static bool TrySplitSlot(string itemName, out string prefix, out int slot)
        {
            prefix = string.Empty;
            slot = 0;

            var dash = itemName.LastIndexOf('-');
            if (dash <= 0 || dash + 1 >= itemName.Length) return false;

            if (!int.TryParse(itemName.AsSpan(dash + 1), out slot)) return false;

            prefix = itemName[..dash];
            return true;
        }

        private async Task RunAsync(List<ModulePlan> plan, List<int> targets, CancellationToken ct)
        {
            var seq = 0;
            try
            {
                _testItem.BeginPlanRun(targets);

                foreach (var entry in plan)
                {
                    ct.ThrowIfCancellationRequested();
                    seq++;

                    var module = entry.Module;
                    OnUi(() => StatusText = $"正在跑（{seq}/{plan.Count}）：{module.Name}");

                    try
                    {
                        // 与整机方案同一个入口：模块方法体里已经写好逐设备并发、异常隔离与逐项上报
                        await Task.Run(() => _testItem.TestItemxx(seq, module, OnItemCompleted, ct), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // 一个模块炸了不拦其余模块：综测仪连不上不该让条码也白跑，现场要的是"哪一项没出来"而不是"整轮没结果"
                        _log.Error($"单板模块 {module.Name} 执行异常：{ex.Message}", ex);
                        OnUi(() =>
                        {
                            foreach (var channel in targets)
                                if (_cardsByChannel.TryGetValue(channel, out var card))
                                    card.AddTrace($"{module.Name} 执行异常：{ex.Message}");
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _log.Warn("===== 单板这一轮已被停止 =====");
                OnUi(() =>
                {
                    foreach (var card in Channels.Where(c => c.IsBusy)) card.AddTrace("已停止：未上报的项按未测判 FAIL");
                });
            }
            catch (Exception ex)
            {
                _log.Error($"单板轮次异常结束：{ex.Message}", ex);
                OnUi(() => StatusText = $"本轮异常结束：{ex.Message}");
            }

            OnUi(() =>
            {
                foreach (var channel in targets)
                {
                    if (!_cardsByChannel.TryGetValue(channel, out var card)) continue;

                    if (_cardWatches.TryGetValue(channel, out var watch))
                    {
                        watch.Stop();
                        card.SetElapsed(watch.Elapsed);
                    }

                    card.FinishRun();
                }

                // 整批级的那几项同样落定：没上报就按未上报判不过，不能让它一直挂着"未测试"让人以为还在跑
                foreach (var item in BatchItems.Where(i => i.Result == TestResultStatus.NotTested))
                    item.MarkNotReportedAsFail();

                FinishRun();
            });
        }

        /// <summary>执行端的逐项上报（在采集线程上调用）：按项名找到那张卡的哪一行，刷值并把这行结论写进该路的明细；回调查不到名 = 报了目录里没有的项（或这一路本轮没建行），只记日志不静默丢掉。</summary>
        private Task OnItemCompleted(string itemName, TestItemResult result)
        {
            if (!_rowsByName.TryGetValue(itemName, out var row))
            {
                _log.Warn($"单板：收到不在本轮清单里的项「{itemName}」（结果未上到界面）");
                return Task.CompletedTask;
            }

            OnUi(() =>
            {
                row.Item.ApplyResult(result);
                // 整批级的那几项没有卡可落（Card 为空），值与结论就在上面那条整批栏里自己显示
                row.Card?.AddTrace($"{itemName} = {row.Item.MeasuredValueText} {row.Item.SpecText}｜{(row.Item.Result == TestResultStatus.Pass ? "PASS" : "FAIL")}");
                if (row.Card != null && !_cardWatches.ContainsKey(row.Card.Channel)) _cardWatches[row.Card.Channel] = Stopwatch.StartNew();
            });
            return Task.CompletedTask;
        }

        private void StopRun()
        {
            _log.Info("===== 收到停止单板轮次请求 =====");
            _runCts?.Cancel();
            StatusText = "已请求停止：正在跑的那一项会尽快退出，后面的模块不再执行";
        }

        private void FinishRun()
        {
            _roundWatch?.Stop();
            _runCts?.Dispose();
            _runCts = null;

            foreach (var card in Channels) card.IsBusy = false;
            IsRunning = false;
            TestRunState.SetRunning(false);
            _log.Info($"===== 单板本轮结束：用时 {(_roundWatch?.Elapsed.TotalSeconds ?? 0):0.#}s，通过 {Channels.Count(c => c.Success == true)}/{Channels.Count} =====");
            RecomputeSummary();
            OnPropertyChanged(nameof(RoundElapsedText));

            _runDone?.TrySetResult();
            _runDone = null;
            RaiseRoundScopeChanged();
        }

        /// <summary>本轮范围一变就同时刷这三句（页头与“这一路本轮不跑”那句都吃它）</summary>
        private void RaiseRoundScopeChanged()
        {
            OnPropertyChanged(nameof(RoundScopeText));
            OnPropertyChanged(nameof(RoundStatusText));
            OnPropertyChanged(nameof(NotInRoundHint));
        }

        /// <summary>「清空结果」：这一页回到没跑过的样子，但行要按当前勾选重新摆回去（撤了下方就又是一片空白）</summary>
        private void ResetResults() => RebuildItemRows();

        /// <summary>把行、结论、耗时、明细、整批栏全撤掉（新一轮开始前也走这里，所以不重建行）</summary>
        private void ClearResults()
        {
            _roundChannels = new List<int>();
            foreach (var card in Channels)
            {
                card.InRound = true;
                card.Items.Clear();
                card.MarkIdle();
            }
            _cardWatches.Clear();
            _rowsByName.Clear();
            BatchItems.Clear();
            OnPropertyChanged(nameof(HasBatchItems));
            _roundWatch = null;
            RecomputeSummary();
            OnPropertyChanged(nameof(RoundElapsedText));
            // 不刷这句的话，开轮在校验那几步提前退回来时页头还挂着上一轮的范围
            RaiseRoundScopeChanged();
        }

        /// <summary>三个数重算一遍（通道条上那一行绑的就是它们）</summary>
        private void RecomputeSummary()
        {
            SuccessCount = Channels.Count(c => c.Success == true);
            FailedCount = Channels.Count(c => c.Success == false);
            IdleCount = Channels.Count - SuccessCount - FailedCount;
        }

        /// <summary>把动作排回 UI 线程：执行端在采集线程上报，界面必须自己回来</summary>
        private static void OnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        /// <summary>关窗口前调用：先请求停止，再泵消息等这一轮收尾（断掉设备连接）</summary>
        public void StopAndAwaitWrapUp(int timeoutMs = 10_000)
        {
            if (!IsRunning || _awaitingWrapUp) return;
            _awaitingWrapUp = true;

            try
            {
                _log.Info("===== 关闭窗口：先请求停止单板轮次，等本轮收尾 =====");
                _runCts?.Cancel();

                var pending = _runDone;
                if (pending == null || pending.Task.IsCompleted) return;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                var frame = new DispatcherFrame();
                pending.Task.ContinueWith(_ => dispatcher.Invoke(() => frame.Continue = false), TaskScheduler.Default);
                _ = Task.Delay(timeoutMs).ContinueWith(_ => dispatcher.Invoke(() => frame.Continue = false), TaskScheduler.Default);
                System.Windows.Threading.Dispatcher.PushFrame(frame);

                if (IsRunning)
                    _log.Warn($"单板轮次收尾等待 {timeoutMs / 1000}s 仍未完成，不阻拦退出；综测仪的输出可能还挂着");
            }
            finally
            {
                _awaitingWrapUp = false;
            }
        }

        // ==================== 通道落点说明与明细复制 ====================

        /// <summary>读不到仪器配置时给一份空档：每行都会走“没有可对上的设备”那句，比抛出去把整页带倒好</summary>
        private InstrumentConnectionConfig SafeLoadInstruments()
        {
            try
            {
                return _instrumentConfig.Load();
            }
            catch (Exception ex)
            {
                _log.Warn($"读仪器配置失败，通道落点说明只能留空：{ex.Message}");
                return new InstrumentConnectionConfig();
            }
        }

        /// <summary>算某一行的设备落点；整份配置由调用方传进来，逐路算时共用同一份。这份说明只念不改是有意的：设定电压与 RSSI 阈值是按台给的（一台综测仪四个通道共用一个设定值），从一条通道进去改会把同台的其余通道一起改掉，那种改法该在单板配置的「工位仪器」页里做。</summary>
        private static string DescribeChannelDevices(int channel, InstrumentConnectionConfig config)
        {
            var lines = new List<string>();
            try
            {
                var power = SpecItems.BoardPowerSlots(config).FirstOrDefault(s => s.Slot == channel);
                lines.Add(power.Slot == channel
                    ? $"供电：{config.Cry5242Testers[power.TesterIndex].Name} CH{power.Channel}（这台统一设定 {config.Cry5242Testers[power.TesterIndex].OutputMilliVolts}mV，±{config.Cry5242Testers[power.TesterIndex].VoltageToleranceMilliVolts}mV）"
                    : "供电：这条槽位没有可对上的综测仪通道（单板配置→工位仪器：综测仪没启用或台数不够）");

                var barcode = SpecItems.BoardBarcodeSlots(config).FirstOrDefault(s => s.Slot == channel);
                lines.Add(barcode.Slot == channel
                    ? $"条码：{config.BarcodeReaders[barcode.ReaderIndex].Name}（这台第 {barcode.IndexInReader} 条）"
                    : "条码：这条槽位没有可对上的读码器（单板配置→工位仪器）");

                var ble = SpecItems.BoardBluetoothSlots(config).FirstOrDefault(s => s.Slot == channel);
                lines.Add(ble.Slot == channel
                    ? $"蓝牙：{config.BluetoothGateways[ble.GatewayIndex].Name}（阈值不低于 {config.BluetoothGateways[ble.GatewayIndex].RssiMinDbm}dBm）"
                    : "蓝牙：这条槽位没有可对上的网关（单板配置→工位仪器）");
            }
            catch (Exception ex)
            {
                lines.Add($"设备落点算不出来：{ex.Message}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private void CopyTraceToClipboard()
        {
            var card = SelectedCard;
            if (card == null) return;

            try
            {
                var text = string.Join(Environment.NewLine, card.Trace);
                if (string.IsNullOrEmpty(text)) text = $"{card.Title} {card.Name}｜{card.ResultText}";
                Clipboard.SetText(text);
                StatusText = $"已复制 {card.Title} 的明细（{card.Trace.Count} 行）";
            }
            catch (Exception ex)
            {
                _log.Warn($"复制明细失败：{ex.Message}");
                StatusText = $"复制失败：{ex.Message}";
            }
        }

        /// <summary>菜单「测试项」弹窗里的一行：显示名 + 属于哪个模块 + 是否勾选</summary>
        public sealed class BoardTestItemOption : ViewModelBase
        {
            private readonly Action<BoardTestItemOption> _changed;
            private bool _selected;

            public BoardTestItemOption(BoardTestItemKind kind, bool selected, Action<BoardTestItemOption> changed, string moduleText)
            {
                Kind = kind;
                _selected = selected;
                _changed = changed;
                ModuleText = moduleText;
            }

            /// <summary>这一项是哪一项（配置 json 里存的就是这个值）</summary>
            public BoardTestItemKind Kind { get; }

            /// <summary>菜单里显示的中文项名</summary>
            public string Name => Kind.ToDisplay();

            /// <summary>这一项由哪个模块跑（项一多就要靠这句认出哪几项是同一次采集带出来的）</summary>
            public string ModuleText { get; }

            public bool Selected
            {
                get => _selected;
                set
                {
                    if (!SetProperty(ref _selected, value)) return;
                    _changed.Invoke(this);
                }
            }

            /// <summary>回退勾选用：不再触发一次保存，否则被拦下的那次改动又写回盘上。属性名必须手写——SetProperty 的 [CallerMemberName] 在这一行取到的是方法名 SetSelectedWithoutSave，通知发给个不存在的属性名，界面上的勾不会跟着变（数据对了、画面没对）。</summary>
            public void SetSelectedWithoutSave(bool value) => SetProperty(ref _selected, value, nameof(Selected));
        }
    }
}
