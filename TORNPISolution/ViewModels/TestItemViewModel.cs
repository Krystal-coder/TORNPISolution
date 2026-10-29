using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using TORNPISolution.Common;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;
using TORNPISolution.Service;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 测试项 ViewModel：测试内容 + 规格 + 测试值 + 判定结果
    /// </summary>
    public class TestItemViewModel : ViewModelBase
    {
        private readonly TestItemModel _model;

        private double? _measuredValue;
        private double? _dynamicLower;
        private double? _dynamicUpper;
        private double? _instrumentValue;
        private double? _productValue;
        private string? _instrumentText;
        private string? _productText;
        private bool _reportedNotJudged;
        private bool _displayOnly;
        private TestResultStatus _result = TestResultStatus.NotTested;

        public TestItemViewModel(TestItemModel model)
        {
            _model = model;
            TimeScale = ResolveTimeScale(model);
        }

        /// <summary>横轴倍率默认值（像素/秒）：目录声明不吃配置、或配置里没这项时使用</summary>
        private const double DefaultTimeScale = 24;

        /// <summary>波形横轴倍率（像素/秒），按目录声明的配置来源读取，未配置时为默认值</summary>
        public double TimeScale { get; }

        /// <summary>底层测试项定义（供 TestItem 执行测量时使用）</summary>
        public TestItemModel Model => _model;

        /// <summary>测试内容名称</summary>
        public string Name => _model.Name;

        /// <summary>测试内容说明</summary>
        public string Description => _model.Description;

        /// <summary>明细表分节标题（目录里声明，同一模块内相同的项归为一节；空 = 本节无标题）</summary>
        public string GroupLabel => _model.GroupLabel;

        /// <summary>该项是否上抛波形数据（行内折叠与ECG Signal都算，快照/清空按这个口径走）</summary>
        public bool HasWaveform => _model.Waveform != WaveformPresentation.None;

        /// <summary>行内折叠模式：明细行下方展开一小张图，只看这一项自己的采样</summary>
        public bool HasInlineWaveform => _model.Waveform == WaveformPresentation.Inline;

        /// <summary>ECG Signal模式：本模块各段曲线拼成一张长图，进独立窗口看（模式由 Spec 目录声明，不按项名判断）</summary>
        public bool HasOverviewWaveform => _model.Waveform == WaveformPresentation.Overview;

        /// <summary>行内“波形”展开按钮</summary>
        public bool ShowWaveformToggle => HasInlineWaveform;

        /// <summary>该项波形是否展开（行内折叠按钮控制，默认折叠；展开时行下方显示压力曲线）</summary>
        public bool IsWaveformExpanded
        {
            get => _isWaveformExpanded;
            set
            {
                if (SetProperty(ref _isWaveformExpanded, value))
                    OnPropertyChanged(nameof(HasRowDetails));
            }
        }
        private bool _isWaveformExpanded;

        /// <summary>逐点子结果（BP-003 等逐点测试项）：行详情子表逐点展示哪个点 PASS / 哪个点 FAIL</summary>
        public ObservableCollection<TestPointResult> PointResults { get; } = new();

        /// <summary>子表动态列头（含首列行名列与末列判定列）：列数随 Points 口径变化，不再写死在 XAML 里</summary>
        public ObservableCollection<SubTableColumn> SubTableColumns { get; } = new();

        /// <summary>子表数据行（与 SubTableColumns 同列序，单元格宽度取自列定义保证对齐）</summary>
        public ObservableCollection<SubTableRow> SubTableRows { get; } = new();

        /// <summary>子表标题（按口径给出，只说明“这张表是什么”，判据看父行的规格列）</summary>
        public string PointTableTitle { get; private set; } = string.Empty;

        /// <summary>是否有逐点子结果（控制行详情子表显示）</summary>
        public bool HasPointResults => PointResults.Count > 0;

        /// <summary>逐点结果摘要（子表标题栏显示）：只统计参与判定的行，多轮表里的均值/极大/极小等中间行不计入</summary>
        public string PointSummaryText
        {
            get
            {
                var judged = PointResults.Where(p => p.ShowVerdict).ToList();
                if (judged.Count == 0) return string.Empty;
                var passCount = judged.Count(p => p.Pass);
                var worstErr = judged
                    .Where(p => p.Error.HasValue)
                    .OrderByDescending(p => p.Error!.Value)
                    .FirstOrDefault();
                // 只有一行参与判定（重复性项的极差行）时直接报该行，不加“共1行”前缀
                var text = judged.Count == 1
                    ? $"{judged[0].Name} {(judged[0].Pass ? "PASS" : "FAIL")}"
                    : $"共{judged.Count}行判定：{passCount} PASS / {judged.Count - passCount} FAIL";
                if (worstErr != null)
                    text += $"，最大 {worstErr.Error!.Value:0.##} {Unit}（{worstErr.Name}）";
                return text;
            }
        }

        /// <summary>行详情是否有内容（逐点子表或波形任一展开即显示，避免空详情行）</summary>
        public bool HasRowDetails => HasPointResults || IsWaveformExpanded;

        /// <summary>该项已采集的波形采样点快照（MainViewModel 按项名+曲线号分发，波形控件绑定绘制）：仪器输出曲线</summary>
        public IReadOnlyList<PressureSamplePoint> WaveformSamples
        {
            get => _waveformSamples;
            set => SetProperty(ref _waveformSamples, value);
        }
        private IReadOnlyList<PressureSamplePoint> _waveformSamples = Array.Empty<PressureSamplePoint>();

        /// <summary>产品回传波形采样点快照（仅 A10 ECG-009 等双波形项有数据，其余项恒为空）</summary>
        public IReadOnlyList<PressureSamplePoint> ProductWaveformSamples
        {
            get => _productWaveformSamples;
            set
            {
                if (SetProperty(ref _productWaveformSamples, value))
                    OnPropertyChanged(nameof(HasProductWaveform));
            }
        }
        private IReadOnlyList<PressureSamplePoint> _productWaveformSamples = Array.Empty<PressureSamplePoint>();

        /// <summary>是否有产品回传波形（有则在仪器曲线下方再画一张产品曲线）</summary>
        public bool HasProductWaveform => _productWaveformSamples.Count > 0;

        /// <summary>规格下限（TestItem 带回动态规格时优先用动态值，否则用 Spec 静态值）</summary>
        public double LowerLimit => _dynamicLower ?? _model.LowerLimit;

        /// <summary>规格上限（TestItem 带回动态规格时优先用动态值，否则用 Spec 静态值）</summary>
        public double UpperLimit => _dynamicUpper ?? _model.UpperLimit;

        /// <summary>规格是否为本次运行动态生成（仪器读回值现算）</summary>
        public bool IsDynamicSpec => _dynamicLower.HasValue || _dynamicUpper.HasValue;

        /// <summary>单位</summary>
        public string Unit => _model.Unit;

        /// <summary>规格显示文本：定义了字符串规格（动态规格带 * 标识）</summary>
        public string SpecText => !string.IsNullOrEmpty(_model.SpecText)
            ? _model.SpecText
            : (_model.HideSpecRange
                ? "有响应即通过"
                : IsDynamicSpec
                    ? $"{LowerLimit:0.###} ~ {UpperLimit:0.###} {Unit} *"
                    : $"{LowerLimit:0.###} ~ {UpperLimit:0.###} {Unit}");

        /// <summary>测试值（null 表示未测试）</summary>
        public double? MeasuredValue
        {
            get => _measuredValue;
            set
            {
                if (SetProperty(ref _measuredValue, value))
                {
                    // 测试值变化后自动重新判定
                    Judge();
                    OnPropertyChanged(nameof(MeasuredValueText));
                }
            }
        }

        /// <summary>测试值显示文本（NaN = 执行端根本没取到有效测量值，列里显示 --，不把 NaN 打进报告；判定仍是不合格）</summary>
        public string MeasuredValueText =>
            !_measuredValue.HasValue || double.IsNaN(_measuredValue.Value)
                ? "--"
                : $"{_measuredValue.Value:0.###} {Unit}";

        /// <summary>数值显示格式：压力类（mmHg）固定 2 位小数（如 59.99），其余量纲保持最多 3 位去尾零</summary>
        private string ValueFormat => Unit == "mmHg" ? "0.00" : "0.###";

        /// <summary>仪器测试值列显示文本（文本类结果如设备信息优先显示；无仪器侧值的项显示 --）</summary>
        public string InstrumentValueText =>
            _instrumentText ?? (_instrumentValue.HasValue ? $"{_instrumentValue.Value.ToString(ValueFormat)} {Unit}" : "--");

        /// <summary>产品上报值列显示文本（文本类结果如读回的版本/状态优先显示；无产品上报值的项显示 --）</summary>
        public string ProductValueText =>
            _productText ?? (_productValue.HasValue ? $"{_productValue.Value.ToString(ValueFormat)} {Unit}" : "--");

        // 入库快照用的原始值与原始文本（InstrumentValue 由 ApplyResult 回填）：显示仍走上面的 *Text；
        // 数值项的 RawText 为 null，文本项的数值为 null
        public double? InstrumentValue => _instrumentValue;

        public double? ProductValue => _productValue;

        public string? InstrumentRawText => _instrumentText;

        public string? ProductRawText => _productText;

        /// <summary>本项耗时（秒）：上一项上报（模块首项取派发时刻）到本项上报的墙钟，由 MainViewModel 在逐项回调里写入；null = 本轮没跑到这项</summary>
        public double? RunSeconds
        {
            get => _runSeconds;
            set
            {
                if (SetProperty(ref _runSeconds, value))
                    OnPropertyChanged(nameof(RunSecondsText));
            }
        }
        private double? _runSeconds;

        /// <summary>耗时列文本：秒数保留一位小数，没跑到显示 --</summary>
        public string RunSecondsText => _runSeconds is double s ? $"{s:0.0}" : "--";

        /// <summary>判定结果</summary>
        public TestResultStatus Result
        {
            get => _result;
            private set
            {
                if (SetProperty(ref _result, value))
                {
                    OnPropertyChanged(nameof(ResultText));
                }
            }
        }

        /// <summary>判定结果显示文本（只展示不判定项固定“—”，不看上下限）</summary>
        public string ResultText => _displayOnly ? "—" : _result switch
        {
            TestResultStatus.Pass => "合格",
            TestResultStatus.Fail => "不合格",
            _ => "未测试"
        };

        /// <summary>根据规格上下限判定测试值</summary>
        private void Judge()
        {
            if (!_measuredValue.HasValue)
            {
                Result = TestResultStatus.NotTested;
                return;
            }

            // 只展示不判定项（如 BP-005 平均值）：测试值与子表照常规入，但不进上下限比较
            if (_displayOnly)
            {
                Result = TestResultStatus.NotTested;
                return;
            }

            // NaN 当作“没测到值”：与上下限比较天生不成立，这里写明了是不合格，不靠浮点比较的巧合
            Result = !double.IsNaN(_measuredValue.Value) && _measuredValue.Value >= LowerLimit && _measuredValue.Value <= UpperLimit
                ? TestResultStatus.Pass
                : TestResultStatus.Fail;
        }

        /// <summary>本次运行是否上报了“演示不判定”结果：此类项保持未测试，模块收尾不强制 FAIL</summary>
        public bool IsReportedNotJudged => _reportedNotJudged;

        /// <summary>本次运行是否上报了“只展示不判定”结果：值与统计量可见，但不计入模块判定、收尾也不强制 FAIL</summary>
        public bool IsDisplayOnly => _displayOnly;

        /// <summary>回填 TestItem 执行结果：先写动态规格再写测试值（保证按本次规格判定），按测试项值来源（ValueSource）分发到仪器测试值/产品上报值两列</summary>
        public void ApplyResult(TestItemResult result)
        {
            _dynamicLower = result.SpecMin;
            _dynamicUpper = result.SpecMax;
            _instrumentText = result.InstrumentText;
            _productText = result.ProductText;
            OnPropertyChanged(nameof(LowerLimit));
            OnPropertyChanged(nameof(UpperLimit));
            OnPropertyChanged(nameof(IsDynamicSpec));

            // 演示不判定项：只显示演示文本，双值列清空并保持未测试，不参与合格判定
            if (result.NotJudged)
            {
                _reportedNotJudged = true;
                _instrumentValue = null;
                _productValue = null;
                OnPropertyChanged(nameof(InstrumentValueText));
                OnPropertyChanged(nameof(ProductValueText));
                PointResults.Clear();
                BuildSubTable(null);
                OnPropertyChanged(nameof(HasPointResults));
                OnPropertyChanged(nameof(HasRowDetails));
                OnPropertyChanged(nameof(PointSummaryText));
                MeasuredValue = null;
                return;
            }

            _reportedNotJudged = false;
            _displayOnly = result.DisplayOnly;
            switch (_model.ValueSource)
            {
                case "Product":
                    _instrumentValue = result.InstrumentValue;
                    _productValue = result.ProductValue ?? result.Value;
                    break;
                case "Both":
                    _instrumentValue = result.InstrumentValue;
                    _productValue = result.ProductValue ?? result.Value;
                    break;
                case "Error":
                    // 误差口径项（BP-003 一点一项）：主值=误差，仪器列=稳压值、产品列=上报值
                    _instrumentValue = result.InstrumentValue;
                    _productValue = result.ProductValue;
                    break;
                case "Points":
                    // 逐点判定项（BP-003）：父行双值列只显示显式回传值（通常为空，显示 --）
                    _instrumentValue = result.InstrumentValue;
                    _productValue = result.ProductValue;
                    break;
                default:
                    _instrumentValue = result.InstrumentValue ?? result.Value;
                    _productValue = result.ProductValue;
                    break;
            }
            OnPropertyChanged(nameof(InstrumentValueText));
            OnPropertyChanged(nameof(ProductValueText));

            // 逐点子节点（BP-003）：清空后重建，子表随父项结果同步刷新
            PointResults.Clear();
            if (result.Points != null)
            {
                foreach (var p in result.Points)
                    PointResults.Add(p);
            }
            BuildSubTable(result.Points);
            OnPropertyChanged(nameof(HasPointResults));
            OnPropertyChanged(nameof(HasRowDetails));
            OnPropertyChanged(nameof(PointSummaryText));

            MeasuredValue = result.Value;
            // 不判定项的 Result 一直是未测试，Setter 不触发通知，这里补上“—”的刷新
            OnPropertyChanged(nameof(ResultText));
        }

        /// <summary>口径→子表形状（行名列 / 各值列列头与像素宽 / 标题）</summary>
        private static (string RowHeader, string[] Headers, double[] Widths, string Title) ResolveSubTableShape(string schema, string unit)
            => schema switch
            {
                TestPointSchemas.Channel => ("通道",
                    new[] { "设定值", "回读值", "偏差" }, new[] { 92.0, 92.0, 82.0 }, "本轮各通道回读"),
                TestPointSchemas.Group => ("分组",
                    new[] { "设定值", "回读值", "偏差" }, new[] { 92.0, 92.0, 82.0 }, "各分组心率回读"),
                TestPointSchemas.Repeat => ("点位",
                    new[] { "设定值", "均值", "与设定偏差", "极大 / 极小", "极差" }, new[] { 82.0, 82.0, 92.0, 116.0, 82.0 }, "多轮统计（仅展示）"),
                TestPointSchemas.RepeatMatrix => ("项目",
                    new[] { "收缩压", "舒张压" }, new[] { 82.0, 82.0 }, "每轮回读与重复性统计"),
                TestPointSchemas.Event => ("阶段",
                    new[] { "时刻(s)", "压力(mmHg)", "说明" }, new[] { 76.0, 92.0, 208.0 }, "过压保护关键时刻（仅记录）"),
                TestPointSchemas.Slice => ("放气段",
                    new[] { "速率(mmHg/s)", "时长(s)", "压力区间" }, new[] { 100.0, 76.0, 92.0 }, "匀速段分段速率（仅展示）"),
                _ => ("测试点",
                    new[] { $"目标({unit})", $"仪器({unit})", $"产品({unit})", $"误差({unit})" }, new[] { 76.0, 88.0, 88.0, 76.0 }, "逐点结果"),
            };

        /// <summary>按口径取一行的单元格文本</summary>
        private static string[] CellsOf(TestPointResult p) => p.Cells switch
        {
            { Count: > 0 } cells => cells.ToArray(),
            _ => p.Schema switch
            {
                TestPointSchemas.Channel or TestPointSchemas.Group
                    => new[] { WithUnit(p.TargetText, p.Unit), WithUnit(p.ProductText, p.Unit), WithUnit(p.ErrorText, p.Unit) },
                TestPointSchemas.Repeat => new[] { p.TargetText, Num(p.Average), Num(p.Error), $"{Num(p.Max)} / {Num(p.Min)}", Num(p.Range) },
                TestPointSchemas.Event or TestPointSchemas.Slice
                    => new[] { p.TargetText, Num(p.Average), p.Remark },
                _ => new[] { p.TargetText, p.InstrumentText, p.ProductText, p.ErrorText },
            }
        };

        private static string WithUnit(string text, string unit) =>
            string.IsNullOrEmpty(unit) || text == "--" ? text : $"{text} {unit}";

        private static string Num(double? value) => value.HasValue ? value.Value.ToString("0.0") : "--";

        /// <summary>重建子表列头与数据行；统计中间行（ShowVerdict=false）末列留空，避免被当成 PASS</summary>
        private void BuildSubTable(List<TestPointResult>? points)
        {
            SubTableColumns.Clear();
            SubTableRows.Clear();
            PointTableTitle = string.Empty;
            if (points == null || points.Count == 0)
            {
                OnPropertyChanged(nameof(PointTableTitle));
                return;
            }

            // RepeatStats 数值统计行只供入库/报表图表读取，不在界面子表显示
            points = points.Where(p => p.Schema != TestPointSchemas.RepeatStats).ToList();
            if (points.Count == 0)
            {
                OnPropertyChanged(nameof(PointTableTitle));
                return;
            }

            // 列头单位跟着子表行的值走：时长类项目（A18 的 t1/t2）行里装的是压力，用大项单位会把 mmHg 标成 s
            var valueUnit = points.Select(p => p.Unit).FirstOrDefault(u => !string.IsNullOrEmpty(u)) ?? Unit;
            var (rowHeader, headers, widths, title) = ResolveSubTableShape(points[0].Schema, valueUnit);
            var cellLists = points.Select(CellsOf).ToArray();
            var columnCount = Math.Max(headers.Length, cellLists.Max(c => c.Length));
            const double rowHeaderWidth = 118;
            const double verdictWidth = 210;

            SubTableColumns.Add(new SubTableColumn { Header = rowHeader, Width = rowHeaderWidth, LeftAlign = true });
            for (var i = 0; i < columnCount; i++)
                SubTableColumns.Add(new SubTableColumn
                {
                    Header = i < headers.Length ? headers[i] : string.Empty,
                    Width = i < widths.Length ? widths[i] : 82,
                });
            SubTableColumns.Add(new SubTableColumn { Header = "判定", Width = verdictWidth, LeftAlign = true });

            for (var r = 0; r < points.Count; r++)
            {
                var p = points[r];
                var cells = cellLists[r];
                var isStatRow = p.Schema == TestPointSchemas.RepeatMatrix && p.ShowVerdict == false && r > 0 && !p.Name.StartsWith("第");
                var row = new List<SubTableCell>
                {
                    new() { Text = p.Name, Width = rowHeaderWidth, LeftAlign = true, Tone = isStatRow ? SubTableTone.Stat : SubTableTone.Main }
                };
                for (var i = 0; i < columnCount; i++)
                    row.Add(new SubTableCell
                    {
                        Text = i < cells.Length ? cells[i] : "--",
                        Width = i < widths.Length ? widths[i] : 82,
                        Tone = isStatRow ? SubTableTone.Stat : SubTableTone.Main,
                    });
                row.Add(new SubTableCell
                {
                    Text = p.ShowVerdict ? p.ResultDisplay : string.Empty,
                    Width = verdictWidth,
                    LeftAlign = true,
                    Tone = p.ShowVerdict ? (p.Pass ? SubTableTone.Pass : SubTableTone.Fail) : SubTableTone.Sub,
                });
                SubTableRows.Add(new SubTableRow { Cells = row });
            }

            PointTableTitle = title;
            OnPropertyChanged(nameof(PointTableTitle));
        }

        /// <summary>波形显示单位（由 Spec 目录逐项声明，不按项名判断；未声明时沿用规格单位）</summary>
        public string WaveformUnit => string.IsNullOrEmpty(_model.WaveformUnit) ? _model.Unit : _model.WaveformUnit;

        /// <summary>清除测试值、动态规格与判定结果</summary>
        public void Reset()
        {
            _dynamicLower = null;
            _dynamicUpper = null;
            _instrumentValue = null;
            _productValue = null;
            _instrumentText = null;
            _productText = null;
            _reportedNotJudged = false;
            _displayOnly = false;
            OnPropertyChanged(nameof(LowerLimit));
            OnPropertyChanged(nameof(UpperLimit));
            OnPropertyChanged(nameof(IsDynamicSpec));
            OnPropertyChanged(nameof(InstrumentValueText));
            OnPropertyChanged(nameof(ProductValueText));
            PointResults.Clear();
            BuildSubTable(null);
            OnPropertyChanged(nameof(HasPointResults));
            OnPropertyChanged(nameof(HasRowDetails));
            OnPropertyChanged(nameof(PointSummaryText));
            MeasuredValue = null;
            RunSeconds = null;
            OnPropertyChanged(nameof(ResultText));
        }

        /// <summary>执行端没报告该项：判定不合格，三个值列保持空（显示 --，入库 null），不把 0 当实测值写进记录</summary>
        public void MarkNotReportedAsFail()
        {
            _measuredValue = null;
            _instrumentText = null;
            _productText = null;
            _instrumentValue = null;
            _productValue = null;
            OnPropertyChanged(nameof(InstrumentValueText));
            OnPropertyChanged(nameof(ProductValueText));
            OnPropertyChanged(nameof(MeasuredValue));
            OnPropertyChanged(nameof(MeasuredValueText));
            Result = TestResultStatus.Fail;
        }

        /// <summary>按目录声明的配置来源解析横轴倍率；不吃配置或配置里没填时返回默认值</summary>
        private static double ResolveTimeScale(TestItemModel model)
        {
            if (model.WaveformConfigSource == WaveformConfigSource.None) return DefaultTimeScale;
            try
            {
                var cfg = App.Services.GetRequiredService<IInstrumentConfigService>().Load();
                var timeScale = model.WaveformConfigSource switch
                {
                    WaveformConfigSource.BloodPressureSimulator => cfg.ActiveBpTestParams.WaveformDisplay.GetItemConfig(model.Name).TimeScale,
                    WaveformConfigSource.Ecg100 => cfg.Aecg100Tester.GetWaveformConfig(model.Name).TimeScale,
                    _ => 0.0,
                };
                if (timeScale > 0) return timeScale;
            }
            catch { /* 配置文件缺失不影响界面加载 */ }
            return DefaultTimeScale;
        }
    }
}
