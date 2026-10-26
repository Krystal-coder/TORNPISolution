using System.Collections.ObjectModel;
using System.Windows;
using TORNPISolution.Common;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 历史数据页 ViewModel：轮次列表 + 轮次明细 + 区间统计 + 补导旧轮次 PDF。
    /// 界面与 PDF 同一个查询、同一份结果；主流程不依赖本页——主流程那颗「导出报告」只导最近一轮。
    /// </summary>
    public class HistoryViewModel : ViewModelBase
    {
        private readonly ITestDataService _dataService;
        private readonly ITestReportService _reportService;
        private readonly ILogService _log = new LogService(nameof(HistoryViewModel));

        // 明细加载防串号：连点两轮时，只认最后一次点中的那轮
        private int _detailLoadId;

        public HistoryViewModel(ITestDataService dataService, ITestReportService reportService)
        {
            _dataService = dataService;
            _reportService = reportService;
            RefreshCommand = new RelayCommand(async () => await LoadAsync());
            ExportCommand = new RelayCommand(async () => await ExportAsync(), () => SelectedRun != null && !IsLoading);
            LoadStatCommand = new RelayCommand(async () => await LoadStatAsync());
            ExportStatCommand = new RelayCommand(async () => await ExportStatAsync(), () => HasStatData && !IsLoading);
        }

        /// <summary>轮次列表（收口时间倒序）</summary>
        public ObservableCollection<TestRunSummary> Runs { get; } = new();

        /// <summary>选中轮次的模块分节明细（随选中项切换）</summary>
        public ObservableCollection<ModuleDetail> Modules { get; } = new();

        // ==================== 区间统计 tab ====================

        /// <summary>统计区间起始（默认最近 7 天；DatePicker 被清空即 null）</summary>
        private DateTime? _statFrom = DateTime.Today.AddDays(-6);
        public DateTime? StatFrom
        {
            get => _statFrom;
            set
            {
                if (SetProperty(ref _statFrom, value?.Date))
                    InvalidateStat("统计区间已变更");
            }
        }

        /// <summary>统计区间止点（闭区间：含当天全天）</summary>
        private DateTime? _statTo = DateTime.Today;
        public DateTime? StatTo
        {
            get => _statTo;
            set
            {
                if (SetProperty(ref _statTo, value?.Date))
                    InvalidateStat("统计区间已变更");
            }
        }

        /// <summary>工位筛选（空 = 全部；可选值来自列表数据，可手输）</summary>
        private string? _statStation;
        public string? StatStation
        {
            get => _statStation;
            set
            {
                if (SetProperty(ref _statStation, value))
                    InvalidateStat("工位筛选已变更");
            }
        }

        /// <summary>方案筛选（空 = 全部；可选值来自列表数据，可手输）</summary>
        private string? _statPlanName;
        public string? StatPlanName
        {
            get => _statPlanName;
            set
            {
                if (SetProperty(ref _statPlanName, value))
                    InvalidateStat("方案筛选已变更");
            }
        }

        /// <summary>工位候选（从轮次列表去重：当前只有"整机"，列给未来单板/烧录混库）</summary>
        public ObservableCollection<string> StationOptions { get; } = new();

        /// <summary>方案候选（从轮次列表去重）</summary>
        public ObservableCollection<string> PlanOptions { get; } = new();

        private RangeStatResult? _rangeStat;
        /// <summary>最近一次区间统计结果（null = 还没统计过）</summary>
        public RangeStatResult? RangeStat
        {
            get => _rangeStat;
            private set
            {
                if (SetProperty(ref _rangeStat, value))
                {
                    OnPropertyChanged(nameof(HasStatData));
                    RequeryCommands();
                }
            }
        }

        /// <summary>统计是否有数据可看（汇总卡/导出按钮的可见性与可用性绑它）</summary>
        public bool HasStatData => _rangeStat is { HasData: true };

        /// <summary>模块通过率行（统计 tab 表格）</summary>
        public ObservableCollection<ModuleStat> ModuleStats { get; } = new();

        /// <summary>NG 原因汇总行（统计 tab 表格）</summary>
        public ObservableCollection<NgReason> NgReasons { get; } = new();

        /// <summary>按天趋势行（统计 tab 表格）</summary>
        public ObservableCollection<TrendPoint> Trend { get; } = new();

        private string _statStatusText = "还没有加载过统计——选好区间点「加载统计」（默认最近 7 天）";
        /// <summary>统计 tab 状态行（筛选提示/加载结果/导出结果/错误）</summary>
        public string StatStatusText
        {
            get => _statStatusText;
            private set => SetProperty(ref _statStatusText, value);
        }

        public RelayCommand LoadStatCommand { get; }
        public RelayCommand ExportStatCommand { get; }

        private TestRunSummary? _selectedRun;
        /// <summary>当前选中的轮次（切换即加载右侧明细）</summary>
        public TestRunSummary? SelectedRun
        {
            get => _selectedRun;
            set
            {
                if (!SetProperty(ref _selectedRun, value)) return;
                OnPropertyChanged(nameof(HasSelection));
                RequeryCommands();   // 导出按钮的可用性跟着选中项走
                _ = LoadDetailAsync();
            }
        }

        private TestRunDetail? _detail;
        /// <summary>选中轮次的明细头信息（编号/方案/MAC/起止/计数）</summary>
        public TestRunDetail? Detail
        {
            get => _detail;
            private set => SetProperty(ref _detail, value);
        }

        private bool _isLoading;
        /// <summary>正在加载（列表或明细）：加载中禁用导出，避免半份数据出手</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value)) RequeryCommands();
            }
        }

        private string _statusText = "正在加载…";
        /// <summary>底部状态行（加载结果/导出结果/错误）</summary>
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public bool HasSelection => SelectedRun != null;

        public RelayCommand RefreshCommand { get; }
        public RelayCommand ExportCommand { get; }

        /// <summary>窗口打开时加载列表（默认选中最近一轮，右边直接有东西看）</summary>
        public async Task LoadAsync()
        {
            IsLoading = true;
            try
            {
                Runs.Clear();
                Modules.Clear();
                Detail = null;
                _selectedRun = null;
                OnPropertyChanged(nameof(SelectedRun));
                OnPropertyChanged(nameof(HasSelection));

                // SQLite 没有真异步，整段查询是同步 I/O：不挪出 UI 线程，开页这几秒整个窗口点不动
                var runs = await Task.Run(() => _dataService.QueryRunsAsync());
                foreach (var r in runs) Runs.Add(r);
                RebuildStatOptions();
                StatusText = Runs.Count > 0
                    ? $"共 {Runs.Count} 轮（按完成时间倒序，最多显示最近 300 条）"
                    : "数据库里还没有测试记录——整机方案跑完会自动入库";

                if (Runs.Count > 0)
                {
                    // 占住选中项后显式 await 明细：走 SelectedRun setter 是 fire-and-forget，
                    // 本方法的 finally 会在明细回来前就把 IsLoading 清掉，加载态等于没有
                    _selectedRun = Runs[0];
                    OnPropertyChanged(nameof(SelectedRun));
                    OnPropertyChanged(nameof(HasSelection));
                    RequeryCommands();
                    await LoadDetailAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"加载历史轮次失败：{ex.Message}", ex);
                StatusText = $"加载失败：{ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>加载选中轮的明细（与 PDF 同一套口径）</summary>
        private async Task LoadDetailAsync()
        {
            var run = _selectedRun;
            if (run == null)
            {
                Modules.Clear();
                Detail = null;
                return;
            }

            var loadId = ++_detailLoadId;
            IsLoading = true;
            try
            {
                var detail = await Task.Run(() => _reportService.LoadRunDetailAsync(run.ReportNo));
                if (loadId != _detailLoadId) return;   // 已经点了别的轮次，这结果作废

                Detail = detail;
                Modules.Clear();
                foreach (var m in detail.Modules) Modules.Add(m);
                StatusText = $"编号 {run.ReportNo}：通过 {detail.PassCount} · 失败 {detail.FailCount} · 未测 {detail.NotTestedCount}";
            }
            catch (Exception ex)
            {
                if (loadId != _detailLoadId) return;
                _log.Error($"加载轮次明细失败（{run.ReportNo}）：{ex.Message}", ex);
                Modules.Clear();
                Detail = null;
                StatusText = $"明细加载失败：{ex.Message}";
            }
            finally
            {
                if (loadId == _detailLoadId) IsLoading = false;
            }
        }

        /// <summary>导出选中轮的 PDF（补导旧轮次就靠它；文件覆盖同名，产线补打场景）</summary>
        private async Task ExportAsync()
        {
            var run = _selectedRun;
            if (run == null) return;
            IsLoading = true;
            try
            {
                var path = await Task.Run(() => _reportService.ExportRunReportAsync(run.ReportNo));
                StatusText = $"已导出：{path}";
            }
            catch (Exception ex)
            {
                _log.Error($"导出报告失败（{run.ReportNo}）：{ex.Message}", ex);
                StatusText = $"导出失败：{ex.Message}";
                MessageBox.Show($"导出报告失败：{ex.Message}", "历史数据", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ==================== 区间统计 ====================

        /// <summary>筛选条件一变即让已加载的统计作废：界面表格与「导出统计」必须同源，
        /// 否则改了条件不重新加载，屏幕是旧结果、PDF 是新筛选。清空后导出按钮随之禁用。</summary>
        private void InvalidateStat(string reason)
        {
            if (_rangeStat == null) return;   // 本来就没加载过：不动空态引导文案
            RangeStat = null;
            ModuleStats.Clear();
            NgReasons.Clear();
            Trend.Clear();
            StatStatusText = $"{reason}——重新点「加载统计」";
        }

        /// <summary>按当前筛选条件加载区间统计（与统计 PDF 同一查询、同一份数据）</summary>
        private async Task LoadStatAsync()
        {
            var query = BuildStatQuery();   // 条件在 UI 线程取好再交给后台线程，不在后台读 VM 属性
            if (query == null)
            {
                StatStatusText = "区间无效：日期不能留空，且结束不能早于开始";
                return;
            }

            IsLoading = true;
            try
            {
                var stat = await Task.Run(() => _dataService.QueryRangeStatsAsync(query));
                RangeStat = stat;

                ModuleStats.Clear();
                foreach (var m in stat.Modules) ModuleStats.Add(m);
                NgReasons.Clear();
                foreach (var n in stat.NgReasons) NgReasons.Add(n);
                Trend.Clear();
                foreach (var t in stat.Trend) Trend.Add(t);

                StatStatusText = stat.HasData
                    ? $"{stat.RangeText}：{stat.RunCount} 轮，通过 {stat.TotalPass} · 失败 {stat.TotalFail} · 未测 {stat.TotalNotTested}，整体通过率 {stat.PassRateText}"
                    : $"{stat.RangeText}：区间内没有测试轮次";
            }
            catch (Exception ex)
            {
                _log.Error($"加载区间统计失败：{ex.Message}", ex);
                RangeStat = null;
                ModuleStats.Clear();
                NgReasons.Clear();
                Trend.Clear();
                StatStatusText = $"统计加载失败：{ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>导出区间统计 PDF（第 2 页）：图在文档里，界面只给表格——两边同一份数据</summary>
        private async Task ExportStatAsync()
        {
            var query = BuildStatQuery();   // 与界面同一份校验，无效区间不出文档
            if (query == null)
            {
                StatStatusText = "区间无效：日期不能留空，且结束不能早于开始";
                return;
            }

            IsLoading = true;
            try
            {
                var path = await Task.Run(() => _reportService.ExportRangeReportAsync(query));
                StatStatusText = $"已导出：{path}";
            }
            catch (Exception ex)
            {
                _log.Error($"导出区间统计失败：{ex.Message}", ex);
                StatStatusText = $"导出失败：{ex.Message}";
                MessageBox.Show($"导出区间统计失败：{ex.Message}", "历史数据", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>组装统计查询：空筛选按"不限"处理；日期留空或起大于止返回 null 当无效区间，
        /// 不回退成默认日期——界面显示的条件必须与实际出数的区间一致</summary>
        private RangeStatQuery? BuildStatQuery()
        {
            if (StatFrom is not { } from || StatTo is not { } to || to < from) return null;
            return new RangeStatQuery
            {
                From = from,
                To = to,
                Station = string.IsNullOrWhiteSpace(StatStation) ? null : StatStation,
                PlanName = string.IsNullOrWhiteSpace(StatPlanName) ? null : StatPlanName,
            };
        }

        /// <summary>工位/方案候选从轮次列表去重（列表已收口 300 条，候选即最近出现值；ComboBox 可手输补全）</summary>
        private void RebuildStatOptions()
        {
            StationOptions.Clear();
            foreach (var s in Runs.Select(r => r.Station).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s))
                StationOptions.Add(s);
            PlanOptions.Clear();
            foreach (var p in Runs.Select(r => r.PlanName).Where(p => !string.IsNullOrEmpty(p)).Distinct().OrderBy(p => p))
                PlanOptions.Add(p);
        }
    }
}
