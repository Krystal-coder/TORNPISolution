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
        private TestResultStatus _result = TestResultStatus.NotTested;

        public TestItemViewModel(TestItemModel model)
        {
            _model = model;
            TimeScale = ResolveTimeScale(model.Name);
        }

        /// <summary>波形显示时间像素（像素/秒），按测试项从配置读取，默认 24</summary>
        public double TimeScale { get; }

        /// <summary>底层测试项定义（供 TestItem 执行测量时使用）</summary>
        public TestItemModel Model => _model;

        /// <summary>测试内容名称</summary>
        public string Name => _model.Name;

        /// <summary>测试内容说明</summary>
        public string Description => _model.Description;

        /// <summary>是否为带波形的测试项（血压压力曲线 / 仪器输出波形，行内显示波形折叠按钮）。
        /// 标志在 Spec 定义测试项时置位（TestItemModel.ShowWaveform），新增波形项无需改这里</summary>
        public bool HasWaveform => _model.ShowWaveform;

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

        /// <summary>是否有逐点子结果（控制行详情子表显示）</summary>
        public bool HasPointResults => PointResults.Count > 0;

        /// <summary>逐点结果摘要（子表标题栏显示）：共 X 点 Y PASS / Z FAIL，最大误差所在点，
        /// 父行只看结论、子表看证据时也能一眼拿到汇总</summary>
        public string PointSummaryText
        {
            get
            {
                if (PointResults.Count == 0) return string.Empty;
                var passCount = PointResults.Count(p => p.Pass);
                var text = $"共{PointResults.Count}点：{passCount} PASS / {PointResults.Count - passCount} FAIL";
                var worstErr = PointResults
                    .Where(p => p.Error.HasValue)
                    .OrderByDescending(p => p.Error!.Value)
                    .FirstOrDefault();
                if (worstErr != null)
                    text += $"，最大误差 {worstErr.Error!.Value:0.00} {Unit}（{worstErr.Name}）";
                return text;
            }
        }

        /// <summary>行详情是否有内容（逐点子表或波形任一展开即显示，避免空详情行）</summary>
        public bool HasRowDetails => HasPointResults || IsWaveformExpanded;

        /// <summary>该项已采集的波形采样点快照（MainViewModel 按项名分发，波形控件绑定绘制）</summary>
        public IReadOnlyList<PressureSamplePoint> WaveformSamples
        {
            get => _waveformSamples;
            set => SetProperty(ref _waveformSamples, value);
        }
        private IReadOnlyList<PressureSamplePoint> _waveformSamples = Array.Empty<PressureSamplePoint>();

        /// <summary>规格下限（TestItem 带回动态规格时优先用动态值，否则用 Spec 静态值）</summary>
        public double LowerLimit => _dynamicLower ?? _model.LowerLimit;

        /// <summary>规格上限（TestItem 带回动态规格时优先用动态值，否则用 Spec 静态值）</summary>
        public double UpperLimit => _dynamicUpper ?? _model.UpperLimit;

        /// <summary>规格是否为本次运行动态生成（仪器读回值现算）</summary>
        public bool IsDynamicSpec => _dynamicLower.HasValue || _dynamicUpper.HasValue;

        /// <summary>单位</summary>
        public string Unit => _model.Unit;

        /// <summary>规格显示文本：定义了字符串规格（如管控嵌软版本）时直接显示该文本；
        /// 否则通讯检查项显示判定规则，数值项显示范围（动态规格带 * 标识）</summary>
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

        /// <summary>测试值显示文本</summary>
        public string MeasuredValueText =>
            _measuredValue.HasValue ? $"{_measuredValue.Value:0.###} {Unit}" : "--";

        /// <summary>数值显示格式：压力类（mmHg）固定 2 位小数（如 59.99），其余量纲保持最多 3 位去尾零</summary>
        private string ValueFormat => Unit == "mmHg" ? "0.00" : "0.###";

        /// <summary>仪器测试值列显示文本（文本类结果如设备信息优先显示；无仪器侧值的项显示 --）</summary>
        public string InstrumentValueText =>
            _instrumentText ?? (_instrumentValue.HasValue ? $"{_instrumentValue.Value.ToString(ValueFormat)} {Unit}" : "--");

        /// <summary>产品上报值列显示文本（文本类结果如读回的版本/状态优先显示；无产品上报值的项显示 --）</summary>
        public string ProductValueText =>
            _productText ?? (_productValue.HasValue ? $"{_productValue.Value.ToString(ValueFormat)} {Unit}" : "--");

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

        /// <summary>判定结果显示文本</summary>
        public string ResultText => _result switch
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

            Result = _measuredValue.Value >= LowerLimit && _measuredValue.Value <= UpperLimit
                ? TestResultStatus.Pass
                : TestResultStatus.Fail;
        }

        /// <summary>本次运行是否上报了“演示不判定”结果：此类项保持未测试，模块收尾不强制 FAIL</summary>
        public bool IsReportedNotJudged => _reportedNotJudged;

        /// <summary>
        /// 回填 TestItem 执行结果：先写动态规格再写测试值（保证按本次规格判定）；
        /// 按测试项值来源（ValueSource）分发到仪器测试值/产品上报值两列
        /// </summary>
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
                OnPropertyChanged(nameof(HasPointResults));
                OnPropertyChanged(nameof(HasRowDetails));
                OnPropertyChanged(nameof(PointSummaryText));
                MeasuredValue = null;
                return;
            }

            _reportedNotJudged = false;
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
                case "Points":
                    // 逐点判定项（BP-003）：父行双值列只显示显式回传值（通常为空，显示 --），逐点双值在子表
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
            OnPropertyChanged(nameof(HasPointResults));
            OnPropertyChanged(nameof(HasRowDetails));
            OnPropertyChanged(nameof(PointSummaryText));

            MeasuredValue = result.Value;
        }

        /// <summary>波形显示单位（血压为 mmHg，ECG/PPG/SpO2 为 μV）</summary>
        public string WaveformUnit => ResolveWaveformUnit(Name);

        private static string ResolveWaveformUnit(string name)
        {
            if (name.StartsWith("ECG-") || name.StartsWith("PPG-") || name.StartsWith("SPO2-"))
                return "μV";
            return "mmHg";
        }

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
            OnPropertyChanged(nameof(LowerLimit));
            OnPropertyChanged(nameof(UpperLimit));
            OnPropertyChanged(nameof(IsDynamicSpec));
            OnPropertyChanged(nameof(InstrumentValueText));
            OnPropertyChanged(nameof(ProductValueText));
            PointResults.Clear();
            OnPropertyChanged(nameof(HasPointResults));
            OnPropertyChanged(nameof(HasRowDetails));
            OnPropertyChanged(nameof(PointSummaryText));
            MeasuredValue = null;
        }

        /// <summary>
        /// 模块未报告结果时强制判 FAIL（测试值保持 0 用于显示，避免落入规格区间被误判为 Pass）。
        /// </summary>
        public void MarkNotReportedAsFail()
        {
            _measuredValue = 0;
            _instrumentText = null;
            _productText = null;
            if (_model.ValueSource == "Product" || _model.ValueSource == "Both")
                _productValue = 0;
            else if (_model.ValueSource != "Points")   // Points 项（BP-003）双值列保持空显示 --
                _instrumentValue = 0;
            OnPropertyChanged(nameof(InstrumentValueText));
            OnPropertyChanged(nameof(ProductValueText));
            OnPropertyChanged(nameof(MeasuredValue));
            OnPropertyChanged(nameof(MeasuredValueText));
            Result = TestResultStatus.Fail;
        }

        /// <summary>从配置解析波形显示时间像素；未配置时返回 24</summary>
        private static double ResolveTimeScale(string name)
        {
            try
            {
                var cfg = App.Services.GetRequiredService<IInstrumentConfigService>().Load();
                if (name.StartsWith("ECG-"))
                {
                    var ecgCfg = cfg.Aecg100Tester.GetWaveformConfig(name);
                    if (ecgCfg.TimeScale > 0) return ecgCfg.TimeScale;
                }
                else if (name.StartsWith("BP-"))
                {
                    var bpaCfg = cfg.ActiveBpTestParams.WaveformDisplay.GetItemConfig(name);
                    if (bpaCfg.TimeScale > 0) return bpaCfg.TimeScale;
                }
            }
            catch { /* 配置文件缺失不影响界面加载 */ }
            return 24;
        }
    }
}
