using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using TORNPISolution.Common;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 血压仪参数配置 ViewModel：对应鲸杨 BPA700 官方上位机的三类测试参数
    /// （静态压力 / 动态血压 / 超压测试），保存时写入 instruments.json。
    /// 目前仅做参数配置与持久化，测试模块挂钩在后续按需接入。
    /// </summary>
    public class BpaParamConfigViewModel : ViewModelBase
    {
        private readonly IInstrumentConfigService _configService;
        private readonly ILogService _log = new LogService(nameof(BpaParamConfigViewModel));

       // 实例化
        public BpaParamConfigViewModel() : this(App.Services.GetRequiredService<IInstrumentConfigService>())
        {
        }

        public BpaParamConfigViewModel(IInstrumentConfigService configService)
        {
            _configService = configService;
            Config = _configService.Load();

            // 压力点列表包一层 ObservableCollection 以支持界面增删刷新，保存时写回配置（超出上限的旧数据截断）
            Points = new ObservableCollection<BpaPressurePoint>(Params.StaticPressure.Points.Take(MaxPointCount));

            // 血压默认值预设：列表随类型变化，初始选中按当前收缩/舒张压匹配
            BpDefaultPresets = new ObservableCollection<string>(CurrentCategoryPresets());
            _selectedBpPreset = ResolveInitialPreset();

            SaveCommand = new RelayCommand(Save);
            AddPointCommand = new RelayCommand(AddPoint, () => CanAddPoint);
            RemovePointCommand = new RelayCommand<BpaPressurePoint>(RemovePoint);
        }

        /// <summary>保存完成请求关闭窗口（由 Window code-behind 订阅）</summary>
        public event Action? CloseRequested;

        public InstrumentConnectionConfig Config { get; }

        /// <summary>BPA700 测试参数（充放气规格 / 静态压力 / 动态血压 / 超压）</summary>
        public BpaTestParams Params => Config.BPA700Simulator.TestParams;

        // ==================== 静态压力 ====================

        /// <summary>压力点数量上限（与官方上位机一致）</summary>
        public const int MaxPointCount = 10;

        /// <summary>静态压力点列表（界面可勾选/增删/改值）</summary>
        public ObservableCollection<BpaPressurePoint> Points { get; }

        /// <summary>还能添加压力点（达到上限后“添加压力点”按钮禁用）</summary>
        public bool CanAddPoint => Points.Count < MaxPointCount;

        /// <summary>压力点为空（列表区显示占位提示，避免表格区域塌成零高度）</summary>
        public bool PointsEmpty => Points.Count == 0;

        /// <summary>全选/全不选压力点</summary>
        public bool SelectAllPoints
        {
            get => Points.Count > 0 && Points.All(p => p.Enabled);
            set
            {
                foreach (var p in Points)
                {
                    p.Enabled = value;
                }

                // 手动刷新列表呈现（POCO 项不通知）
                OnPropertyChanged(nameof(Points));
            }
        }

        /// <summary>新增压力点（默认在上一个点基础上 +30，最多 <see cref="MaxPointCount"/> 个）</summary>
        private void AddPoint()
        {
            if (Points.Count >= MaxPointCount)
            {
                _log.Warn($"血压仪参数配置：压力点已达上限 {MaxPointCount} 个（与官方上位机一致），不能再添加");
                return;
            }

            var lastValue = Points.Count > 0 ? Points[^1].Value : 0;
            Points.Add(new BpaPressurePoint { Value = lastValue + 30 });
            OnPropertyChanged(nameof(SelectAllPoints));
            OnPropertyChanged(nameof(CanAddPoint));
            OnPropertyChanged(nameof(PointsEmpty));
        }

        /// <summary>删除指定压力点</summary>
        private void RemovePoint(BpaPressurePoint? point)
        {
            if (point != null)
            {
                Points.Remove(point);
                OnPropertyChanged(nameof(SelectAllPoints));
                OnPropertyChanged(nameof(CanAddPoint));
                OnPropertyChanged(nameof(PointsEmpty));
            }
        }

        // ==================== 动态血压 ====================

        public List<string> DynamicMethods { get; } = new() { "Default", "Customized" };

        public List<string> DynamicCategories { get; } = new() { "Adult", "Neonatal" };

        // 血压默认值预设（官方上位机动态血压页的下拉，格式“收缩压/舒张压(平均动脉压)”，列表随类型变化）
        private static readonly List<string> NeonatalPresets = new() { "35/15(22)", "60/30(40)", "80/50(60)", "100/65(77)", "120/80(93)", "150/100(117)" };
        private static readonly List<string> AdultPresets = new() { "60/30(40)", "80/50(60)", "100/65(77)", "120/80(93)", "150/100(117)", "200/150(167)", "255/195(215)" };

        /// <summary>血压默认值下拉项（随类型重建）</summary>
        public ObservableCollection<string> BpDefaultPresets { get; }

        private string? _selectedBpPreset;

        /// <summary>类型（桥接属性：切换时按新类型重建血压默认值下拉项）</summary>
        public string DynamicCategory
        {
            get => Params.DynamicBloodPressure.Category;
            set
            {
                if (Params.DynamicBloodPressure.Category == value) return;
                Params.DynamicBloodPressure.Category = value;
                OnPropertyChanged(nameof(DynamicCategory));
                RebuildPresets();
            }
        }

        /// <summary>当前选中的血压默认值（选中后自动填充收缩压/舒张压）</summary>
        public string SelectedBpPreset
        {
            get => _selectedBpPreset ?? string.Empty;
            set
            {
                if (_selectedBpPreset == value) return;
                _selectedBpPreset = value;
                OnPropertyChanged(nameof(SelectedBpPreset));
                if (value != null && TryParsePreset(value, out var sys, out var dia))
                {
                    Params.DynamicBloodPressure.Systolic = sys;
                    Params.DynamicBloodPressure.Diastolic = dia;
                    Params.DynamicBloodPressure.DefaultBpPreset = value;
                    // 模型为 POCO 无通知，整体刷新以 Params 为根的绑定（收缩压/舒张压输入框回显）
                    OnPropertyChanged(nameof(Params));
                }
            }
        }

        private List<string> CurrentCategoryPresets()
            => Params.DynamicBloodPressure.Category == "Neonatal" ? NeonatalPresets : AdultPresets;

        /// <summary>类型切换后重建预设列表：尽量保留当前选中项，否则退回按血压值匹配</summary>
        private void RebuildPresets()
        {
            var presets = CurrentCategoryPresets();
            BpDefaultPresets.Clear();
            foreach (var p in presets)
            {
                BpDefaultPresets.Add(p);
            }

            _selectedBpPreset = _selectedBpPreset != null && presets.Contains(_selectedBpPreset) ? _selectedBpPreset : ResolveInitialPreset();
            OnPropertyChanged(nameof(SelectedBpPreset));
        }

        /// <summary>初始/回退选中项：优先按当前收缩压/舒张压匹配预设，匹配不到取第一项（Default 方法血压以预设为准，同步填充）</summary>
        private string ResolveInitialPreset()
        {
            var presets = CurrentCategoryPresets();
            foreach (var p in presets)
            {
                if (TryParsePreset(p, out var sys, out var dia)
                    && sys == Params.DynamicBloodPressure.Systolic
                    && dia == Params.DynamicBloodPressure.Diastolic)
                {
                    return p;
                }
            }

            var fallback = presets[0];
            if (TryParsePreset(fallback, out var s, out var d))
            {
                Params.DynamicBloodPressure.Systolic = s;
                Params.DynamicBloodPressure.Diastolic = d;
                Params.DynamicBloodPressure.DefaultBpPreset = fallback;
            }

            return fallback;
        }

        /// <summary>解析预设字符串“120/80(93)”：取收缩压/舒张压，括号内为平均动脉压（仅展示）</summary>
        private static bool TryParsePreset(string preset, out double systolic, out double diastolic)
        {
            systolic = diastolic = 0;
            var slash = preset.IndexOf('/');
            var paren = slash > 0 ? preset.IndexOf('(', slash + 1) : -1;
            if (slash <= 0 || paren <= slash) return false;
            return double.TryParse(preset[..slash], out systolic)
                && double.TryParse(preset[(slash + 1)..paren], out diastolic);
        }

        /// <summary>动态血压方法（桥接属性：切换时联动 Default/Customized 参数区的可用性）</summary>
        public string DynamicMethod
        {
            get => Params.DynamicBloodPressure.Method;
            set
            {
                Params.DynamicBloodPressure.Method = value;
                OnPropertyChanged(nameof(DynamicMethod));
                OnPropertyChanged(nameof(IsDefaultMethod));
                OnPropertyChanged(nameof(IsCustomizedMethod));
            }
        }

        /// <summary>Default 方法参数区可用（选 Customized 时血压默认六参数不可选，与官方上位机一致）</summary>
        public bool IsDefaultMethod => DynamicMethod == "Default";

        /// <summary>Customized 包络参数区可用</summary>
        public bool IsCustomizedMethod => DynamicMethod == "Customized";

        // ==================== 保存 ====================

        public RelayCommand SaveCommand { get; }
        public RelayCommand AddPointCommand { get; }
        public RelayCommand<BpaPressurePoint> RemovePointCommand { get; }

        // ==================== 波形显示配置 ====================

        /// <summary>X 轴模式下拉项</summary>
        public List<string> XAxisModes { get; } = new() { "Time", "SampleIndex" };

        /// <summary>带波形的血压测试项名称列表（与 Spec 中 showWaveform:true 的 BP- 项一致）</summary>
        public List<string> WaveformItemNames { get; } = new()
        {
            "BP-001充气时间",
            "BP-002放气时间",
            "BP-003静态压力精度测试",
            "BP-004收缩压精度测试",
            "BP-006过压保护触发压力",
            "BP-007快速放气速率",
            "BP-008放气时间",
            "BP-009过压保护响应"
        };

        private string? _selectedWaveformItem;

        /// <summary>当前选中的波形测试项</summary>
        public string SelectedWaveformItem
        {
            get => _selectedWaveformItem ?? WaveformItemNames[0];
            set
            {
                if (_selectedWaveformItem == value) return;
                _selectedWaveformItem = value;
                OnPropertyChanged(nameof(SelectedWaveformItem));
                OnPropertyChanged(nameof(SelectedXAxisMode));
                OnPropertyChanged(nameof(SelectedTimeScale));
                OnPropertyChanged(nameof(IsTimeXAxis));
            }
        }

        /// <summary>选中项的 X 轴模式</summary>
        public string SelectedXAxisMode
        {
            get => Params.WaveformDisplay.GetItemConfig(SelectedWaveformItem).XAxisMode;
            set
            {
                var cfg = GetOrCreateWaveformConfig(SelectedWaveformItem);
                if (cfg.XAxisMode == value) return;
                cfg.XAxisMode = value;
                OnPropertyChanged(nameof(SelectedXAxisMode));
                OnPropertyChanged(nameof(IsTimeXAxis));
            }
        }

        /// <summary>选中项的时间刻度（像素/秒）</summary>
        public double SelectedTimeScale
        {
            get => Params.WaveformDisplay.GetItemConfig(SelectedWaveformItem).TimeScale;
            set
            {
                var cfg = GetOrCreateWaveformConfig(SelectedWaveformItem);
                if (Math.Abs(cfg.TimeScale - value) < 0.01) return;
                cfg.TimeScale = value;
                OnPropertyChanged(nameof(SelectedTimeScale));
            }
        }

        /// <summary>X 轴模式是否为 Time（控制时间刻度输入框可用性）</summary>
        public bool IsTimeXAxis => SelectedXAxisMode == "Time";

        private BpaWaveformItemConfig GetOrCreateWaveformConfig(string itemName)
        {
            if (!Params.WaveformDisplay.ItemConfigs.TryGetValue(itemName, out var cfg))
            {
                cfg = new BpaWaveformItemConfig();
                Params.WaveformDisplay.ItemConfigs[itemName] = cfg;
            }
            return cfg;
        }

        /// <summary>保存：压力点列表写回配置后整体落盘 instruments.json</summary>
        private void Save()
        {
            _log.Info($"{DateTime.Now} Start calling the BpaParamConfigViewModel.Save() method . {Environment.NewLine}");
            try
            {
                Params.StaticPressure.Points = Points.ToList();

                // 至少保留一个勾选的压力点，避免静态压力测试无点可跑
                if (Params.StaticPressure.Points.All(p => !p.Enabled))
                {
                    _log.Warn("血压仪参数配置：静态压力未勾选任何压力点，已自动勾选第一个");
                    if (Params.StaticPressure.Points.Count > 0)
                    {
                        Params.StaticPressure.Points[0].Enabled = true;
                    }
                }

                _configService.Save(Config);
                _log.Info("血压仪参数配置已保存（instruments.json → BPA700Simulator.TestParams）");
                CloseRequested?.Invoke();
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in BpaParamConfigViewModel.class Save method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the BpaParamConfigViewModel.Save() method . {Environment.NewLine}");
            }
        }
    }
}
