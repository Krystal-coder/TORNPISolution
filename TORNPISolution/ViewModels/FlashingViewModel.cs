using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TORNPISolution.Common;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Service.Devices;
using TORNPISolution.Service.Flashing;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 烧录页 VM：八通道并发烧 Flash / 富芮坤 / All，参数在菜单配置页，本页只呈现状态。
    /// 开烧前必须先落盘配置——编排每轮自己读盘，没保存就开烧会把上一份配置的结果记到本轮卡片上。
    /// 结果不逐条 Invoke：每通道只留最新一份。
    /// </summary>
    public class FlashingViewModel : ViewModelBase
    {
        /// <summary>UI 刷新节拍（毫秒）</summary>
        private const int FlushIntervalMs = 80;

        /// <summary>擦除范围（工具 -size 的四个取值，十六进制页数）</summary>
        private static readonly string[] EraseSizeChoices = { "200000", "100000", "80000", "40000" };

        private readonly IFlashingConfigService _configService;
        private readonly IFlashingService _flashing;
        private readonly IFlashRecordService _records;
        private readonly ILogService _log = new LogService(nameof(FlashingViewModel));

        private readonly DispatcherTimer _flushTimer;
        private readonly Dictionary<int, FlashChannelCardViewModel> _cardsByChannel = new();

        /// <summary>待刷到卡片的最新快照：八条路并发写，键是通道号，后到的覆盖先到的</summary>
        private readonly ConcurrentDictionary<int, ChannelFlashReport> _pending = new();

        private FlashingConfig _config = new();
        private CancellationTokenSource? _roundCts;
        private Stopwatch? _roundWatch;
        // 一轮真正跑完（含写记录）后置位，关窗口时靠它等收尾
        private TaskCompletionSource? _roundDone;
        // 已在等收尾：消息泵期间再进来一次（比如又点一次关闭）不能叠消息泵
        private bool _awaitingWrapUp;
        // 本轮这一阶段实际在烧哪一颗（All 会先 Flash 后富芮坤）；没在跑时为 null
        private FlashAction? _phase;

        private FlashJob _job = FlashJob.All;
        private bool _isRunning;
        private bool _isDetecting;
        private FlashChannelCardViewModel? _selectedCard;
        private string _roundId = string.Empty;
        private string _statusText = string.Empty;
        private string _configIssueText = string.Empty;
        private string _detectText = string.Empty;
        private int _successCount;
        private int _failedCount;
        private int _skippedCount;
        private int _waitingCount;
        private int _runningCount;
        private int _idleCount;

        public FlashingViewModel(IFlashingConfigService configService, IFlashingService flashing, IFlashRecordService records)
        {
            _configService = configService;
            _flashing = flashing;
            _records = records;

            _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(FlushIntervalMs),
            };
            _flushTimer.Tick += (_, _) => FlushPending();

            LoadConfig();

            StartRoundCommand = new RelayCommand(StartRound, () => !IsRunning && !IsDetecting && !TestRunState.IsRunning);
            StopRoundCommand = new RelayCommand(StopRound, () => IsRunning);
            ResetResultsCommand = new RelayCommand(ResetResults, () => !IsRunning);
            SaveConfigCommand = new RelayCommand(SaveConfig, () => !IsRunning);
            RefreshPortsCommand = new RelayCommand(RefreshPorts);
            DetectProgrammersCommand = new RelayCommand(async () => await DetectProgrammersAsync(), () => !IsRunning && !IsDetecting);
            OpenRecordFolderCommand = new RelayCommand(OpenRecordFolder);
            CheckAllChannelsCommand = new RelayCommand(() => SetAllSelected(true), () => !IsRunning);
            UncheckAllChannelsCommand = new RelayCommand(() => SetAllSelected(false), () => !IsRunning);
            CopyTraceCommand = new RelayCommand(CopyTraceToClipboard, () => SelectedCard != null);
            SelectFlashCommand = new RelayCommand(() => SelectedJob = FlashJob.WizProFlash, () => !IsRunning);
            SelectFreqchipCommand = new RelayCommand(() => SelectedJob = FlashJob.FreqchipMcu, () => !IsRunning);
            SelectAllCommand = new RelayCommand(() => SelectedJob = FlashJob.All, () => !IsRunning);
        }

        // ==================== 配置与通道清单 ====================

        /// <summary>正在编辑的配置（XAML 直接绑 Config.WizPro.* / Config.Freqchip.*）</summary>
        public FlashingConfig Config => _config;

        /// <summary>通道卡片清单：行数就是配置里的通道行数，界面按它排</summary>
        public ObservableCollection<FlashChannelCardViewModel> Channels { get; } = new();

        /// <summary>本机可用串口（配置对话框的下拉数据源）</summary>
        public List<string> AvailablePorts { get; private set; } = new();

        public string AvailablePortsText => AvailablePorts.Count == 0
            ? "本机没识别到任何串口（设备管理器里一个 COM 都没有）"
            : string.Join("、", AvailablePorts);

        public string[] EraseSizeOptions => EraseSizeChoices;

        public int[] DataBitsOptions { get; } = { 7, 8 };

        public int[] StopBitsOptions { get; } = { 1, 2 };

        /// <summary>芯片系列下拉项：显示中文、存枚举值（工具命令行了 -chip 的数字由驱动自己换算）</summary>
        public ChipOption[] ChipOptions { get; } =
        {
            new(FreqchipChip.Series802x, "802x 系列"),
            new(FreqchipChip.Series800x, "800x 系列"),
            new(FreqchipChip.Series30xx, "30xx 系列（波特率上限 460800）"),
            new(FreqchipChip.Series303x, "303x 系列"),
            new(FreqchipChip.Series508x, "508x 系列"),
        };

        /// <summary>记录目录（界面底部显示，并给"打开记录文件夹"用）</summary>
        public string RecordDirectory => _records.StorageDirectory;

        /// <summary>从盘上重读配置并按最新通道清单重建卡片（换治具、或手工改过文件后用）</summary>
        public void LoadConfig()
        {
            _config = _configService.Load();
            RefreshPorts();

            Channels.Clear();
            _cardsByChannel.Clear();
            _selectedCard = null;
            for (var i = 0; i < _config.Channels.Count; i++)
            {
                var card = new FlashChannelCardViewModel(i + 1, _config.Channels[i]) { UseWizProPort = CurrentPhase == FlashAction.WizProFlash };
                Channels.Add(card);
                _cardsByChannel[card.Channel] = card;
                card.PropertyChanged += OnCardConfigChanged;
            }

            SelectedCard = Channels.FirstOrDefault();
            ApplyJobToCards();
            RecomputeSummary();
            OnPropertyChanged(nameof(Config));     // 绑的是 Config.WizPro.* 这条路径：换了一份配置对象就得重走一次
            ValidateConfig();
        }

        /// <summary>刷新串口列表：已保存但当前不在本机列表里的口也留着——USB 临时掉线时若把它从下拉里抹掉，绑定会让配置看着像被清空了，操作人员不知道该填什么。</summary>
        private void RefreshPorts()
        {
            var ports = SerialPort.GetPortNames().Distinct().OrderBy(p => p).ToList();
            foreach (var saved in Channels.SelectMany(c => new[] { c.WizProPortName, c.FreqchipPortName }))
            {
                if (!string.IsNullOrWhiteSpace(saved) && !ports.Contains(saved, StringComparer.OrdinalIgnoreCase))
                    ports.Add(saved);
            }

            AvailablePorts = ports;
            OnPropertyChanged(nameof(AvailablePorts));
            OnPropertyChanged(nameof(AvailablePortsText));
            ValidateConfig();
        }

        // ==================== 本轮动作 ====================

        /// <summary>本轮做什么（烧 Flash / 烧富芮坤 / All）</summary>
        public FlashJob SelectedJob
        {
            get => _job;
            private set
            {
                if (!SetProperty(ref _job, value)) return;

                OnPropertyChanged(nameof(IsFlashJob));
                OnPropertyChanged(nameof(IsFreqchipJob));
                OnPropertyChanged(nameof(IsAllJob));
                OnPropertyChanged(nameof(ActionHintText));
                // 卡片显示哪个串口跟着第一阶段的动作走；真跑起来后由 SetPhase 换成当前阶段那个
                ApplyJobToCards();
                // 换动作等于换了一轮：上一轮的结果不属于这一颗芯片，留着会让人拿 Flash 的结论判富芮坤
                ResetResults();
                ValidateConfig();
            }
        }

        /// <summary>本轮只烧 SPI Flash</summary>
        public bool IsFlashJob
        {
            get => _job == FlashJob.WizProFlash;
            set { if (value) SelectedJob = FlashJob.WizProFlash; }
        }

        /// <summary>本轮只烧富芮坤主控</summary>
        public bool IsFreqchipJob
        {
            get => _job == FlashJob.FreqchipMcu;
            set { if (value) SelectedJob = FlashJob.FreqchipMcu; }
        }

        /// <summary>本轮 All：两段连着走，默认选这一档</summary>
        public bool IsAllJob
        {
            get => _job == FlashJob.All;
            set { if (value) SelectedJob = FlashJob.All; }
        }

        /// <summary>正在跑的阶段动作（没在跑时为 null）：卡片要显示哪个串口、汇总行写哪一颗都吃它</summary>
        private FlashAction CurrentPhase => _phase ?? SelectedJob.ToPhases()[0];

        /// <summary>All 分到第几段了（单独动作永远写“1/1”）</summary>
        public string PhaseText => _phase == null
            ? SelectedJob == FlashJob.All ? "阶段 1/2：待开始" : "阶段 1/1：待开始"
            : $"阶段 {Array.IndexOf(SelectedJob.ToPhases(), _phase.Value) + 1}/{SelectedJob.ToPhases().Length}：{_phase.Value.ToDisplayText()}";

        /// <summary>动作说明</summary>
        public string ActionHintText => SelectedJob switch
        {
            FlashJob.WizProFlash => "只烧 SPI Flash：先让厂商上位机把工程下载进烧录器（一次覆盖所有），再各通道并发烧写",
            FlashJob.FreqchipMcu => "只烧富芮坤主控：芯片只在一上电那一刻找工具，卡片提示\"按复位键\"时按下这一路的复位键",
            _ => "All：先整批烧 SPI Flash，落定后再整批烧富芮坤，中间不用人按复位；Flash 没烧好的那一路不再进第二阶段",
        };

        /// <summary>把当前阶段要用的串口切到卡片要显示的那个口</summary>
        private void ApplyJobToCards() => ApplyPhaseToCards(CurrentPhase);

        private void ApplyPhaseToCards(FlashAction phase)
        {
            var useWiz = phase == FlashAction.WizProFlash;
            foreach (var card in Channels) card.UseWizProPort = useWiz;
        }

        // ==================== 轮次状态与汇总 ====================

        /// <summary>是否有烧录轮次在跑（跑起来锁住整页配置）</summary>
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (!SetProperty(ref _isRunning, value)) return;

                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(RoundVisual));
                OnPropertyChanged(nameof(VerdictText));
                // 烧录轮收轮也是后台回调落回 UI 线程，没有输入事件：不补这一句开始/清空/保存配置会一直灰着
                RequeryCommands();
            }
        }

        /// <summary>没在跑（配置区与通道清单的可编辑性吃它）</summary>
        public bool IsIdle => !IsRunning;

        /// <summary>是否正在探测烧录器</summary>
        public bool IsDetecting
        {
            get => _isDetecting;
            private set
            {
                if (!SetProperty(ref _isDetecting, value)) return;

                OnPropertyChanged(nameof(IsDetectingText));
                // 探测完成同样是从后台任务回来，不重问一遍 CanExecute，“探测烧录器”与开始烧录都会停在灰态
                RequeryCommands();
            }
        }

        /// <summary>探测进行中的提示文字</summary>
        public string IsDetectingText => IsDetecting ? "探测中：逐个串口发 RDMC 读设备自报的通道号（每个口要等它回应或超时）" : string.Empty;

        /// <summary>本轮轮次号（与记录文件里的轮次列一致，事后按它聚合整批）</summary>
        public string RoundId
        {
            get => _roundId;
            private set => SetProperty(ref _roundId, value);
        }

        public FlashChannelCardViewModel? SelectedCard
        {
            get => _selectedCard;
            set
            {
                if (!SetProperty(ref _selectedCard, value)) return;

                OnPropertyChanged(nameof(HasSelectedCard));
            }
        }

        public bool HasSelectedCard => SelectedCard != null;

        /// <summary>本轮已通过的通路数</summary>
        public int SuccessCount
        {
            get => _successCount;
            private set => SetProperty(ref _successCount, value);
        }

        /// <summary>本轮未通过的通路数（含失败与取消）</summary>
        public int FailedCount
        {
            get => _failedCount;
            private set => SetProperty(ref _failedCount, value);
        }

        /// <summary>本轮被跳过的通路数（点名了但清单里没有、或这一路没启用）</summary>
        public int SkippedCount
        {
            get => _skippedCount;
            private set => SetProperty(ref _skippedCount, value);
        }

        public int WaitingCount
        {
            get => _waitingCount;
            private set => SetProperty(ref _waitingCount, value);
        }

        /// <summary>正在跑的通路数（不含等人动作的）</summary>
        public int RunningCount
        {
            get => _runningCount;
            private set => SetProperty(ref _runningCount, value);
        }

        /// <summary>本轮没开始的通路数</summary>
        public int IdleCount
        {
            get => _idleCount;
            private set => SetProperty(ref _idleCount, value);
        }

        /// <summary>汇总徽章的颜色语义：有路在等动作时优先用"等人"那档，催操作人员去按复位</summary>
        public ChannelVisual RoundVisual => IsRunning
            ? WaitingCount > 0 ? ChannelVisual.Waiting : ChannelVisual.Running
            : SettledCount == 0 ? ChannelVisual.Idle
            : FailedCount + SkippedCount > 0 ? ChannelVisual.Failed
            : ChannelVisual.Success;

        /// <summary>已落定的通路数</summary>
        private int SettledCount => SuccessCount + FailedCount + SkippedCount;

        /// <summary>汇总徽章文字</summary>
        public string VerdictText => IsRunning
            ? WaitingCount > 0 ? $"等 {WaitingCount} 路按复位" : "进行中"
            : SettledCount == 0 ? "未开始"
            : FailedCount > 0 ? $"{FailedCount} 路未通过"
            : SkippedCount > 0 ? $"有 {SkippedCount} 路没烧"
            : "全部通过";

        /// <summary>本轮一行汇总：动作、通过比、耗时</summary>
        public string RoundSummaryText
        {
            get => _roundSummaryText;
            private set => SetProperty(ref _roundSummaryText, value);
        }

        private string _roundSummaryText = string.Empty;

        /// <summary>本轮整批耗时文字</summary>
        public string RoundElapsedText => _roundWatch?.IsRunning == true
            ? $"已用 {_roundWatch.Elapsed.TotalSeconds:0.#}s"
            : _roundWatch != null ? $"用时 {_roundWatch.Elapsed.TotalSeconds:0.#}s" : string.Empty;

        /// <summary>操作提示一行（保存结果、校验拦下的原因都写这里）</summary>
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

        /// <summary>配置校验出来的问题清单（空表示没问题）</summary>
        public string ConfigIssueText
        {
            get => _configIssueText;
            private set
            {
                if (!SetProperty(ref _configIssueText, value)) return;

                OnPropertyChanged(nameof(HasConfigIssueText));
            }
        }

        public bool HasConfigIssueText => !string.IsNullOrWhiteSpace(ConfigIssueText);

        /// <summary>探测烧录器的结果：只念出"哪个口上挂着几号烧录器"，串口仍由人填</summary>
        public string DetectText
        {
            get => _detectText;
            private set => SetProperty(ref _detectText, value);
        }

        // ==================== 命令 ====================

        public ICommand StartRoundCommand { get; }
        public ICommand StopRoundCommand { get; }
        public ICommand ResetResultsCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand RefreshPortsCommand { get; }
        public ICommand DetectProgrammersCommand { get; }
        public ICommand OpenRecordFolderCommand { get; }
        public ICommand CheckAllChannelsCommand { get; }
        public ICommand UncheckAllChannelsCommand { get; }
        public ICommand CopyTraceCommand { get; }
        public ICommand SelectFlashCommand { get; }
        public ICommand SelectFreqchipCommand { get; }
        public ICommand SelectAllCommand { get; }

        /// <summary>本轮要烧的通道号：勾了且启用的那些</summary>
        public List<int> TargetChannels => Channels
            .Where(c => c.Selected && c.Enabled)
            .Select(c => c.Channel)
            .ToList();

        private void SetAllSelected(bool value)
        {
            foreach (var card in Channels.Where(c => c.Enabled)) card.Selected = value;
        }

        // ==================== 开轮与收轮 ====================

        /// <summary>开一轮：先把界面配置落盘，再做一次校验，有拦着的问题直接不烧并写在提示里——现场宁可被拦住，也不要在八张卡片上看到同一句超时。</summary>
        private void StartRound() => StartRoundCore(null);

        /// <summary>卡片右键“开始测试”：只烧这一路，哪怕它没勾参与整批；与整批跑走同一条开轮代码，差别只在点名了哪几路，校验只做一套。</summary>
        public void StartChannel(FlashChannelCardViewModel? card)
        {
            if (card == null) return;

            if (IsRunning)
            {
                StatusText = $"本轮还在跑：要停{card.Title}用右键「停止测试」，不要再开一轮（两套设备共用这些串口，同时开会互抢）";
                return;
            }

            StartRoundCore(new[] { card.Channel });
        }

        /// <summary>卡片右键“停止测试”：只停这一路，其余路照常跑完</summary>
        public void StopChannel(FlashChannelCardViewModel? card)
        {
            if (card == null) return;

            if (!IsRunning)
            {
                StatusText = $"{card.Title} 现在没在跑，不用停";
                return;
            }

            _log.Info($"===== 收到停止{card.Title}的请求（其余路不受影响） =====");
            _flashing.CancelChannel(card.Channel);
            StatusText = $"已请求停止{card.Title}：这一路自己收尾，其余几路照常跑完";
        }

        /// <summary>开轮真正入口：only 为 null 就是整批（勾了且启用的那些路），点名就是卡片右键单独跑那几路</summary>
        private void StartRoundCore(IReadOnlyList<int>? only)
        {
            _log.Info($"{DateTime.Now} Start calling the FlashingViewModel.StartRoundCore() method . {Environment.NewLine}");
            try
            {
                // 右键"开始测试"不经过命令的 CanExecute，运行期闸门必须在这里再补一道：不拦的话测试方案（整机或单板）跑着时右键能开出一轮并发烧录，两边抢同一批串口与仪器
                if (IsRunning)
                {
                    StatusText = "本轮还在跑，不要再开一轮（两套设备共用这些串口，同时开两班会互抢）";
                    return;
                }

                if (TestRunState.IsRunning)
                {
                    StatusText = "测试正在跑（整机方案或单板轮次），先停下再开烧：两边抢同一批仪器与串口";
                    _log.Warn("测试方案或单板轮次正在跑，忽略启动烧录轮的请求");
                    return;
                }

                var targets = only?.ToList() ?? TargetChannels;
                if (targets.Count == 0)
                {
                    StatusText = "本轮没勾要烧的通道：勾掉的路不会报错，只会一直停在\"等待开始\"";
                    return;
                }

                // 不保存就不开烧：编排那边每轮自己读盘，没落盘的改动不会参与这一轮
                if (!SaveConfigCore(quiet: true)) return;

                var issues = BuildIssues(targets);
                if (issues.Count > 0)
                {
                    StatusText = "配置还有问题，没开烧：" + issues[0];
                    _log.Warn($"烧录轮次被配置校验拦下：{string.Join("；", issues)}");
                    return;
                }

                ResetResults();
                _phase = null;
                RoundId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{SelectedJob.ToDisplayText()}";
                IsRunning = true;
                FlashRunState.SetRunning(true);
                _roundWatch = Stopwatch.StartNew();
                _roundCts = new CancellationTokenSource();
                _roundDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                StatusText = SelectedJob == FlashJob.All
                    ? $"本轮 All：{targets.Count} 路并发，先整批烧 Flash、后整批烧富芮坤；单路失败不牵连其余"
                    : $"本轮{SelectedJob.ToDisplayText()}：{targets.Count} 路并发，单路失败不牵连其余";
                foreach (var card in Channels) card.IsBusy = true;
                _flushTimer.Start();
                _ = RunJobAsync(targets, _roundCts.Token);
            }
            catch (Exception ex)
            {
                _log.Error($"开烧录轮失败：{ex.Message}", ex);
                StatusText = $"开烧失败：{ex.Message}";
                // 可能只是配置校验那一步抛回来，轮根本还没开：这时收尾会把 FlashRunState 抹成 false，连带把测试那边占着的闸门一起打开
                if (IsRunning) FinishRound();
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the FlashingViewModel.StartRoundCore() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// 把本轮选的那一档展开成阶段跑：单独动作就一步，All 则是“整批 Flash → 整批富芮坤”两段。
        /// 下一段只接上一段烧成功的那几路：主控的 boot 程序在 Flash 里，上一颗没烧好时烧它只会拿到一句握手失败，反而把真因盖住。
        /// </summary>
        private async Task RunJobAsync(List<int> targets, CancellationToken ct)
        {
            var phases = SelectedJob.ToPhases();
            var running = targets;
            try
            {
                for (var i = 0; i < phases.Length; i++)
                {
                    var phase = phases[i];
                    if (running.Count == 0)
                    {
                        // i==0 时没有上一段可提：直接报本轮，否则 phases[-1] 越界
                        var last = i == 0 ? "本轮" : phases[i - 1].ToDisplayText();
                        OnUi(() => StatusText = $"{last} 没有一路通过，{phase.ToDisplayText()} 不启动");
                        break;
                    }

                    var set = phase;
                    OnUi(() =>
                    {
                        _phase = set;
                        ApplyPhaseToCards(set);
                        OnPropertyChanged(nameof(PhaseText));
                        OnPropertyChanged(nameof(RoundSummaryText));
                    });

                    var reports = await RunPhaseAsync(phase, running, ct).ConfigureAwait(false);
                    // 终值也走同一条队列：卡片结论与记录文件吃的是同一份快照
                    foreach (var report in reports) _pending[report.Channel] = report;
                    if (reports.Count > 0) _records.AppendRound(RoundId, reports);   // 写盘在后台线程，不挡界面刷新

                    // 阶段终值先刷到卡片上：下一阶段的“下载工程数据”会盖掉同一通道那份快照，不抢在这一刻刷就看不见上一颗的结论
                    OnUi(FlushPending);

                    running = reports.Where(r => r.Success == true).Select(r => r.Channel).ToList();
                }
            }
            catch (OperationCanceledException)
            {
                // 下载工程数据那一步被打断时整轮直接抛回来，各卡片不会再有快照，这里补成已取消
                // 只标还在跑的那几路：上一阶段已经落定的结论不能被这一句抹掉，本轮没参与的通道也不该跟着变红
                var stuck = running.ToList();
                OnUi(() =>
                {
                    foreach (var channel in stuck)
                        if (_cardsByChannel.TryGetValue(channel, out var card)) card.MarkCanceled();
                });
            }
            catch (Exception ex)
            {
                _log.Error($"烧录轮异常结束：{ex.Message}", ex);
                var stuck = running.ToList();
                OnUi(() =>
                {
                    foreach (var channel in stuck)
                        if (_cardsByChannel.TryGetValue(channel, out var card)) card.MarkCanceled();
                    StatusText = $"本轮异常结束：{ex.Message}";
                });
            }

            // 上位机下载工程时可能重占串口，本轮结束先在后台刷新一次列表，再回 UI 线程收尾
            OnUi(() =>
            {
                _phase = null;
                RefreshPorts();
                FinishRound();
            });
        }

        /// <summary>跑一个阶段：单独抽出是为了取消时能分清楚“整轮报错”与“这一路被停”</summary>
        private Task<IReadOnlyList<ChannelFlashReport>> RunPhaseAsync(FlashAction phase, List<int> targets, CancellationToken ct)
            => _flashing.RunRoundAsync(phase, targets, OnChannelReport, ct);

        /// <summary>编排回调来自各通道自己的线程：只留最新一份快照，交给节拍刷新</summary>
        private void OnChannelReport(ChannelFlashReport report) => _pending[report.Channel] = report;

        /// <summary>把动作排回 UI 线程：已经在 UI 线程就直接执行，不刻意多一次跳转</summary>
        private static void OnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.Invoke(action);
        }

        /// <summary>把待刷的快照摊到卡片上（只跑在 UI 线程）</summary>
        private void FlushPending()
        {
            if (!_flushTimer.Dispatcher.CheckAccess())
            {
                OnUi(FlushPending);
                return;
            }

            if (_pending.IsEmpty) return;

            foreach (var key in _pending.Keys.ToList())
            {
                if (!_pending.TryRemove(key, out var report)) continue;
                if (_cardsByChannel.TryGetValue(key, out var card)) card.Apply(report);
            }

            RecomputeSummary();
            OnPropertyChanged(nameof(RoundElapsedText));
        }

        private void StopRound()
        {
            _log.Info("===== 收到停止烧录请求 =====");
            _roundCts?.Cancel();
            StatusText = "已请求停止：正在跑的那几路会各自收尾，其余不再启动";
        }

        private void FinishRound()
        {
            _flushTimer.Stop();
            FlushPending();
            _roundWatch?.Stop();
            _roundCts?.Dispose();
            _roundCts = null;

            foreach (var card in Channels) card.IsBusy = false;
            IsRunning = false;
            FlashRunState.SetRunning(false);
            RecomputeSummary();
            OnPropertyChanged(nameof(RoundElapsedText));
            OnPropertyChanged(nameof(PhaseText));

            // 上面收尾全部走完才放行关窗口的等待（中间任何一步抛异常也不能漏这一句）
            _roundDone?.TrySetResult();
            _roundDone = null;
        }

        /// <summary>
        /// 关窗口前调用：先请求停止，再泵送 UI 消息等本轮收尾（杀工具进程、关串口、写记录）跑完；等到超时也放行，不拦住用户。
        /// 不等就退进程，富芮坤工具会作为孤儿进程继续占着那一路的 COM，下一次开软件只看到"打开串口失败"，却查不出是谁占着。
        /// </summary>
        public void StopAndAwaitWrapUp(int timeoutMs = 10_000)
        {
            if (!IsRunning || _awaitingWrapUp) return;
            _awaitingWrapUp = true;

            try
            {
                _log.Info("===== 关闭窗口：先请求停止烧录，等本轮收尾 =====");
                _roundCts?.Cancel();

                var pending = _roundDone;
                if (pending == null || pending.Task.IsCompleted) return;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                // 两个回调都排回 UI 线程，避开"退出标志比 PushFrame 先生效"的抢时间
                var frame = new DispatcherFrame();
                pending.Task.ContinueWith(_ => dispatcher.Invoke(() => frame.Continue = false), TaskScheduler.Default);
                _ = Task.Delay(timeoutMs).ContinueWith(_ => dispatcher.Invoke(() => frame.Continue = false), TaskScheduler.Default);
                Dispatcher.PushFrame(frame);

                if (IsRunning)
                    _log.Warn($"烧录轮收尾等待 {timeoutMs / 1000}s 仍未完成，不阻拦退出；工具进程可能还占着那几路的串口");
            }
            finally
            {
                _awaitingWrapUp = false;
            }
        }

        /// <summary>清成未开始：换动作、或新一轮开始前</summary>
        private void ResetResults()
        {
            foreach (var card in Channels) card.Reset();
            RecomputeSummary();
            OnPropertyChanged(nameof(RoundElapsedText));
        }

        /// <summary>按卡片当前状态重算汇总计数与徽章</summary>
        private void RecomputeSummary()
        {
            var ok = 0;
            var fail = 0;
            var skipped = 0;
            var waiting = 0;
            var running = 0;
            var idle = 0;

            foreach (var card in Channels)
            {
                // 先分"落定了没"，落定的按结论归档（取消也算没通过），没落定的才分到等人/在跑
                if (card.Stage == FlashStage.Idle) idle++;
                else if (card.Stage == FlashStage.Skipped) skipped++;
                else if (card.Success == true) ok++;
                else if (card.Stage is FlashStage.Failed or FlashStage.Canceled) fail++;
                else if (card.Stage == FlashStage.WaitingReset) waiting++;
                else running++;
            }

            SuccessCount = ok;
            FailedCount = fail;
            SkippedCount = skipped;
            WaitingCount = waiting;
            RunningCount = running;
            IdleCount = idle;

            RoundSummaryText = $"本轮{SelectedJob.ToDisplayText()}（{CurrentPhase.ToDisplayText()}）：通过 {ok}｜未通过 {fail}｜跳过 {skipped}｜" +
                               $"进行中 {running}（等人按复位 {waiting}）｜未开始 {idle}";
            OnPropertyChanged(nameof(RoundVisual));
            OnPropertyChanged(nameof(VerdictText));
            OnPropertyChanged(nameof(TargetChannels));
        }

        // ==================== 配置保存与校验 ====================

        private void SaveConfig() => SaveConfigCore(quiet: false);

        /// <summary>落盘配置。quiet=true 是开轮前的自动保存：成功不吭声，失败必须吭声并让调用方别再往下开烧（配置没写进去，烧的是上一份）。</summary>
        private bool SaveConfigCore(bool quiet)
        {
            _log.Info($"{DateTime.Now} Start calling the FlashingViewModel.SaveConfigCore() method . {Environment.NewLine}");
            try
            {
                _configService.Save(_config);
                ValidateConfig();
                if (!quiet) StatusText = $"烧录配置已保存（{_config.Channels.Count} 路），下一轮就按这份跑";
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"保存烧录配置失败：{ex.Message}", ex);
                StatusText = $"保存失败：{ex.Message}";
                return false;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the FlashingViewModel.SaveConfigCore() method . {Environment.NewLine}");
            }
        }

        /// <summary>按盘上的配置重建通道清单（配置窗里的“重新载入”与没保存就关窗都走这里）</summary>
        private void ReloadConfig()
        {
            LoadConfig();
            StatusText = "已按盘上的配置重建通道清单（界面上没保存的改动会被覆盖）";
        }

        /// <summary>配置对话框（全局参数或通道配置）点确定时走这里：落盘、重拼校验、刷串口列表。</summary>
        public bool SaveConfigFromDialog() => SaveConfigCore(quiet: false);

        /// <summary>配置对话框里的“重新载入”：丢掉没保存的改动，按盘上那份重建</summary>
        public void ReloadConfigFromDisk() => ReloadConfig();

        /// <summary>某一行的串口或参与开关改了：马上重查一遍配置问题，让「烧录配置 → 通道配置」那页的红字同步更新。卡片实例在轮次之间不变，只在 <see cref="LoadConfig"/> 重建那一次挂上来；旧卡片连着它的委托一起被回收，不用手动摘。</summary>
        private void OnCardConfigChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(FlashChannelCardViewModel.WizProPortName)
                or nameof(FlashChannelCardViewModel.FreqchipPortName)
                or nameof(FlashChannelCardViewModel.Enabled))
                ValidateConfig();
        }

        /// <summary>重算配置问题清单并刷到界面上（按整批范围查，因为界面提示要说的是“现在这份配置能不能开整批”）</summary>
        private void ValidateConfig()
        {
            var issues = BuildIssues(null);
            ConfigIssueText = issues.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, issues.Select((text, i) => $"{i + 1}. {text}"));
        }

        /// <summary>
        /// 开烧前的问题清单：只列"放过去必然整批失败且看不出真因"的那几类。
        /// 工具/固件路径与芯片-波特率搭配由驱动自己的校验函数说，口径与编排那边完全一致，不在界面上另写一套。
        /// </summary>
        /// <param name="targets">只看这几路（卡片右键单独开烧时传那一路）；为空表示查整批要用的那些已启用的路</param>
        private List<string> BuildIssues(IReadOnlyList<int>? targets)
        {
            var issues = new List<string>();
            var livePorts = SerialPort.GetPortNames();
            var rows = Channels
                .Where(c => targets == null ? c.Enabled : targets.Contains(c.Channel))
                .ToList();
            var phases = SelectedJob.ToPhases();
            var needWiz = phases.Contains(FlashAction.WizProFlash);
            var needFreq = phases.Contains(FlashAction.FreqchipMcu);

            // 串口按两颗芯片各查一道：重复占用与配了已经不存在的口，是现场最常见的两种错位
            CheckPortGroup(issues, "烧录器串口", needWiz, rows
                .Select(c => (c.Channel, c.Title, Port: c.WizProPortName)), livePorts);
            CheckPortGroup(issues, "主控烧录串口", needFreq, rows
                .Select(c => (c.Channel, c.Title, Port: c.FreqchipPortName)), livePorts);

            // 本轮要跑的几个阶段都查：All 两段都要用，只查第一段会让第二段在卡片上报同一句路径错
            if (needWiz)
            {
                var problem = WizProProjectDownloader.CheckArguments(Config.WizPro);
                if (problem != null) issues.Add(problem);
            }

            if (needFreq)
            {
                // 逐路查工具与固件参数：只报第一个问题（路径这类全局错会在每路重复一遍，念着像八条路各有毛病）
                foreach (var card in rows)
                {
                    var problem = FreqchipFlasher.CheckArguments(Config.Freqchip, card.FreqchipPortName);
                    if (problem == null) continue;

                    issues.Add($"{card.Title}：{problem}");
                    break;
                }
            }

            return issues;
        }

        /// <summary>同一个动作下的串口逐路查：重复占用、本机已消失的口、本轮要用却没配的口</summary>
        private void CheckPortGroup(
            List<string> issues, string label, bool required,
            IEnumerable<(int Channel, string Title, string Port)> rows, string[] livePorts)
        {
            var filled = rows
                .Select(r => (r.Channel, r.Title, Port: r.Port.Trim()))
                .Where(r => r.Port.Length > 0)
                .ToList();

            foreach (var group in filled.GroupBy(r => r.Port, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                issues.Add($"{label} {group.Key} 被 {string.Join("、", group.Select(g => g.Title))} 同时占用，同一时刻只有一个程序能打开它");

            foreach (var row in filled.Where(r => !livePorts.Contains(r.Port, StringComparer.OrdinalIgnoreCase)))
                issues.Add($"{row.Title} 配的{label} {row.Port} 现在不在本机串口列表里（被拔了、或编号变了）");

            // 这一轮用不上的那颗芯片不算问题：只烧富芮坤时没配烧录器口不影响这一轮，反过来也是
            if (!required) return;

            var none = rows.Where(r => string.IsNullOrWhiteSpace(r.Port)).Select(r => r.Title).ToList();
            if (none.Count > 0)
                issues.Add($"{label}：{string.Join("、", none)} 没配（开烧时这些路会被直接判不过）");
        }

        // ==================== 明细与记录 ====================

        private void CopyTraceToClipboard()
        {
            var card = SelectedCard;
            if (card == null) return;

            try
            {
                var text = string.Join(Environment.NewLine, card.Trace);
                if (string.IsNullOrEmpty(text)) text = $"{card.Title} {card.Name}｜{card.StageText}｜{card.Detail}";
                Clipboard.SetText(text);
                StatusText = $"已复制 {card.Title} 的明细（{card.Trace.Count} 行），发给厂商时直接贴这份";
            }
            catch (Exception ex)
            {
                // 剪贴板被别的程序占着时只报一句，别让它把界面带崩
                _log.Warn($"复制明细失败：{ex.Message}");
                StatusText = $"复制失败：{ex.Message}";
            }
        }

        private void OpenRecordFolder()
        {
            try
            {
                Directory.CreateDirectory(_records.StorageDirectory);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_records.StorageDirectory}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _log.Warn($"打开记录文件夹失败：{ex.Message}");
                StatusText = $"打不开记录文件夹：{_records.StorageDirectory}";
            }
        }

        // ==================== 烧录器探测 ====================

        /// <summary>
        /// 逐个串口发 RDMC，把设备自报的通道号念出来给人对着填。只念不填：设备自报编号与治具接线的对应关系只有现场知道，替人填错比让人自己填更糟。
        /// 每个口都要等它回应或超时，口多时耗时长是预期的；进度按行念出来，避免被当成卡住。
        /// </summary>
        private async Task DetectProgrammersAsync()
        {
            IsDetecting = true;
            var lines = new List<string>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var ports = AvailablePorts;
                if (ports.Count == 0)
                {
                    DetectText = "本机没有可用串口";
                    return;
                }

                for (var i = 0; i < ports.Count; i++)
                {
                    var port = ports[i];
                    DetectText = $"探测中（{i + 1}/{ports.Count}）：{port}";
                    lines.Add(await ProbeOneAsync(port, cts.Token).ConfigureAwait(false));
                    DetectText = string.Join(Environment.NewLine, lines);
                }

                DetectText = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "没探到烧录器";
                StatusText = "探测结束：念到自报通道号的口，就按它填到对应通道那一行";
            }
            catch (Exception ex)
            {
                _log.Warn($"探测烧录器中断：{ex.Message}");
                StatusText = $"探测中断：{ex.Message}";
            }
            finally
            {
                IsDetecting = false;
            }
        }

        /// <summary>开一个口读一次自报身份：任何异常都转成一行文字，探测不能因为一个坏口整个停掉</summary>
        private async Task<string> ProbeOneAsync(string port, CancellationToken ct)
        {
            try
            {
                using IWizProProgrammer programmer = new WizProProgrammer(0, port, Config.WizPro, _log);
                await programmer.OpenAsync(ct).ConfigureAwait(false);
                var identity = await programmer.QueryIdentityAsync(ct).ConfigureAwait(false);
                await programmer.CloseAsync().ConfigureAwait(false);

                return identity == null
                    ? $"{port}：连上了但没回 RDMC（不是 WizPro 烧录器，或这块 USB 转串口芯片不支持 1M 波特率）"
                    : $"{port}：烧录器自报通道 {identity.ChannelId}｜{identity.ProductInfo}";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return $"{port}：打不开（{ex.Message.Split('\n')[0]}）";
            }
        }

        /// <summary>芯片系列下拉项：Value 存进配置，Label 给界面看</summary>
        public sealed record ChipOption(FreqchipChip Value, string Label);
    }
}
