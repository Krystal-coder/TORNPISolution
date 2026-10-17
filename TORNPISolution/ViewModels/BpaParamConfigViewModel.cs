using System.Collections.ObjectModel;
using TORNPISolution.Common;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 血压模拟仪（BPA700 / 康伟达）测试配置 ViewModel
    /// 保存时写入 instruments.json 
    /// </summary>
    public class BpaParamConfigViewModel : ViewModelBase
    {
        private readonly IInstrumentConfigService _configService;
        private readonly ILogService _log = new LogService(nameof(BpaParamConfigViewModel));

        public BpaParamConfigViewModel(IInstrumentConfigService configService)
        {
            _configService = configService;
            Config = _configService.Load();

            // 默认页签跟随仪器链接配置当前启用的模拟仪
            _selectedDeviceIndex = Config.ActiveSimulator == SimulatorType.Kangwenda ? 1 : 0;

            // 压力点列表包一层 ObservableCollection 以支持界面增删刷新，保存时写回配置
            Points = new ObservableCollection<BpaPressurePoint>(Params.StaticPressure.Points.Take(MaxPointCount));

            // BP-004 压力组 / BP-005 心率点列表同样包一层，保存时写回（两个模块各一份配置）
            BpAccuracyGroups = new ObservableCollection<BpaBpPressureGroup>(Params.BpAccuracy.Groups.Take(MaxPointCount));
            BpHeartRatePoints = new ObservableCollection<BpaHeartRatePoint>(Params.BpHeartRate.HeartRates.Take(MaxPointCount));

            // 血压默认值预设：列表随类型变化，初始选中按当前收缩/舒张压匹配
            BpDefaultPresets = new ObservableCollection<string>(CurrentCategoryPresets());
            _selectedBpPreset = ResolveInitialPreset();

            SaveCommand = new RelayCommand(Save, () => !TestRunState.IsRunning);
            AddPointCommand = new RelayCommand(AddPoint, () => CanAddPoint);
            RemovePointCommand = new RelayCommand<BpaPressurePoint>(RemovePoint);
            AddBpAccuracyGroupCommand = new RelayCommand(AddBpAccuracyGroup, () => CanAddBpAccuracyGroup);
            RemoveBpAccuracyGroupCommand = new RelayCommand<BpaBpPressureGroup>(RemoveBpAccuracyGroup);
            AddBpHeartRatePointCommand = new RelayCommand(AddBpHeartRatePoint, () => CanAddBpHeartRatePoint);
            RemoveBpHeartRatePointCommand = new RelayCommand<BpaHeartRatePoint>(RemoveBpHeartRatePoint);
            CopyPointsFromActiveCommand = new RelayCommand(CopyPointsFromActive);
        }

        /// <summary>保存完成请求关闭窗口</summary>
        public event Action? CloseRequested;

        public InstrumentConnectionConfig Config { get; }

        // ==================== 设备页签（BPA700 / 康伟达） ====================

        private int _selectedDeviceIndex;

        /// <summary>设备页签：0 = BPA700（鲸杨），1 = 康伟达</summary>
        public int SelectedDeviceIndex
        {
            get => _selectedDeviceIndex;
            set
            {
                if (_selectedDeviceIndex == value) return;
                 
                Params.StaticPressure.Points = Points.ToList();
                Params.BpAccuracy.Groups = BpAccuracyGroups.ToList();
                Params.BpHeartRate.HeartRates = BpHeartRatePoints.ToList();
                _selectedDeviceIndex = value;

                Points.Clear();
                foreach (var p in Params.StaticPressure.Points.Take(MaxPointCount))
                    Points.Add(p);
                BpAccuracyGroups.Clear();
                foreach (var p in Params.BpAccuracy.Groups.Take(MaxPointCount))
                    BpAccuracyGroups.Add(p);
                BpHeartRatePoints.Clear();
                foreach (var p in Params.BpHeartRate.HeartRates.Take(MaxPointCount))
                    BpHeartRatePoints.Add(p);
                RebuildPresets();

                OnPropertyChanged(nameof(SelectedDeviceIndex));
                OnPropertyChanged(nameof(IsKangwenda));
                OnPropertyChanged(nameof(Params));
                OnPropertyChanged(nameof(CurrentDeviceName));
                OnPropertyChanged(nameof(IsViewingActiveDevice));
                OnPropertyChanged(nameof(InactiveDeviceHint));
                OnPropertyChanged(nameof(CanCopyPointsFromActive));
                OnPropertyChanged(nameof(SelfInflationLabel));
                OnPropertyChanged(nameof(SelectAllPoints));
                OnPropertyChanged(nameof(CanAddPoint));
                OnPropertyChanged(nameof(PointsEmpty));
                OnPropertyChanged(nameof(SelectAllBpAccuracyGroups));
                OnPropertyChanged(nameof(CanAddBpAccuracyGroup));
                OnPropertyChanged(nameof(BpAccuracyGroupsEmpty));
                OnPropertyChanged(nameof(BpAccuracyRepeats));
                OnPropertyChanged(nameof(BpAccuracyHeartRate));
                OnPropertyChanged(nameof(BpAccuracyRestSeconds));
                OnPropertyChanged(nameof(BpAccuracyVentDuringRest));
                OnPropertyChanged(nameof(SelectAllBpHeartRatePoints));
                OnPropertyChanged(nameof(CanAddBpHeartRatePoint));
                OnPropertyChanged(nameof(BpHeartRatePointsEmpty));
                OnPropertyChanged(nameof(BpHeartRateRepeats));
                OnPropertyChanged(nameof(BpHeartRateRestSeconds));
                OnPropertyChanged(nameof(BpHeartRateVentDuringRest));
                OnPropertyChanged(nameof(StaticVentDuringRest));
                OnPropertyChanged(nameof(StaticVentHold));
                OnPropertyChanged(nameof(DynamicCategory));
                OnPropertyChanged(nameof(DynamicMethod));
                OnPropertyChanged(nameof(IsDefaultMethod));
                OnPropertyChanged(nameof(IsCustomizedMethod));
                OnPropertyChanged(nameof(SelectedXAxisMode));
                OnPropertyChanged(nameof(SelectedTimeScale));
                OnPropertyChanged(nameof(IsTimeXAxis));
            }
        }

        /// <summary>当前页签设备名（动态文案用）</summary>
        public string CurrentDeviceName => _selectedDeviceIndex == 1 ? "康伟达" : "BPA700";

        /// <summary>
        /// 当前页签是否康伟达（两设备页签共用参数编辑器模板，康伟达无效配置项按此隐藏：
        /// 充气方式/量测时间/方法/类型/包络偏移等均属 BPA700 SDK 
        /// </summary>
        public bool IsKangwenda => _selectedDeviceIndex == 1;

        /// <summary>充气方式开关文案（随页签设备变化）</summary>
        public string SelfInflationLabel => $"{CurrentDeviceName} 自行充气";

        // ==================== 当前生效设备（真正参与测试） ====================

        /// <summary>
        /// 生效设备名：与 <see cref="InstrumentConnectionConfig.ActiveBpTestParams"/> 同源，
        /// 测试模块（A03/A04/A05/A06 等）只读这一台设备的 TestParams，另一份是换仪器时的备用配置。
        /// </summary>
        public string ActiveDeviceName
            => Config.ActiveSimulator switch
            {
                SimulatorType.Kangwenda => "康伟达",
                SimulatorType.BPA700 => "BPA700",
                _ => "（未选择）"
            };

        /// <summary>当前页签是否就是生效那台（未选类型时两台都不生效）</summary>
        public bool IsViewingActiveDevice
            => Config.ActiveSimulator switch
            {
                SimulatorType.Kangwenda => _selectedDeviceIndex == 1,
                SimulatorType.BPA700 => _selectedDeviceIndex == 0,
                _ => false
            };

        /// <summary>外层页签标题：BPA700 那页，生效时带“· 当前生效”标记</summary>
        public string Bpa700TabHeader
            => Config.ActiveSimulator == SimulatorType.BPA700 ? "BPA700（鲸杨）· 当前生效" : "BPA700（鲸杨）";

        /// <summary>外层页签标题：康伟达那页，生效时带“· 当前生效”标记</summary>
        public string KangwendaTabHeader
            => Config.ActiveSimulator == SimulatorType.Kangwenda ? "康伟达· 当前生效" : "康伟达";

        /// <summary>看的是非生效页签时的页顶提示（说清“改这里不会生效”，避免误以为两套点位都在跑）</summary>
        public string InactiveDeviceHint
            => Config.ActiveSimulator == SimulatorType.None
                ? "仪器链接配置里还没选「模拟仪类型」，两台设备的参数都不会参与测试，血压相关模块会直接报错跳过。"
                : $"当前生效的是 {ActiveDeviceName}，本页（{CurrentDeviceName}）的参数不参与测试；"
                  + "要换仪器请先到 配置 → 仪器链接 改「模拟仪类型」。";

        /// <summary>能复制生效设备的点位过来（只在看着非生效页签时才需要）</summary>
        public bool CanCopyPointsFromActive => CanEdit && !IsViewingActiveDevice;

        /// <summary>当前页签设备的测试参数（充放气规格 / 静态压力 / 动态血压 / 超压 / 压力控制 / 波形显示，两台设备独立存放）</summary>
        public BpaTestParams Params
            => _selectedDeviceIndex == 1 ? Config.KangwendaSimulator.TestParams : Config.BPA700Simulator.TestParams;

        // ==================== 运行期锁定 ====================

        /// <summary>测试方案是否正在运行</summary>
        public bool IsRunning => TestRunState.IsRunning;

        /// <summary>参数编辑区可用（运行中锁定，禁止修改）</summary>
        public bool CanEdit => !IsRunning;

        /// <summary>保存按钮可用（运行中灰色不可选）</summary>
        public bool CanSave => !IsRunning;

        // ==================== 静态压力 ====================

        /// <summary>压力点数量上限</summary>
        public const int MaxPointCount = 10;

        /// <summary>静态压力点列表（界面可勾选/增删/改值）</summary>
        public ObservableCollection<BpaPressurePoint> Points { get; }

        /// <summary>还能添加压力点（达到上限后“添加压力点”按钮禁用）</summary>
        public bool CanAddPoint => Points.Count < MaxPointCount;

        /// <summary>压力点为空</summary>
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

                OnPropertyChanged(nameof(Points));
            }
        }

        /// <summary>新增压力点（默认在上一个点基础上 +30，最多 <see cref="MaxPointCount"/> 个）</summary>
        private void AddPoint()
        {
            if (Points.Count >= MaxPointCount)
            {
                _log.Warn($"血压仪参数配置：压力点已达上限 {MaxPointCount} 个，不能再添加");
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

        /// <summary>静态压力点间泄压（RadioButton 一组：泄压 / 不泄）</summary>
        public bool StaticVentDuringRest
        {
            get => Params.StaticPressure.VentBetweenPoints;
            set
            {
                Params.StaticPressure.VentBetweenPoints = value;
                OnPropertyChanged(nameof(StaticVentDuringRest));
                OnPropertyChanged(nameof(StaticVentHold));
            }
        }

        /// <summary>静态压力点间不泄（与 <see cref="StaticVentDuringRest"/> 互斥取反）</summary>
        public bool StaticVentHold
        {
            get => !Params.StaticPressure.VentBetweenPoints;
            set
            {
                Params.StaticPressure.VentBetweenPoints = !value;
                OnPropertyChanged(nameof(StaticVentDuringRest));
                OnPropertyChanged(nameof(StaticVentHold));
            }
        }

        // ==================== BP-004 血压精度/重复性（A06） ====================

        /// <summary>BP-004 压力组列表（勾选的行参与测试，顺序即执行顺序）</summary>
        public ObservableCollection<BpaBpPressureGroup> BpAccuracyGroups { get; }

        /// <summary>还能添加压力组（达上限后按钮禁用）</summary>
        public bool CanAddBpAccuracyGroup => BpAccuracyGroups.Count < MaxPointCount;

        /// <summary>压力组为空（列表区显示占位提示）</summary>
        public bool BpAccuracyGroupsEmpty => BpAccuracyGroups.Count == 0;

        /// <summary>全选/全不选压力组</summary>
        public bool SelectAllBpAccuracyGroups
        {
            get => BpAccuracyGroups.Count > 0 && BpAccuracyGroups.All(p => p.Enabled);
            set
            {
                foreach (var p in BpAccuracyGroups)
                {
                    p.Enabled = value;
                }

                OnPropertyChanged(nameof(BpAccuracyGroups));
            }
        }

        /// <summary>BP-004 心率（全模块一个值，发给模拟仪驱动脉搏波）</summary>
        public int BpAccuracyHeartRate
        {
            get => Params.BpAccuracy.HeartRate;
            set
            {
                Params.BpAccuracy.HeartRate = value;
                OnPropertyChanged();
            }
        }

        /// <summary>BP-004 每压力组轮数（少于 3 抬到 3，执行端取极差至少三轮）</summary>
        public int BpAccuracyRepeats
        {
            get => Params.BpAccuracy.Repeats;
            set
            {
                Params.BpAccuracy.Repeats = value < 3 ? 3 : value;
               
                OnPropertyChanged();
            }
        }

        /// <summary>BP-004 轮间休息秒数（0 = 不休息）</summary>
        public int BpAccuracyRestSeconds
        {
            get => Params.BpAccuracy.RestSeconds;
            set
            {
                Params.BpAccuracy.RestSeconds = value < 0 ? 0 : value;
                OnPropertyChanged();
            }
        }

        /// <summary>BP-004 休息时泄压（RadioButton 一组：泄压 / 不泄）</summary>
        public bool BpAccuracyVentDuringRest
        {
            get => Params.BpAccuracy.VentDuringRest;
            set
            {
                Params.BpAccuracy.VentDuringRest = value;
                OnPropertyChanged(nameof(BpAccuracyVentDuringRest));
                OnPropertyChanged(nameof(BpAccuracyVentHold));
            }
        }

        /// <summary>BP-004 休息时不泄（与 <see cref="BpAccuracyVentDuringRest"/> 互斥取反）</summary>
        public bool BpAccuracyVentHold
        {
            get => !Params.BpAccuracy.VentDuringRest;
            set
            {
                Params.BpAccuracy.VentDuringRest = !value;
                OnPropertyChanged(nameof(BpAccuracyVentDuringRest));
                OnPropertyChanged(nameof(BpAccuracyVentHold));
            }
        }

        /// <summary>新增 BP-004 压力组（沿用上一行的血压，方便连续加相近点位）</summary>
        private void AddBpAccuracyGroup()
        {
            if (BpAccuracyGroups.Count >= MaxPointCount)
            {
                _log.Warn($"血压仪参数配置：BP-004 压力组已达上限 {MaxPointCount} 组，不能再添加");
                return;
            }

            var last = BpAccuracyGroups.Count > 0 ? BpAccuracyGroups[^1] : new BpaBpPressureGroup();
            BpAccuracyGroups.Add(new BpaBpPressureGroup { Systolic = last.Systolic, Diastolic = last.Diastolic });
            OnPropertyChanged(nameof(SelectAllBpAccuracyGroups));
            OnPropertyChanged(nameof(CanAddBpAccuracyGroup));
            OnPropertyChanged(nameof(BpAccuracyGroupsEmpty));
        }

        /// <summary>删除指定压力组</summary>
        private void RemoveBpAccuracyGroup(BpaBpPressureGroup? group)
        {
            if (group != null)
            {
                BpAccuracyGroups.Remove(group);
                OnPropertyChanged(nameof(SelectAllBpAccuracyGroups));
                OnPropertyChanged(nameof(CanAddBpAccuracyGroup));
                OnPropertyChanged(nameof(BpAccuracyGroupsEmpty));
            }
        }

        // ==================== BP-005 心率精度/重复性（A21） ====================

        /// <summary>BP-005 心率点列表（压力只用一组，心率按勾选的点逐点轮测）</summary>
        public ObservableCollection<BpaHeartRatePoint> BpHeartRatePoints { get; }

        /// <summary>还能添加心率点（达上限后按钮禁用）</summary>
        public bool CanAddBpHeartRatePoint => BpHeartRatePoints.Count < MaxPointCount;

        /// <summary>心率点为空（列表区显示占位提示）</summary>
        public bool BpHeartRatePointsEmpty => BpHeartRatePoints.Count == 0;

        /// <summary>全选/全不选心率点</summary>
        public bool SelectAllBpHeartRatePoints
        {
            get => BpHeartRatePoints.Count > 0 && BpHeartRatePoints.All(p => p.Enabled);
            set
            {
                foreach (var p in BpHeartRatePoints)
                {
                    p.Enabled = value;
                }

                OnPropertyChanged(nameof(BpHeartRatePoints));
            }
        }

        /// <summary>BP-005 收缩压（mmHg，血压只用这一组）</summary>
        public double BpHeartRateSystolic
        {
            get => Params.BpHeartRate.Systolic;
            set
            {
                Params.BpHeartRate.Systolic = value;
                OnPropertyChanged();
            }
        }

        /// <summary>BP-005 舒张压（mmHg）</summary>
        public double BpHeartRateDiastolic
        {
            get => Params.BpHeartRate.Diastolic;
            set
            {
                Params.BpHeartRate.Diastolic = value;
                OnPropertyChanged();
            }
        }

        /// <summary>BP-005 每心率点轮数（少于 3 抬到 3）</summary>
        public int BpHeartRateRepeats
        {
            get => Params.BpHeartRate.Repeats;
            set
            {
                Params.BpHeartRate.Repeats = value < 3 ? 3 : value;
                OnPropertyChanged();
            }
        }

        /// <summary>BP-005 轮间休息秒数（0 = 不休息）</summary>
        public int BpHeartRateRestSeconds
        {
            get => Params.BpHeartRate.RestSeconds;
            set
            {
                Params.BpHeartRate.RestSeconds = value < 0 ? 0 : value;
                OnPropertyChanged();
            }
        }

        /// <summary>BP-005 休息时泄压（RadioButton 一组：泄压 / 不泄）</summary>
        public bool BpHeartRateVentDuringRest
        {
            get => Params.BpHeartRate.VentDuringRest;
            set
            {
                Params.BpHeartRate.VentDuringRest = value;
                OnPropertyChanged(nameof(BpHeartRateVentDuringRest));
                OnPropertyChanged(nameof(BpHeartRateVentHold));
            }
        }

        /// <summary>BP-005 休息时不泄（与 <see cref="BpHeartRateVentDuringRest"/> 互斥取反）</summary>
        public bool BpHeartRateVentHold
        {
            get => !Params.BpHeartRate.VentDuringRest;
            set
            {
                Params.BpHeartRate.VentDuringRest = !value;
                OnPropertyChanged(nameof(BpHeartRateVentDuringRest));
                OnPropertyChanged(nameof(BpHeartRateVentHold));
            }
        }

        /// <summary>新增心率点</summary>
        private void AddBpHeartRatePoint()
        {
            if (BpHeartRatePoints.Count >= MaxPointCount)
            {
                _log.Warn($"血压仪参数配置：BP-005 心率点已达上限 {MaxPointCount} 个，不能再添加");
                return;
            }

            var last = BpHeartRatePoints.Count > 0 ? BpHeartRatePoints[^1] : new BpaHeartRatePoint();
            BpHeartRatePoints.Add(new BpaHeartRatePoint { Value = last.Value });
            OnPropertyChanged(nameof(SelectAllBpHeartRatePoints));
            OnPropertyChanged(nameof(CanAddBpHeartRatePoint));
            OnPropertyChanged(nameof(BpHeartRatePointsEmpty));
        }

        /// <summary>删除指定心率点</summary>
        private void RemoveBpHeartRatePoint(BpaHeartRatePoint? point)
        {
            if (point != null)
            {
                BpHeartRatePoints.Remove(point);
                OnPropertyChanged(nameof(SelectAllBpHeartRatePoints));
                OnPropertyChanged(nameof(CanAddBpHeartRatePoint));
                OnPropertyChanged(nameof(BpHeartRatePointsEmpty));
            }
        }

        public List<string> DynamicMethods { get; } = new() { "Default", "Customized" };

        public List<string> DynamicCategories { get; } = new() { "Adult", "Neonatal" };

        // 血压默认值预设
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

        /// <summary>初始/回退选中项：优先用当前设备已保存的预设，其次按收缩压/舒张压匹配，
        /// 都匹配不到才取第一项（Default 方法血压以预设为准，同步填充；切换设备页签时避免误改已保存参数）</summary>
        private string ResolveInitialPreset()
        {
            var presets = CurrentCategoryPresets();
            var saved = Params.DynamicBloodPressure.DefaultBpPreset;
            if (!string.IsNullOrEmpty(saved) && presets.Contains(saved))
                return saved;

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

        /// <summary>解析预设字符串“120/80(93)”：取收缩压/舒张压，括号内为平均动脉压</summary>
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

        /// <summary>Default 方法参数区可用（选 Customized 时血压默认六参数不可选）</summary>
        public bool IsDefaultMethod => DynamicMethod == "Default";

        /// <summary>Customized 包络参数区可用</summary>
        public bool IsCustomizedMethod => DynamicMethod == "Customized";

        // ==================== JJG692-2010 标准检测（仅康伟达 0x04 功能的放气速率 / 气密性两项） ====================

        /// <summary>袖带类型下拉项（帧低 4 位，下标即协议值；0x12/0x13 使用）</summary>
        public List<string> JjgCuffTypes { get; } = new() { "外部袖带", "内部成人" };

        /// <summary>袖带类型（桥接：下拉项名 ↔ 协议值）</summary>
        public string JjgCuffType
        {
            get => JjgIndexToName(Params.Jjg692.CuffType, JjgCuffTypes);
            set => Params.Jjg692.CuffType = JjgNameToIndex(value, JjgCuffTypes);
        }

        /// <summary>整项检测超时（秒，仅 0x12/0x13 等设备回包用）</summary>
        public double JjgTimeoutSeconds
        {
            get => Params.Jjg692.TimeoutSeconds;
            set
            {
                Params.Jjg692.TimeoutSeconds = value <= 0 ? 300 : (int)Math.Round(value);
                OnPropertyChanged();
            }
        }

        /// <summary>0x12 放气速率（听诊）帧参数</summary>
        public double JjgDeflationSetPressure
        {
            get => Params.Jjg692.DeflationRate.SetPressure;
            set => Params.Jjg692.DeflationRate.SetPressure = value;
        }

        public double JjgDeflationStartPressure
        {
            get => Params.Jjg692.DeflationRate.StartPressure;
            set => Params.Jjg692.DeflationRate.StartPressure = value;
        }

        public double JjgDeflationStopPressure
        {
            get => Params.Jjg692.DeflationRate.StopPressure;
            set => Params.Jjg692.DeflationRate.StopPressure = value;
        }

        /// <summary>放气速率合格下限文本（留空=不判，与上限都留空时该项不判 PASS/FAIL 只报实测值）</summary>
        public string JjgDeflationMinRateText
        {
            get => FormatOptionalNumber(Params.Jjg692.DeflationRate.MinRate);
            set => Params.Jjg692.DeflationRate.MinRate = ParseOptionalNumber(value);
        }

        /// <summary>放气速率合格上限文本（留空=不判）</summary>
        public string JjgDeflationMaxRateText
        {
            get => FormatOptionalNumber(Params.Jjg692.DeflationRate.MaxRate);
            set => Params.Jjg692.DeflationRate.MaxRate = ParseOptionalNumber(value);
        }

        /// <summary>0x13 气密性帧参数</summary>
        public double JjgAirtightSetPressure
        {
            get => Params.Jjg692.Airtightness.SetPressure;
            set => Params.Jjg692.Airtightness.SetPressure = value;
        }

        /// <summary>气密性稳压时间（秒）：帧内只占 1 字节，超 255 会被 (byte) 强转静默截成低字节</summary>
        public double JjgAirtightStabilizeSeconds
        {
            get => Params.Jjg692.Airtightness.StabilizeSeconds;
            set
            {
                Params.Jjg692.Airtightness.StabilizeSeconds = Math.Clamp((int)Math.Round(value), 0, 255);
                OnPropertyChanged();
            }
        }

        /// <summary>气密性漏气率合格上限文本（留空=不判）</summary>
        public string JjgAirtightMaxLeakRateText
        {
            get => FormatOptionalNumber(Params.Jjg692.Airtightness.MaxLeakRate);
            set => Params.Jjg692.Airtightness.MaxLeakRate = ParseOptionalNumber(value);
        }

        private static string JjgIndexToName(int index, List<string> names)
            => index >= 0 && index < names.Count ? names[index] : names[0];

        private static int JjgNameToIndex(string? name, List<string> names)
        {
            var i = names.IndexOf(name ?? string.Empty);
            return i < 0 ? 0 : i;
        }

        /// <summary>可空规格值→输入框文本（null 显示空串）</summary>
        private static string FormatOptionalNumber(double? v)
            => v.HasValue ? v.Value.ToString("0.###") : string.Empty;

        /// <summary>输入框文本→可空规格值：空串/非法值返回 null（=该项不判定）</summary>
        private static double? ParseOptionalNumber(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (double.TryParse(text.Trim(), out var v)) return v;
            if (double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var inv)) return inv;
            return null;
        }

        // ==================== 保存 ====================

        public RelayCommand SaveCommand { get; }
        public RelayCommand AddPointCommand { get; }
        public RelayCommand<BpaPressurePoint> RemovePointCommand { get; }
        public RelayCommand AddBpAccuracyGroupCommand { get; }
        public RelayCommand<BpaBpPressureGroup> RemoveBpAccuracyGroupCommand { get; }
        public RelayCommand AddBpHeartRatePointCommand { get; }
        public RelayCommand<BpaHeartRatePoint> RemoveBpHeartRatePointCommand { get; }

        /// <summary>从生效设备复制点位到本页签（换仪器时省得手抄）</summary>
        public RelayCommand CopyPointsFromActiveCommand { get; }

        /// <summary>
        /// 把生效那台设备的静态压力点 + BP-004 压力组 + BP-005 心率点及其轮数/休息复制到当前页签。
        /// 逐项新建实例而不是直接搬列表：两台设备若共享同一个点位对象，改一边另一边会跟着变。
        /// 设备专属参数（充气方式/量测时间/方法/包络/JJG692）不复制，那些本来就不通用。
        /// </summary>
        private void CopyPointsFromActive()
        {
            var source = Config.ActiveBpTestParams;

            Points.Clear();
            foreach (var p in source.StaticPressure.Points.Take(MaxPointCount))
                Points.Add(new BpaPressurePoint { Enabled = p.Enabled, Value = p.Value });

            BpAccuracyGroups.Clear();
            foreach (var p in source.BpAccuracy.Groups.Take(MaxPointCount))
                BpAccuracyGroups.Add(new BpaBpPressureGroup { Enabled = p.Enabled, Systolic = p.Systolic, Diastolic = p.Diastolic });

            BpHeartRatePoints.Clear();
            foreach (var p in source.BpHeartRate.HeartRates.Take(MaxPointCount))
                BpHeartRatePoints.Add(new BpaHeartRatePoint { Enabled = p.Enabled, Value = p.Value });

            Params.BpAccuracy.HeartRate = source.BpAccuracy.HeartRate;
            Params.BpAccuracy.Repeats = source.BpAccuracy.Repeats;
            Params.BpAccuracy.RestSeconds = source.BpAccuracy.RestSeconds;
            Params.BpAccuracy.VentDuringRest = source.BpAccuracy.VentDuringRest;
            Params.BpHeartRate.Systolic = source.BpHeartRate.Systolic;
            Params.BpHeartRate.Diastolic = source.BpHeartRate.Diastolic;
            Params.BpHeartRate.Repeats = source.BpHeartRate.Repeats;
            Params.BpHeartRate.RestSeconds = source.BpHeartRate.RestSeconds;
            Params.BpHeartRate.VentDuringRest = source.BpHeartRate.VentDuringRest;
            Params.StaticPressure.VentBetweenPoints = source.StaticPressure.VentBetweenPoints;

            // 点位编辑集合就是本页签的数据源，同步回写一次，省得等切页签/保存才落地
            Params.StaticPressure.Points = Points.ToList();
            Params.BpAccuracy.Groups = BpAccuracyGroups.ToList();
            Params.BpHeartRate.HeartRates = BpHeartRatePoints.ToList();

            OnPropertyChanged(nameof(Params));
            OnPropertyChanged(nameof(BpAccuracyRepeats));
            OnPropertyChanged(nameof(BpAccuracyHeartRate));
            OnPropertyChanged(nameof(BpAccuracyRestSeconds));
            OnPropertyChanged(nameof(BpAccuracyVentDuringRest));
            OnPropertyChanged(nameof(BpHeartRateSystolic));
            OnPropertyChanged(nameof(BpHeartRateDiastolic));
            OnPropertyChanged(nameof(BpHeartRateRepeats));
            OnPropertyChanged(nameof(BpHeartRateRestSeconds));
            OnPropertyChanged(nameof(BpHeartRateVentDuringRest));
            OnPropertyChanged(nameof(BpHeartRateVentHold));
            OnPropertyChanged(nameof(StaticVentDuringRest));
            OnPropertyChanged(nameof(StaticVentHold));
            OnPropertyChanged(nameof(SelectAllPoints));
            OnPropertyChanged(nameof(PointsEmpty));
            OnPropertyChanged(nameof(CanAddPoint));
            OnPropertyChanged(nameof(SelectAllBpAccuracyGroups));
            OnPropertyChanged(nameof(BpAccuracyGroupsEmpty));
            OnPropertyChanged(nameof(CanAddBpAccuracyGroup));
            OnPropertyChanged(nameof(SelectAllBpHeartRatePoints));
            OnPropertyChanged(nameof(BpHeartRatePointsEmpty));
            OnPropertyChanged(nameof(CanAddBpHeartRatePoint));
            OnPropertyChanged(nameof(BpAccuracyVentHold));

            _log.Info($"血压模拟仪测试配置：已从生效设备 {ActiveDeviceName} 复制 {Points.Count} 个静态压力点、"
                + $"{BpAccuracyGroups.Count} 组 BP-004 压力（{Params.BpAccuracy.Repeats} 轮）、"
                + $"{BpHeartRatePoints.Count} 个 BP-005 心率点（{Params.BpHeartRate.Repeats} 轮）到 {CurrentDeviceName}，设备专属参数未复制");
        }

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
            "BP-006匀速放气控制",
            "BP-007快速放气控制",
            "BP-008过压保护"
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
                Params.BpAccuracy.Groups = BpAccuracyGroups.ToList();
                Params.BpHeartRate.HeartRates = BpHeartRatePoints.ToList();

                // 至少保留一个勾选的压力点
                if (Params.StaticPressure.Points.All(p => !p.Enabled))
                {
                    _log.Warn($"血压模拟仪测试配置：{CurrentDeviceName} 静态压力未勾选任何压力点，已自动勾选第一个");
                    if (Params.StaticPressure.Points.Count > 0)
                    {
                        Params.StaticPressure.Points[0].Enabled = true;
                    }
                }

                if (Params.BpAccuracy.Groups.Count > 0 && Params.BpAccuracy.Groups.All(g => !g.Enabled))
                {
                    _log.Warn($"血压模拟仪测试配置：{CurrentDeviceName} BP-004 压力组未勾选任何行，已自动勾选第一行");
                    Params.BpAccuracy.Groups[0].Enabled = true;
                }
                if (Params.BpHeartRate.HeartRates.Count > 0 && Params.BpHeartRate.HeartRates.All(p => !p.Enabled))
                {
                    _log.Warn($"血压模拟仪测试配置：{CurrentDeviceName} BP-005 心率点未勾选任何行，已自动勾选第一行");
                    Params.BpHeartRate.HeartRates[0].Enabled = true;
                }
                if (Params.BpAccuracy.Repeats < 3)
                {
                    Params.BpAccuracy.Repeats = 3;
                }
                if (Params.BpHeartRate.Repeats < 3)
                {
                    Params.BpHeartRate.Repeats = 3;
                }

                _configService.Save(Config);
                _log.Info($"血压模拟仪测试配置已保存（instruments.json → {(_selectedDeviceIndex == 1 ? "KangwendaSimulator" : "BPA700Simulator")}.TestParams）");
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
