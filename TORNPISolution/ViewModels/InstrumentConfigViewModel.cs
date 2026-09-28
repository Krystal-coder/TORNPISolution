using System.ComponentModel;
using System.IO.Ports;
using System.Windows.Data;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using TORNPISolution.Common;
using TORNPISolution.Interface;
using TORNPISolution.Models;
using TORNPISolution.Service;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 仪器链接配置对话框的 ViewModel：
    /// 加载/保存均通过 IInstrumentConfigService，界面不直接碰文件。
    /// </summary>
    public class InstrumentConfigViewModel : ViewModelBase
    {
        private readonly IInstrumentConfigService _configService;
        private readonly ILogService _log = new LogService(nameof(InstrumentConfigViewModel));

       // 实例化
        public InstrumentConfigViewModel() : this(App.Services.GetRequiredService<IInstrumentConfigService>())
        {
        }

        public InstrumentConfigViewModel(IInstrumentConfigService configService)
        {
            _configService = configService;
            Config = _configService.Load();
            RefreshPorts();

            // 左侧导航列表按类别分组，默认选中第一项
            DeviceNavView = CollectionViewSource.GetDefaultView(DeviceNavItems);
            DeviceNavView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DeviceNavItem.Category)));
            SelectedDevice = DeviceNavItems[0];

            SaveCommand = new RelayCommand(Save, () => !TestRunState.IsRunning);
            RefreshPortsCommand = new RelayCommand(RefreshPorts);
        }

        /// <summary>当前编辑中的配置（直接双向绑定其子属性）</summary>
        public InstrumentConnectionConfig Config { get; }

        // ==================== 左侧设备导航列表 ====================

        /// <summary>设备导航项（Key 与右侧详情面板一一对应，新增设备在此追加一项）</summary>
        public List<DeviceNavItem> DeviceNavItems { get; } = new()
        {
            new() { Key = "Product",             Name = "被测产品（DUT）",   Category = "产品通讯",  Icon = PackIconKind.Chip },
            new() { Key = "PressureTransmitter", Name = "压力变送器",        Category = "测量仪器",  Icon = PackIconKind.Gauge },
            new() { Key = "MassFlowController",  Name = "质量流量控制器",     Category = "测量仪器",  Icon = PackIconKind.Waves },
            new() { Key = "Simulator",           Name = "血压模拟仪",        Category = "血压模拟仪", Icon = PackIconKind.HeartPulse },
            new() { Key = "Aecg100",             Name = "AECG100 综合性能测试仪", Category = "多功能测试仪", Icon = PackIconKind.Pulse },
            new() { Key = "NfcReader",           Name = "NFC 读卡器",        Category = "NFC 读卡器", Icon = PackIconKind.NfcVariant },
            new() { Key = "PowerSupply",         Name = "IT6411 直流电源",   Category = "直流电源",  Icon = PackIconKind.PowerPlug },
            new() { Key = "Reserved",            Name = "预留设备",          Category = "预留",      Icon = PackIconKind.PlusBoxOutline }
        };

        /// <summary>导航列表视图（按 Category 分组）</summary>
        public ICollectionView DeviceNavView { get; }

        private DeviceNavItem? _selectedDevice;

        /// <summary>当前选中的设备（切换时右侧显示对应详情面板）</summary>
        public DeviceNavItem? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                OnPropertyChanged(nameof(SelectedDevice));
            }
        }

        /// <summary>本机可用串口列表</summary>
        public List<string> AvailablePorts { get; private set; } = new();

        public int[] BaudRates { get; } = { 4800, 9600, 19200, 38400, 57600, 115200, 921600 };

        public string[] Parities { get; } = { "None", "Odd", "Even" };

        public int[] StopBitsOptions { get; } = { 1, 2 };

        public int[] DataBitsOptions { get; } = { 7, 8 };

        // ==================== BPA700 初始化设置开关（二选一：关=默认项，开=第二项） ====================
        // 官方 BPA700.h 确认：BPASwitchToAdultMode(enable) / BPASwitchTo500mlMode(enable) 均双向可切，
        // 患者类型与袖带容量可自由组合，三个开关相互独立。

        /// <summary>BPA700 患者类型开关：关=Adult（默认），开=Neonatal</summary>
        public bool Bpa700IsNeonatal
        {
            get => Config.BPA700Simulator.Category == "Neonatal";
            set
            {
                Config.BPA700Simulator.Category = value ? "Neonatal" : "Adult";
                OnPropertyChanged(nameof(Bpa700IsNeonatal));
            }
        }

        /// <summary>BPA700 压力单位开关：关=mmHg（默认），开=kPa</summary>
        public bool Bpa700IsKPa
        {
            get => Config.BPA700Simulator.PressureUnit == "kPa";
            set
            {
                Config.BPA700Simulator.PressureUnit = value ? "kPa" : "mmHg";
                OnPropertyChanged(nameof(Bpa700IsKPa));
            }
        }

        /// <summary>BPA700 袖带容量开关：关=500ml（默认），开=100ml</summary>
        public bool Bpa700IsSmallReservoir
        {
            get => Config.BPA700Simulator.AirReservoir == "100ml";
            set
            {
                Config.BPA700Simulator.AirReservoir = value ? "100ml" : "500ml";
                OnPropertyChanged(nameof(Bpa700IsSmallReservoir));
            }
        }

        // ==================== 模拟仪单选按钮绑定 ====================

        /// <summary>是否选择康伟达模拟仪</summary>
        public bool IsKangwendaSelected
        {
            get => Config.ActiveSimulator == SimulatorType.Kangwenda;
            set
            {
                if (value) Config.ActiveSimulator = SimulatorType.Kangwenda;
                OnPropertyChanged(nameof(IsKangwendaSelected));
                OnPropertyChanged(nameof(IsBPA700Selected));
                OnPropertyChanged(nameof(IsNoSimulatorSelected));
            }
        }

        /// <summary>是否选择鲸扬 BPA700</summary>
        public bool IsBPA700Selected
        {
            get => Config.ActiveSimulator == SimulatorType.BPA700;
            set
            {
                if (value) Config.ActiveSimulator = SimulatorType.BPA700;
                OnPropertyChanged(nameof(IsBPA700Selected));
                OnPropertyChanged(nameof(IsKangwendaSelected));
                OnPropertyChanged(nameof(IsNoSimulatorSelected));
            }
        }

        /// <summary>是否不启用模拟仪</summary>
        public bool IsNoSimulatorSelected
        {
            get => Config.ActiveSimulator == SimulatorType.None;
            set
            {
                if (value) Config.ActiveSimulator = SimulatorType.None;
                OnPropertyChanged(nameof(IsNoSimulatorSelected));
                OnPropertyChanged(nameof(IsKangwendaSelected));
                OnPropertyChanged(nameof(IsBPA700Selected));
            }
        }

        public ICommand SaveCommand { get; }

        /// <summary>测试是否运行中（运行期锁定配置保存，与血压模拟仪测试配置页一致）</summary>
        public bool IsRunning => TestRunState.IsRunning;

        /// <summary>是否允许保存（运行期灰色不可点）</summary>
        public bool CanSave => !IsRunning;

        public ICommand RefreshPortsCommand { get; }

        /// <summary>保存成功后请求关闭对话框（由窗口订阅）</summary>
        public event Action? CloseRequested;

        private void RefreshPorts()
        {
            _log.Info($"{DateTime.Now} Start calling the InstrumentConfigViewModel.RefreshPorts() method . {Environment.NewLine}");
            try
            {
                var ports = SerialPort.GetPortNames().Distinct().OrderBy(p => p).ToList();

                // 已保存的串口即使当前未插入也保留在列表中，避免绑定丢失
                var savedPorts = new[]
                {
                    Config.Product.PortName,
                    Config.PressureTransmitter.PortName,
                    Config.MassFlowController.PortName,
                    Config.KangwendaSimulator.PortName,
                    Config.BPA700Simulator.PortName,
                    Config.Aecg100Tester.PortName,
                    Config.NfcReader.PortName,
                    Config.ElectroacousticTester.PortName,
                    Config.ElectronicLoad.PortName
                };

                foreach (var saved in savedPorts)
                {
                    if (!string.IsNullOrWhiteSpace(saved) && !ports.Contains(saved))
                    {
                        ports.Add(saved);
                    }
                }

                AvailablePorts = ports;
                OnPropertyChanged(nameof(AvailablePorts));
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in InstrumentConfigViewModel.class RefreshPorts method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the InstrumentConfigViewModel.RefreshPorts() method . {Environment.NewLine}");
            }
        }

        private void Save()
        {
            _log.Info($"{DateTime.Now} Start calling the InstrumentConfigViewModel.Save() method . {Environment.NewLine}");
            try
            {
                _configService.Save(Config);
                CloseRequested?.Invoke();
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in InstrumentConfigViewModel.class Save method, message: {ex}", ex);
                System.Windows.MessageBox.Show($"保存失败：{ex.Message}", "仪器链接配置",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the InstrumentConfigViewModel.Save() method . {Environment.NewLine}");
            }
        }
    }
}
