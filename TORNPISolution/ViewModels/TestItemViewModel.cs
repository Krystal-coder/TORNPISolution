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
            set => SetProperty(ref _isWaveformExpanded, value);
        }
        private bool _isWaveformExpanded;

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

        /// <summary>规格显示文本，如 "3.20 ~ 3.40 V"（动态规格带 * 标识）</summary>
        public string SpecText => IsDynamicSpec
            ? $"{LowerLimit:0.###} ~ {UpperLimit:0.###} {Unit} *"
            : $"{LowerLimit:0.###} ~ {UpperLimit:0.###} {Unit}";

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

        /// <summary>仪器测试值列显示文本（文本类结果如设备信息优先显示；无仪器侧值的项显示 --）</summary>
        public string InstrumentValueText =>
            _instrumentText ?? (_instrumentValue.HasValue ? $"{_instrumentValue.Value:0.###} {Unit}" : "--");

        /// <summary>产品上报值列显示文本（无产品上报值的项显示 --）</summary>
        public string ProductValueText =>
            _productValue.HasValue ? $"{_productValue.Value:0.###} {Unit}" : "--";

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

        /// <summary>
        /// 回填 TestItem 执行结果：先写动态规格再写测试值（保证按本次规格判定）；
        /// 按测试项值来源（ValueSource）分发到仪器测试值/产品上报值两列
        /// </summary>
        public void ApplyResult(TestItemResult result)
        {
            _dynamicLower = result.SpecMin;
            _dynamicUpper = result.SpecMax;
            _instrumentText = result.InstrumentText;
            OnPropertyChanged(nameof(LowerLimit));
            OnPropertyChanged(nameof(UpperLimit));
            OnPropertyChanged(nameof(IsDynamicSpec));
            OnPropertyChanged(nameof(SpecText));

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
                default:
                    _instrumentValue = result.InstrumentValue ?? result.Value;
                    _productValue = result.ProductValue;
                    break;
            }
            OnPropertyChanged(nameof(InstrumentValueText));
            OnPropertyChanged(nameof(ProductValueText));

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
            OnPropertyChanged(nameof(LowerLimit));
            OnPropertyChanged(nameof(UpperLimit));
            OnPropertyChanged(nameof(IsDynamicSpec));
            OnPropertyChanged(nameof(SpecText));
            OnPropertyChanged(nameof(InstrumentValueText));
            OnPropertyChanged(nameof(ProductValueText));
            MeasuredValue = null;
        }

        /// <summary>
        /// 模块未报告结果时强制判 FAIL（测试值保持 0 用于显示，避免落入规格区间被误判为 Pass）。
        /// </summary>
        public void MarkNotReportedAsFail()
        {
            _measuredValue = 0;
            _instrumentText = null;
            if (_model.ValueSource == "Product" || _model.ValueSource == "Both")
                _productValue = 0;
            else
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
                    var bpaCfg = cfg.BPA700Simulator.TestParams.WaveformDisplay.GetItemConfig(name);
                    if (bpaCfg.TimeScale > 0) return bpaCfg.TimeScale;
                }
            }
            catch { /* 配置文件缺失不影响界面加载 */ }
            return 24;
        }
    }
}
