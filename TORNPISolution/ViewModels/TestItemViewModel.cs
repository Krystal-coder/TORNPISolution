using TORNPISolution.Common;
using TORNPISolution.Share.Models;

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
        private TestResultStatus _result = TestResultStatus.NotTested;

        public TestItemViewModel(TestItemModel model)
        {
            _model = model;
        }

        /// <summary>底层测试项定义（供 TestItem 执行测量时使用）</summary>
        public TestItemModel Model => _model;

        /// <summary>测试内容名称</summary>
        public string Name => _model.Name;

        /// <summary>测试内容说明</summary>
        public string Description => _model.Description;

        /// <summary>是否为带波形的测试项（血压压力曲线 / ECG 输出波形，行内显示波形折叠按钮）</summary>
        public bool HasWaveform => Name is "BP-001物理按键启动测量" or "BP-002测量中手动停止"
            or "BP-003静态压力精度测试" or "BP-009过压保护"
            or "ECG-001测量启动" or "ECG-002导联脱落检测"
            or "ECG-009动态范围" or "ECG-010输入阻抗" or "ECG-011增益准确性" or "ECG-012频率响应";

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

        /// <summary>回填 TestItem 执行结果：先写动态规格再写测试值（保证按本次规格判定）</summary>
        public void ApplyResult(TestItemResult result)
        {
            _dynamicLower = result.SpecMin;
            _dynamicUpper = result.SpecMax;
            OnPropertyChanged(nameof(LowerLimit));
            OnPropertyChanged(nameof(UpperLimit));
            OnPropertyChanged(nameof(IsDynamicSpec));
            OnPropertyChanged(nameof(SpecText));
            MeasuredValue = result.Value;
        }

        /// <summary>清除测试值、动态规格与判定结果</summary>
        public void Reset()
        {
            _dynamicLower = null;
            _dynamicUpper = null;
            OnPropertyChanged(nameof(LowerLimit));
            OnPropertyChanged(nameof(UpperLimit));
            OnPropertyChanged(nameof(IsDynamicSpec));
            OnPropertyChanged(nameof(SpecText));
            MeasuredValue = null;
        }
    }
}
