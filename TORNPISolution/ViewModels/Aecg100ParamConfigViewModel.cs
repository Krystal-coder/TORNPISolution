using TORNPISolution.Common;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// ECG 参数配置 ViewModel：对应鲸扬 AECG100 综合性能测试仪的测试参数配置，
    /// 保存时写入 instruments.json 的 Aecg100Tester 节点。
    /// 独立配置页面（配置 → ECG参数配置），与血压仪参数配置页面同级。
    /// </summary>
    public class Aecg100ParamConfigViewModel : ViewModelBase
    {
        private readonly IInstrumentConfigService _configService;
        private readonly ILogService _log = new LogService(nameof(Aecg100ParamConfigViewModel));

        public Aecg100ParamConfigViewModel() : this(new InstrumentConfigService())
        {
        }

        public Aecg100ParamConfigViewModel(IInstrumentConfigService configService)
        {
            _configService = configService;
            Config = _configService.Load();
            SaveCommand = new RelayCommand(Save);
        }

        /// <summary>当前编辑中的配置（直接双向绑定其子属性）</summary>
        public InstrumentConnectionConfig Config { get; }

        /// <summary>AECG100 测试参数的快捷引用</summary>
        public Aecg100Settings Params => Config.Aecg100Tester;

        // ==================== 下拉选项 ====================

        /// <summary>ECG 波形类型选项（Key=显示名，Value=枚举值）</summary>
        public Dictionary<string, int> EcgWaveformTypes { get; } = new()
        {
            { "Sine", 0 }, { "Triangle", 1 }, { "Square", 2 },
            { "RectanglePulse", 3 }, { "TrianglePulse", 4 }, { "Exponential", 5 },
            { "ECG", 6 }
        };

        /// <summary>输出电极选项</summary>
        public Dictionary<string, int> ElectrodeOptions { get; } = new()
        {
            { "RA（右臂）", 0 }, { "LA（左臂）", 255 }
        };

        /// <summary>ECG 噪声频率选项</summary>
        public Dictionary<string, int> NoiseFrequencyOptions { get; } = new()
        {
            { "Off", 0 }, { "50Hz", 1 }, { "60Hz", 2 }, { "100Hz", 3 }, { "120Hz", 4 }
        };

        /// <summary>心率测试点文本（逗号分隔，双向绑定转换 List<int>）</summary>
        public string HeartRateTestPointsText
        {
            get => string.Join(",", Params.HeartRateTestPoints.Select(p => p.ToString()));
            set
            {
                var points = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                  .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
                                  .Where(n => n.HasValue)
                                  .Select(n => n!.Value)
                                  .ToList();
                if (points.Count > 0)
                    Params.HeartRateTestPoints = points;
                OnPropertyChanged(nameof(HeartRateTestPointsText));
            }
        }

        // ==================== 命令 ====================

        public RelayCommand SaveCommand { get; }

        /// <summary>保存成功后请求关闭对话框（由窗口订阅）</summary>
        public event Action? CloseRequested;

        private void Save()
        {
            _log.Info($"{DateTime.Now} Start calling the Aecg100ParamConfigViewModel.Save() method . {Environment.NewLine}");
            try
            {
                _configService.Save(Config);
                _log.Info("ECG 参数配置已保存（instruments.json → Aecg100Tester）");
                CloseRequested?.Invoke();
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in Aecg100ParamConfigViewModel.class Save method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the Aecg100ParamConfigViewModel.Save() method . {Environment.NewLine}");
            }
        }
    }
}
