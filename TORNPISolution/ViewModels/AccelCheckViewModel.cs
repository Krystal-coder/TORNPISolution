using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using TORNPISolution.Common;
using TORNPISolution.Data;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 加速度充电检查页：与整机测试共用被测产品串口，采样先落中转 CSV（切姿态只另起一段），停止保存时再从它固定出 PDF 报告与标红 xlsx，标红版落定即删 CSV。
    /// 结论与备注不入库，数据以这份 xlsx 为准，因此本页只做采集、比对与落盘，不产测试项、不进方案。
    /// </summary>
    public class AccelCheckViewModel : ViewModelBase
    {
        /// <summary>表格最多留多少行（落盘不限行数，一次保存一份文件）：再多就只是把界面拖慢</summary>
        private const int MaxUiRows = 10_000;

        /// <summary>一次定时器 tick 最多补多少行进表：采满时按批进场，避免逐帧跨线程排队把消息泵压死</summary>
        private const int DrainBatchRows = 500;

        private readonly AccelCheckService _service;
        private readonly ILogService _log = new LogService(nameof(AccelCheckViewModel));
        private readonly ConcurrentQueue<AccelCheckRow> _pending = new();
        private readonly DispatcherTimer _drainTimer;
        private bool _awaitingWrapUp;

        public AccelCheckViewModel(AccelCheckService service)
        {
            _service = service;
            _drainTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _drainTimer.Tick += (_, _) => DrainPending();

            _service.SampleJudged += OnSampleJudged;
            _service.StateChanged += OnServiceStateChanged;
            _service.ReportsGenerated += OnReportsGenerated;

            AngleTag = "0°平放";
            // 起始档位要交给运行层，否则采样记录里的「设置角度」是空的、标签名义角读不出来，整表只能显示"未比对"
            _service.ChangeAngleTag(AngleTag);
            IntervalText = _service.IntervalMs.ToString();
            ToleranceText = _service.ToleranceDegrees.ToString("0.#");

            ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => !_service.IsConnected && CanUsePort());
            // 传感器还在推流时不让人按断开：先看到「停止传感器」把流停了，再交口
            DisconnectCommand = new RelayCommand(async () => await _service.DisconnectAsync(),
                () => _service.IsConnected && !_service.IsSensorRunning && !_service.IsDisconnecting);
            StartSensorCommand = new RelayCommand(async () => await _service.StartSensorAsync(), () => _service.IsConnected && !_service.IsSensorRunning);
            StopSensorCommand = new RelayCommand(async () => await _service.StopSensorAsync(), () => _service.IsSensorRunning);
            ApplyIntervalCommand = new RelayCommand(async () => await ApplyIntervalAsync(), () => _service.IsConnected);
            QueryChargingCommand = new RelayCommand(async () => await _service.QueryChargingAsync(), () => _service.IsConnected);
            ChangeAngleTagCommand = new RelayCommand(ChangeAngleTag);
            TeachCommand = new RelayCommand(Teach, () => _service.Taught != null || _service.TeachSampleCount > 0);
            ApplyJudgementCommand = new RelayCommand(ApplyJudgement);
            ToggleSaveCommand = new RelayCommand(ToggleSave, () => _service.IsConnected);
            // 暂停只对已经打开的文件有意义：没开始保存时这颗键灰着，避免点下去什么都没发生
            PauseSaveCommand = new RelayCommand(TogglePauseSave, () => _service.IsSaving);
            ClearDisplayCommand = new RelayCommand(ClearDisplay, () => Rows.Count > 0);
            OpenFolderCommand = new RelayCommand(OpenFolder);
            ToggleChartCommand = new RelayCommand(ToggleChart);
            RollWindowText = DefaultRollWindowSeconds.ToString("0.#", CultureInfo.InvariantCulture);
            ApplyRollWindowCommand = new RelayCommand(ApplyRollWindow);
            RollZoomInCommand = new RelayCommand(() => ZoomRollWindow(0.5));
            RollZoomOutCommand = new RelayCommand(() => ZoomRollWindow(2));
            RollResetZoomCommand = new RelayCommand(RollResetZoom);
            RollFollowCommand = new RelayCommand(RollFollowLatest);
            ClearRollCurveCommand = new RelayCommand(ClearRollCurve);

            RollXAxes = new[] { BuildRollAxis("时间 (mm:ss)", 1, FormatClock) };
            // 纵轴最小步长 2°：Roll 是十几度的量，1° 一格会把网格铺满、标签挤成一片
            RollYAxes = new[] { BuildRollAxis("Roll (°)", 2, v => v.ToString("0.#", CultureInfo.InvariantCulture)) };

            RefreshState();
        }

        /// <summary>表格绑定源（只用来盯着看，全量数据在落盘文件里）</summary>
        public ObservableCollection<AccelCheckRow> Rows { get; } = new();

        // ==================== 界面状态 ====================

        /// <summary>产品串口还归不归这一路：连着就归，只有「断开」才交——主界面据此锁住返回键，整机/单板开新一轮前也问一句</summary>
        public bool IsPortBusy => _service.IsPortBusy;

        /// <summary>传感器还在推采样</summary>
        public bool IsSensorRunning => _service.IsSensorRunning;

        /// <summary>串口参数摘要（只读自仪器链接配置，本页不再单独配口）</summary>
        public string PortText => _service.IsConnected
            ? $"产品串口：{_service.PortSummary}（与整机测试共用）"
            : $"产品串口：{_service.PortSummary}，参数在「配置 → 仪器链接配置 → 被测产品（DUT）」里改";

        private string _angleTag = string.Empty;

        /// <summary>姿态标签：既是 CSV 的「设置角度」列，也提供判定偏差的名义角度</summary>
        public string AngleTag
        {
            get => _angleTag;
            set => SetProperty(ref _angleTag, value);
        }

        private string _intervalText = string.Empty;
        public string IntervalText
        {
            get => _intervalText;
            set => SetProperty(ref _intervalText, value);
        }

        private string _toleranceText = string.Empty;
        public string ToleranceText
        {
            get => _toleranceText;
            set => SetProperty(ref _toleranceText, value);
        }

        private string _compareAxis = AccelAngleJudge.AxisBoth;

        /// <summary>判哪一轴（Roll / Pitch / Roll&amp;Pitch）；名义角优先取示教值，没示教时用标签里的角</summary>
        public string CompareAxis
        {
            get => _compareAxis;
            set => SetProperty(ref _compareAxis, value);
        }

        /// <summary>判定轴可选项</summary>
        public string[] CompareAxisOptions => AccelAngleJudge.Axes;

        public string SummaryText
        {
            get
            {
                var file = Path.GetFileName(_service.CsvFile);
                var saved = !_service.IsSaving
                    ? "未保存"
                    : _service.IsSavePaused ? $"已暂停写入：{file}" : $"保存中：{file}";
                var interval = _service.MeasuredIntervalMs > 0 ? $"实测间隔 {_service.MeasuredIntervalMs} ms" : "还没测到帧间隔";
                // 滚离最新一条时表格不再追加，这个状态必须常驻（HintText 会被别的动作覆盖，靠不住）
                var tail = _followTail ? string.Empty : $"（表格停在 {Rows.Count} 条，滚回底部继续）";
                return $"采样 {_service.FrameCount} 条，丢帧 {_service.DroppedFrames} 条，{interval}，{saved}{tail}";
            }
        }

        /// <summary>电池与充电并成一条读数：工况条只给这一组留出短位的剩余空间，分两条并排会把整行挤窄</summary>
        public string BatteryText => _service.Battery == null
            ? "未读取"
            : $"{_service.Battery.VoltageMv} mV / {_service.Battery.Percentage}% · " +
              (_service.Charging == null ? "充电未读取" : _service.Charging.Value == 1 ? "充电中" : "未充电");

        /// <summary>同一颗按钮两种用途：没在存就叫「开始保存」，正在存就叫「停止保存」</summary>
        public string SaveButtonText => _service.IsSaving ? "停止保存" : "开始保存";

        /// <summary>同一颗按钮两种用途：正在写就叫「暂停保存」，暂停中就叫「继续保存」</summary>
        public string PauseSaveButtonText => _service.IsSavePaused ? "继续保存" : "暂停保存";

        /// <summary>标签里读不出名义角度、或改了判定条件后的提示；没有需要提醒的就空着</summary>
        private string _hintText = string.Empty;
        public string HintText
        {
            get => _hintText;
            set => SetProperty(ref _hintText, value);
        }

        /// <summary>置零按钮两种用途：没置零就叫「本姿态置零」，置过就叫「撤销置零」</summary>
        public string TeachButtonText => _service.Taught == null ? "本姿态置零" : "撤销置零";

        // ==================== 命令 ====================

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand StartSensorCommand { get; }
        public ICommand StopSensorCommand { get; }
        public ICommand ApplyIntervalCommand { get; }
        public ICommand QueryChargingCommand { get; }
        public ICommand ChangeAngleTagCommand { get; }
        public ICommand TeachCommand { get; }
        public ICommand ApplyJudgementCommand { get; }
        public ICommand ToggleSaveCommand { get; }
        public ICommand PauseSaveCommand { get; }
        public ICommand ClearDisplayCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand ToggleChartCommand { get; }

        /// <summary>整机/单板那一轮在跑、或这一路正在后台收尾时，这个口不归本页</summary>
        private bool CanUsePort() => !TestRunState.IsRunning && !_service.IsPortBusy;

        private async Task ConnectAsync()
        {
            if (!CanUsePort())
            {
                HintText = "整机或单板那一轮还在跑（或这一路正在收尾），先等它把串口交出来再连";
                return;
            }

            try
            {
                // 回执非空表示串口连上了但产品没应答进入产测，原样摊给操作台
                HintText = await _service.ConnectAsync();
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in AccelCheckViewModel.class ConnectAsync method, message: {ex}", ex);
                HintText = $"连接失败：{ex.Message}";
            }
            finally
            {
                RefreshState();
            }
        }

        private async Task ApplyIntervalAsync()
        {
            if (!int.TryParse(IntervalText.Trim(), out var ms) || ms <= 0)
            {
                HintText = "上报间隔要填正整数（毫秒）";
                return;
            }

            var ok = await _service.ApplyIntervalAsync(ms);
            HintText = ok
                ? $"上报间隔已下发 {ms} ms，看摘要里的实测间隔是否对得上（0x62 的单位以实机为准）"
                : $"上报间隔 {ms} ms 没等到应答，数值不改";
            OnPropertyChanged(nameof(SummaryText));
        }

        private void ChangeAngleTag()
        {
            var tag = AngleTag.Trim();
            if (tag.Length == 0)
            {
                HintText = "请先填姿态标签";
                return;
            }

            if (!_service.ChangeAngleTag(tag))
            {
                HintText = $"标签还是「{tag}」，这一段接着往下记（没另起段，序号不退回 1）";
                return;
            }

            var angle = AccelAngleJudge.ParseTagAngle(tag);
            var taught = _service.Taught;
            HintText = taught != null
                ? $"标签已切到「{tag}」，本档沿用已示教的名义角 Roll {taught.Roll:F2}° / Pitch {taught.Pitch:F2}°，另起一段（段内序号从 1）"
                : angle == null
                    ? $"标签「{tag}」里读不到角度数字，本档不会标红（只照常存文件）——想按实测置零就点「本姿态置零」"
                    : $"姿态标签已切到「{tag}」（名义角 {angle}°），另起一段（段内序号从 1）";
            RejudgeRows();
            RefreshState();
        }

        /// <summary>本姿态置零：把这一档实测均值写成名义角，此后按判定轴所选去比示教角；已置过则撤销</summary>
        private void Teach()
        {
            if (_service.Taught != null)
            {
                _service.ClearTeach();
                var textAngle = AccelAngleJudge.ParseTagAngle(AngleTag);
                HintText = textAngle == null
                    ? $"已撤销「{AngleTag}」的置零；这个标签里读不到角度，本档不再标红"
                    : $"已撤销「{AngleTag}」的置零，回到{JudgementCaption(_service.ToleranceDegrees)}的口径";
            }
            else
            {
                var nominal = _service.Teach();
                if (nominal == null)
                {
                    HintText = "这一档还没有采样，先启动传感器收到几条再置零";
                    return;
                }

                HintText = $"「{AngleTag}」名义角已置为 Roll {nominal.Roll:F2}° / Pitch {nominal.Pitch:F2}°"
                         + $"（{_service.TeachSampleCount} 条均值），此后{JudgementCaption(_service.ToleranceDegrees)}";
            }

            RejudgeRows();
            OnPropertyChanged(nameof(TeachButtonText));
        }

        /// <summary>把当前判定口径讲成一句：名义角优先取示教值；两轴都判又没示教时，标签里那个角按 Roll 用、Pitch 按 0° 判</summary>
        private string JudgementCaption(double tolerance)
            => CompareAxis switch
            {
                AccelAngleJudge.AxisBoth => _service.Taught != null
                    ? $"Roll 与 Pitch 各自比示教角，任一超过 {tolerance:0.#}° 标红"
                    : $"标签名义角按 Roll 判、Pitch 按 0° 判，任一超过 {tolerance:0.#}° 标红（两轴都想按实测判就先「本姿态置零」）",
                _ => _service.Taught != null
                    ? $"只比 {CompareAxis} 与示教角，相差超过 {tolerance:0.#}° 标红"
                    : $"只比 {CompareAxis} 与标签名义角，相差超过 {tolerance:0.#}° 标红"
            };

        /// <summary>容差与判定轴改了，已显示的行按新口径重算（原始值不动）</summary>
        private void ApplyJudgement()
        {
            if (!double.TryParse(ToleranceText.Trim(), out var tolerance) || tolerance < 0)
            {
                HintText = "判定角度要填 0 以上的数字（单位 °）";
                return;
            }

            _service.SetJudgement(tolerance, CompareAxis);
            RejudgeRows();
            HintText = $"判定口径已更新：{JudgementCaption(tolerance)}，已显示 {Rows.Count} 行随之重算";
        }

        private void ToggleSave()
        {
            if (_service.IsSaving)
            {
                var data = _service.StopSaving();
                HintText = data == null
                    ? "已停止保存：这一段没有数据，未生成文件（见日志）"
                    : "已停止保存：正在出 PDF 报告与标红 xlsx（中间的 CSV 出完即删），出来时点「打开目录」看";
            }
            else
            {
                var path = _service.StartSaving();
                HintText = $"开始保存：{path}（从现在起的采样才落盘）";
            }

            RefreshState();
        }

        private void TogglePauseSave()
        {
            var pause = !_service.IsSavePaused;
            _service.SetSavePaused(pause);
            HintText = pause
                ? "已暂停写入：表格照常刷新，只是这段采样不进文件（文件仍开着），摆好姿态后「继续保存」"
                : $"已继续写入：{AngleTag} 这一段接着记（文件序号数的是已写进去几行）";
            RefreshState();
        }

        private void ClearDisplay()
        {
            Rows.Clear();
            _pending.Clear();
            _followTail = true;
            HintText = "表格显示已清空，已落盘的文件不受影响、序号继续累加";
            RequeryCommands();
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(TestDataPaths.AccelCheckDirectory);
                Process.Start("explorer.exe", TestDataPaths.AccelCheckDirectory);
            }
            catch (Exception ex)
            {
                _log.Warn($"打开加速度数据目录失败：{ex.Message}");
                HintText = $"打不开目录：{ex.Message}";
            }
        }

        // ==================== 报告 ====================

        /// <summary>两份报告在后台线程渲染完：回执投回 UI 线程，别在这条线上直接改界面</summary>
        private void OnReportsGenerated(string? pdf, string? workbook)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                var parts = new List<string>();
                if (pdf != null) parts.Add($"报告 {Path.GetFileName(pdf)}");
                if (workbook != null) parts.Add($"标红表 {Path.GetFileName(workbook)}");
                HintText = parts.Count == 0
                    ? "报告没出来：这份采样里没有可统计的行（见日志），逐帧数据仍留在中转 CSV"
                    : $"已生成：{string.Join("、", parts)}" + (parts.Count == 1 ? "（另一份没出来，见日志）" : "");
            }));
        }

        // ==================== Roll 折线 ====================

        /// <summary>线色按姿态标签首次出现的顺序取，超过这一排就循环复用（一档一条线，十档够用）</summary>
        private static readonly SKColor[] RollPalette =
        {
            new SKColor(0x1F, 0x77, 0xB4), new SKColor(0xD6, 0x27, 0x28), new SKColor(0x2C, 0xA0, 0x2C),
            new SKColor(0x94, 0x67, 0xBD), new SKColor(0xFF, 0x7F, 0x0E), new SKColor(0x17, 0xBE, 0xCF),
            new SKColor(0xE3, 0x77, 0xC2), new SKColor(0xBC, 0xBD, 0x22), new SKColor(0x8C, 0x56, 0x4B),
            new SKColor(0x7F, 0x7F, 0x7F)
        };

        private readonly Dictionary<int, AccelRollLine> _rollLinesById = new();
        private readonly Dictionary<string, AccelRollLegendItem> _rollLegendByTag = new();
        private int _rollLegendColors;
        private int _rollSeenSegments;

        private bool _isChartVisible;

        /// <summary>曲线是否展开：默认折叠，折叠时不建线也不补画，只留数据层那份缓冲</summary>
        public bool IsChartVisible
        {
            get => _isChartVisible;
            private set => SetProperty(ref _isChartVisible, value);
        }

        public string ChartButtonText => IsChartVisible ? "收起曲线" : "Roll 曲线";

        /// <summary>图例：一个姿态标签一项，勾掉就不画它名下的段</summary>
        public ObservableCollection<AccelRollLegendItem> RollLegend { get; } = new();

        private ISeries[] _rollSeries = Array.Empty<ISeries>();

        /// <summary>整批换掉而不是增量改：段与勾选一变就重挂一次，图表按新数组重建，不会留下勾掉的线</summary>
        public ISeries[] RollSeries
        {
            get => _rollSeries;
            private set => SetProperty(ref _rollSeries, value);
        }

        public Axis[] RollXAxes { get; }

        public Axis[] RollYAxes { get; }

        // ==================== Roll 横轴窗口 ====================

        /// <summary>横轴窗口默认宽度（秒）：「重置缩放」回到它，采样中开了窗就贴着尾部滚</summary>
        private const double DefaultRollWindowSeconds = 60;

        /// <summary>窗口最窄 10 秒：再窄连一个姿态段都放不下</summary>
        private const double MinRollWindowSeconds = 10;

        private string _rollWindowText = string.Empty;

        /// <summary>横轴窗口输入框文案（秒）</summary>
        public string RollWindowText
        {
            get => _rollWindowText;
            set => SetProperty(ref _rollWindowText, value);
        }

        private double _rollWindowSeconds = DefaultRollWindowSeconds;
        private bool _rollFollowTail;

        /// <summary>「应用」：按填的秒数开窗，窗口贴着最新数据，采样中跟着尾部滚</summary>
        public ICommand ApplyRollWindowCommand { get; }

        /// <summary>放大：窗口围绕中点缩窄一半</summary>
        public ICommand RollZoomInCommand { get; }

        /// <summary>缩小：窗口围绕中点放宽一倍；盖住全部数据就回全量视图</summary>
        public ICommand RollZoomOutCommand { get; }

        /// <summary>重置缩放：回默认窗口宽度并贴着最新数据</summary>
        public ICommand RollResetZoomCommand { get; }

        /// <summary>回到最新：窗口拨回贴着尾部</summary>
        public ICommand RollFollowCommand { get; }

        /// <summary>清空曲线：连同缓冲与图例一起撤掉，落盘文件不受影响，新采样从空白重画</summary>
        public ICommand ClearRollCurveCommand { get; }

        private void ToggleChart()
        {
            IsChartVisible = !IsChartVisible;
            OnPropertyChanged(nameof(ChartButtonText));

            if (IsChartVisible) RebuildRollChart();
            else
            {
                _rollLinesById.Clear();
                RollSeries = Array.Empty<ISeries>();
            }
        }

        /// <summary>按当前勾选重画一遍：段数就是切过的标签次数（一档一条线），整表重建比逐条增量维护不易错位</summary>
        private void RebuildRollChart()
        {
            var trace = _service.RollTrace;
            var segments = trace.Segments();
            var series = new List<ISeries>();
            _rollLinesById.Clear();
            _rollSeenSegments = segments.Length;

            // 图上没有这个标签的段就把图例项一起撤掉：重连清空缓冲后，图例不该还挂着上一台产品采过的档
            var alive = new HashSet<string>(segments.Select(s => s.Tag));
            foreach (var stale in _rollLegendByTag.Where(kv => !alive.Contains(kv.Key)).ToList())
            {
                stale.Value.Changed -= RebuildRollChart;
                _rollLegendByTag.Remove(stale.Key);
                RollLegend.Remove(stale.Value);
            }

            foreach (var segment in segments)
            {
                var legend = _rollLegendByTag.GetValueOrDefault(segment.Tag) ?? AddRollLegendItem(segment.Tag);
                if (!legend.IsChecked) continue;

                var points = new RangeObservableCollection<ObservablePoint>();
                var line = new AccelRollLine(segment, points, new LineSeries<ObservablePoint>
                {
                    Values = points,
                    Name = segment.Tag,
                    Stroke = new SolidColorPaint(legend.Color) { StrokeThickness = 1.5f },
                    Fill = null,
                    GeometrySize = 0,
                    LineSmoothness = 0,
                    AnimationsSpeed = TimeSpan.Zero
                });

                var chunk = segment.Snapshot();
                foreach (var point in chunk.Points)
                    points.Add(ToRollPoint(point, trace.SessionStart));

                line.Cursor = chunk.Count;
                line.Generation = chunk.Generation;
                _rollLinesById[segment.Id] = line;
                series.Add(line.Series);
            }

            RollSeries = series.ToArray();
        }

        private AccelRollLegendItem AddRollLegendItem(string tag)
        {
            var item = new AccelRollLegendItem(tag, RollPalette[_rollLegendColors++ % RollPalette.Length]);
            item.Changed += RebuildRollChart;
            _rollLegendByTag[tag] = item;
            RollLegend.Add(item);
            return item;
        }

        /// <summary>跟着采样补画：段数一变就整表重建，平时只把各线缺的尾部补上，抽稀过的那段整段重抄</summary>
        private void PumpRollChart()
        {
            var trace = _service.RollTrace;
            var segments = trace.Segments();
            if (segments.Length != _rollSeenSegments)
            {
                RebuildRollChart();
                return;
            }

            foreach (var segment in segments)
            {
                var line = _rollLinesById.GetValueOrDefault(segment.Id);
                if (line == null) continue;   // 这个标签没勾上，不画

                var chunk = segment.ReadFrom(line.Cursor, line.Generation);
                if (chunk.Full)
                {
                    // 刚抽稀过，缓冲里的下标已经错位：整段重抄，且一次换掉（逐条加会把图表按点数重算几遍）
                    line.Points.ReplaceRange(chunk.Points.Select(p => ToRollPoint(p, trace.SessionStart)));
                }
                else
                {
                    foreach (var point in chunk.Points)
                        line.Points.Add(ToRollPoint(point, trace.SessionStart));
                }

                line.Cursor = chunk.Count;
                line.Generation = chunk.Generation;
            }

            if (_rollFollowTail && RollXAxes[0].MinLimit.HasValue)
            {
                var tail = RollTailSeconds();
                if (tail > RollXAxes[0].MaxLimit)
                    SetRollWindow(tail);
            }
        }

        /// <summary>当前曲线最右一帧距会话起点的秒数；没有数据时为 0</summary>
        private double RollTailSeconds()
        {
            var origin = _service.RollTrace.SessionStart;
            var tail = 0.0;
            foreach (var segment in _service.RollTrace.Segments())
            {
                if (segment.TotalSamples == 0) continue;
                var at = (segment.LastAt - origin).TotalSeconds;
                if (at > tail) tail = at;
            }
            return tail;
        }

        /// <summary>把横轴窗口定到 [tail-W, tail]，窗口宽度取当前口径，尾部不足就往前借</summary>
        private void SetRollWindow(double tail)
        {
            var x = RollXAxes[0];
            var width = Math.Max(MinRollWindowSeconds, _rollWindowSeconds);
            x.MinLimit = Math.Max(0, tail - width);
            x.MaxLimit = Math.Max(x.MinLimit.Value + width, tail);
        }

        /// <summary>「应用」：按填的秒数开窗并贴着最新数据，之后采样中跟着尾部滚</summary>
        private void ApplyRollWindow()
        {
            if (!double.TryParse(RollWindowText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                || seconds < MinRollWindowSeconds)
            {
                HintText = $"横轴窗口要填 {MinRollWindowSeconds:0.#} 秒以上的数字";
                return;
            }

            _rollWindowSeconds = seconds;
            _rollFollowTail = true;
            var tail = RollTailSeconds();
            if (tail <= 0)
            {
                HintText = $"横轴窗口已设为 {seconds:0.#} 秒，等采到数据就贴着尾部滚";
                return;
            }

            SetRollWindow(tail);
            HintText = $"横轴窗口 {seconds:0.#} 秒，贴着最新数据（采样中跟着滚）";
        }

        /// <summary>放大/缩小：窗口围绕中点缩放；缩到盖住全部数据就干脆回到全量视图</summary>
        private void ZoomRollWindow(double factor)
        {
            var x = RollXAxes[0];
            var tail = RollTailSeconds();
            if (tail <= 0)
            {
                HintText = "还没有可缩放的曲线数据";
                return;
            }

            if (!x.MinLimit.HasValue || !x.MaxLimit.HasValue)
            {
                _rollFollowTail = false;
                SetRollWindow(tail);
            }

            var span = (x.MaxLimit!.Value - x.MinLimit!.Value) * factor;
            if (span >= tail)
            {
                x.MinLimit = null;
                x.MaxLimit = null;
                _rollFollowTail = false;
                HintText = $"横轴已回到全量视图（0 ~ {FormatClock(tail)}）";
                return;
            }

            var width = Math.Max(MinRollWindowSeconds, span);
            var center = (x.MinLimit.Value + x.MaxLimit.Value) / 2;
            var half = width / 2;
            var min = Math.Min(Math.Max(0, center - half), Math.Max(0, tail - width));
            x.MinLimit = min;
            x.MaxLimit = min + width;
            _rollWindowSeconds = width;
            RollWindowText = width.ToString("0.#", CultureInfo.InvariantCulture);
            HintText = $"横轴窗口 {width:0.#} 秒（中点 {FormatClock(center)}）";
        }

        /// <summary>「重置缩放」：回默认窗口宽度并贴着最新数据</summary>
        private void RollResetZoom()
        {
            _rollWindowSeconds = DefaultRollWindowSeconds;
            RollWindowText = DefaultRollWindowSeconds.ToString("0.#", CultureInfo.InvariantCulture);
            _rollFollowTail = true;
            var tail = RollTailSeconds();
            if (tail <= 0)
            {
                RollXAxes[0].MinLimit = null;
                RollXAxes[0].MaxLimit = null;
                HintText = $"横轴已重置：默认窗口 {DefaultRollWindowSeconds:0.#} 秒，等采到数据就贴着尾部滚";
                return;
            }

            SetRollWindow(tail);
            HintText = $"横轴已重置：默认窗口 {DefaultRollWindowSeconds:0.#} 秒，贴着最新数据（采样中跟着滚）";
        }

        /// <summary>「回到最新」：窗口拨回贴着尾部，窗口宽度保持现场</summary>
        private void RollFollowLatest()
        {
            var tail = RollTailSeconds();
            if (tail <= 0)
            {
                HintText = "还没有可跟随的曲线数据";
                return;
            }

            _rollFollowTail = true;
            SetRollWindow(tail);
            HintText = $"横轴已拨回最新数据（窗口 {Math.Max(MinRollWindowSeconds, _rollWindowSeconds):0.#} 秒）";
        }

        /// <summary>「清空曲线」：缓冲、图例、线全撤，窗口回到全量默认；落盘文件不受影响，等新采样从空白重画</summary>
        private void ClearRollCurve()
        {
            _service.RollTrace.Reset();
            _rollLinesById.Clear();
            _rollSeenSegments = 0;
            _rollLegendColors = 0;
            foreach (var legend in _rollLegendByTag.Values)
                legend.Changed -= RebuildRollChart;
            _rollLegendByTag.Clear();
            RollLegend.Clear();
            RollSeries = Array.Empty<ISeries>();

            RollXAxes[0].MinLimit = null;
            RollXAxes[0].MaxLimit = null;
            _rollWindowSeconds = DefaultRollWindowSeconds;
            RollWindowText = DefaultRollWindowSeconds.ToString("0.#", CultureInfo.InvariantCulture);
            _rollFollowTail = false;
            HintText = "Roll 曲线已清空，等新采样从空白重画（落盘的文件不受影响）";
        }

        /// <summary>横轴用相对会话起点的秒数：采样间隔改了也不用重排，鼠标读数与表里的时间对得上同一段</summary>
        private static ObservablePoint ToRollPoint(AccelRollPoint point, DateTime origin)
            => new((point.At - origin).TotalSeconds, point.Roll);

        private static string FormatClock(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Abs(seconds));
            return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
        }

        /// <summary>粉色主网格 + 每格再切 4 份的小网格、黑字 11 号（轴名同字号），与 ECG 波形页同一档口径</summary>
        private static Axis BuildRollAxis(string name, double minStep, Func<double, string> labeler) => new()
        {
            Name = name,
            NamePaint = new SolidColorPaint(SKColors.Black),
            LabelsPaint = new SolidColorPaint(SKColors.Black),
            TextSize = 11,
            NameTextSize = 11,
            MinStep = minStep,
            Labeler = labeler,
            SeparatorsPaint = new SolidColorPaint(new SKColor(255, 182, 193)) { StrokeThickness = 1 },
            SubseparatorsCount = 4,
            SubseparatorsPaint = new SolidColorPaint(new SKColor(255, 182, 193, 100)) { StrokeThickness = 0.75f }
        };

        /// <summary>离开本页：只收界面定时器；没点「断开」交出口就出不去，所以走到这里时口、产测态与采样文件都已收干净</summary>
        public void LeavePage()
        {
            _drainTimer.Stop();
            _log.Info($"已离开加速度充电检查页：{_service.PortSummary}");
        }

        public void EnterPage()
        {
            RefreshState();
        }

        /// <summary>关窗口前调用：限时收尾（CSV 必须落完，产品不能留在推流态）</summary>
        public void StopAndAwaitWrapUp(int timeoutMs = 5_000)
        {
            _drainTimer.Stop();
            if (_awaitingWrapUp) return;   // 消息泵期间再进来一次（比如又点一次关闭）直接返回，不叠消息泵

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                _service.StopAndAwaitWrapUp(timeoutMs);
                return;
            }

            _awaitingWrapUp = true;
            try
            {
                _log.Info($"===== 关闭窗口：加速度充电检查限时收尾（最长 {timeoutMs / 1000}s）=====");

                // 服务层不引用 WPF，泵只能架在这层：收尾丢给线程池跑，UI 线程继续泵消息等它，界面不再被 5 秒硬阻塞钉死
                var pending = Task.Run(() => _service.StopAndAwaitWrapUp(timeoutMs));
                var frame = new DispatcherFrame();
                pending.ContinueWith(_ => dispatcher.Invoke(() => frame.Continue = false), TaskScheduler.Default);
                _ = Task.Delay(timeoutMs).ContinueWith(_ => dispatcher.Invoke(() => frame.Continue = false), TaskScheduler.Default);
                Dispatcher.PushFrame(frame);

                if (!pending.IsCompleted)
                    _log.Warn($"加速度充电检查收尾等待 {timeoutMs / 1000}s 仍未完成，不阻拦退出；产品可能仍在产测态或持续推采样");
            }
            finally
            {
                _awaitingWrapUp = false;
            }
        }

        // ==================== 采样进表 ====================

        /// <summary>表格是否贴着最新一条：滚上去看旧数据时暂停追加，滚回来补上（边追加边拖滚动条会被反复重算的视口顶回去）</summary>
        private bool _followTail = true;
        private long _summaryFrames;

        /// <summary>界面侧回报视口位置（ScrollChanged 里算好的"在不在最底下"）</summary>
        public void SetTailFollow(bool follow)
        {
            if (_followTail == follow) return;

            _followTail = follow;
            OnPropertyChanged(nameof(SummaryText));
            if (follow) DrainPending();
        }

        /// <summary>读线程回调：只入队，定时器由 UI 线程操控，跨线程刷新按批补</summary>
        private void OnSampleJudged(AccelCheckRecord record)
        {
            _pending.Enqueue(new AccelCheckRow(record));
        }

        private void DrainPending()
        {
            if (_followTail)
            {
                var added = 0;
                while (added < DrainBatchRows && Rows.Count < MaxUiRows && _pending.TryDequeue(out var row))
                {
                    Rows.Add(row);
                    added++;
                }

                // 表格到顶就停住显示（文件照旧全量落盘）：滚动删头在万行级会把 UI 拖垮，这里宁可不显示也不卡
                if (Rows.Count >= MaxUiRows && _pending.Count > 0)
                    HintText = $"表格已显示满 {MaxUiRows} 条并暂停刷新，文件仍在继续写；点「清空显示」接着看新数据";
            }

            if (_service.FrameCount != _summaryFrames)
            {
                _summaryFrames = _service.FrameCount;
                OnPropertyChanged(nameof(SummaryText));
            }

            // 曲线跟着同一次 tick 补：表格滚上去不再追加时，线照画
            if (_isChartVisible) PumpRollChart();

            if (_pending.Count == 0 && !_service.IsSensorRunning) _drainTimer.Stop();
        }

        private void RejudgeRows()
        {
            // 快照后再改：重算途中集合若被插进一条，直接枚举会抛"集合已修改"
            foreach (var row in Rows.ToArray())
                row.Apply(AccelAngleJudge.Evaluate(row.Record.Seq, row.Record.AngleTag, row.Record.Sample,
                    _service.CompareAxis, _service.ToleranceDegrees,
                    _service.TaughtFor(row.Record.AngleTag)));
        }

        private void OnServiceStateChanged()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            if (dispatcher.CheckAccess()) RefreshState();
            else dispatcher.BeginInvoke(new Action(RefreshState));
        }

        private void RefreshState()
        {
            // 采集起来就按周期补表，停下来且队列排空才收定时器：读线程不碰 DispatcherTimer
            if (_service.IsSensorRunning) _drainTimer.Start();

            // 重连会清空曲线缓冲，展开着曲线就得照新的一份重画，否则线上还挂着上一台产品的段
            if (_isChartVisible) RebuildRollChart();

            OnPropertyChanged(nameof(PortText));
            OnPropertyChanged(nameof(IsPortBusy));
            OnPropertyChanged(nameof(IsSensorRunning));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(BatteryText));
            OnPropertyChanged(nameof(SaveButtonText));
            OnPropertyChanged(nameof(PauseSaveButtonText));
            RequeryCommands();
        }

        /// <summary>界面上的一条线：一段曲线配一份显示点集，游标记着已经画到第几个点</summary>
        private sealed class AccelRollLine
        {
            public AccelRollLine(AccelRollSegment segment, RangeObservableCollection<ObservablePoint> points,
                                 LineSeries<ObservablePoint> series)
            {
                Segment = segment;
                Points = points;
                Series = series;
            }

            public AccelRollSegment Segment { get; }

            public RangeObservableCollection<ObservablePoint> Points { get; }

            public LineSeries<ObservablePoint> Series { get; }

            /// <summary>已画到的缓冲下标；抽稀一次就整段重来</summary>
            public int Cursor { get; set; }

            public int Generation { get; set; }
        }
    }

    /// <summary>Roll 曲线图例一项：一个姿态标签一种线色，勾选变化就重画</summary>
    public sealed class AccelRollLegendItem : ViewModelBase
    {
        public AccelRollLegendItem(string tag, SKColor color)
        {
            Tag = tag;
            Color = color;
            // 属性名 Color 是本实例的 SKColor，会把 WPF 的 Color 类型名盖掉，这里只能写全名
            Brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.Red, color.Green, color.Blue));
        }

        public string Tag { get; }

        public SKColor Color { get; }

        /// <summary>图例上那个色块用的刷子（Skia 的颜色给图表，WPF 的刷子给图例）</summary>
        public Brush Brush { get; }

        private bool _isChecked = true;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (!SetProperty(ref _isChecked, value)) return;
                Changed?.Invoke();
            }
        }

        public event Action? Changed;
    }

    /// <summary>
    /// 表格一行：一条采样连同它与姿态标签名义角的比对结果；超容差整行标红。
    /// </summary>
    public class AccelCheckRow : ViewModelBase
    {
        public AccelCheckRow(AccelCheckRecord record)
        {
            _record = record;
            _isExceeded = record.Exceeded;
        }

        private AccelCheckRecord _record;

        /// <summary>原始记录（改判定口径时按它重算）</summary>
        public AccelCheckRecord Record => _record;

        public int Seq => Record.Seq;
        public string AngleTag => Record.AngleTag;
        public short X => Record.Sample.X;
        public short Y => Record.Sample.Y;
        public short Z => Record.Sample.Z;
        public string RollText => Record.Sample.RollDegrees.ToString("F2");
        public string PitchText => Record.Sample.PitchDegrees.ToString("F2");
        public string ResultantText => Record.ResultantG.ToString("F4");
        /// <summary>本条口径那一轴的实测角：两轴都判时它可能是 Roll 也可能是 Pitch（取偏差大的那一轴）</summary>
        public string CompareText => Record.CompareAngle.ToString("F2");
        public string DeviationText => Record.Deviation?.ToString("F2") ?? "—";
        public string TimeText => Record.Sample.ReceivedAt.ToString("HH:mm:ss.fff");
        public string VerdictText => Record.TagAngle == null ? "未比对" : (Record.Exceeded ? "超限" : "合格");

        private bool _isExceeded;

        /// <summary>超容差（整行标红的依据）；标签读不出角度时不标</summary>
        public bool IsExceeded
        {
            get => _isExceeded;
            private set => SetProperty(ref _isExceeded, value);
        }

        /// <summary>按新口径重算：原始字节不动，只有比对结果与颜色变</summary>
        public void Apply(AccelCheckRecord record)
        {
            _record = record;
            IsExceeded = record.Exceeded;

            OnPropertyChanged(nameof(Seq));
            OnPropertyChanged(nameof(AngleTag));
            OnPropertyChanged(nameof(X));
            OnPropertyChanged(nameof(Y));
            OnPropertyChanged(nameof(Z));
            OnPropertyChanged(nameof(RollText));
            OnPropertyChanged(nameof(PitchText));
            OnPropertyChanged(nameof(ResultantText));
            OnPropertyChanged(nameof(CompareText));
            OnPropertyChanged(nameof(DeviationText));
            OnPropertyChanged(nameof(TimeText));
            OnPropertyChanged(nameof(VerdictText));
        }
    }
}
