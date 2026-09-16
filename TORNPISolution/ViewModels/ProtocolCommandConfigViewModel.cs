using System.Collections.ObjectModel;
using TORNPISolution.Common;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>产品协议命令码配置 ViewModel：DataGrid 展示 16 条 Ctrl* 命令，保存前需密码验证</summary>
    public class ProtocolCommandConfigViewModel : ViewModelBase
    {
        private readonly IProtocolCommandConfigService _configService;
        private readonly ILogService _log = new LogService(nameof(ProtocolCommandConfigViewModel));

        public ProtocolCommandConfigViewModel() : this(new ProtocolCommandConfigService()) { }

        public ProtocolCommandConfigViewModel(IProtocolCommandConfigService configService)
        {
            _configService = configService;
            var loaded = _configService.Load();

            Commands = new ObservableCollection<ProtocolCommandRow>();
            foreach (var key in OrderedKeys)
            {
                if (loaded.TryGetValue(key, out var entry))
                    Commands.Add(new ProtocolCommandRow { Name = key, Code = entry.Code, FuncCode = entry.FuncCode, Description = entry.Description });
            }
        }

        /// <summary>保持与源码声明一致的展示顺序</summary>
        private static readonly string[] OrderedKeys =
        {
            "CtrlGetVersion", "CtrlSetMode", "CtrlGetState", "CtrlStartInflate",
            "CtrlGetPressure", "CtrlStartDeflate", "CtrlInflateStop", "CtrlStartBpMeasure",
            "CtrlGetBpValue", "CtrlGetBpResult", "CtrlGetHrValue", "CtrlStopBpMeasure",
            "CtrlGetStaticPress", "CtrlStartSpo2", "CtrlGetSpo2Value", "CtrlGetLeadState",
            "CtrlGetBatteryInfo", "CtrlGetChargingStatus"
        };

        /// <summary>DataGrid 绑定源</summary>
        public ObservableCollection<ProtocolCommandRow> Commands { get; }

        /// <summary>保存成功后请求关闭窗口</summary>
        public event Action? CloseRequested;

        /// <summary>密码验证后保存；返回 false 表示密码错误</summary>
        public bool ValidateAndSave(string username, string password)
        {
            _log.Info($"{DateTime.Now} Start calling the ProtocolCommandConfigViewModel.ValidateAndSave() method . {Environment.NewLine}");
            try
            {
                if (username != "admin" || password != "admin")
                {
                    _log.Warn("协议命令配置保存被拒绝：密码验证失败");
                    return false;
                }

                var dict = new Dictionary<string, ProtocolCommandEntry>();
                foreach (var row in Commands)
                    dict[row.Name] = new ProtocolCommandEntry { Code = row.Code, FuncCode = row.FuncCode, Description = row.Description };

                _configService.Save(dict);
                ProductProtocol.ReloadCommands();
                _log.Info("协议命令配置保存成功并已热重载");

                CloseRequested?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in ProtocolCommandConfigViewModel.ValidateAndSave method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the ProtocolCommandConfigViewModel.ValidateAndSave() method . {Environment.NewLine}");
            }
        }
    }

    /// <summary>DataGrid 行模型（可通知属性变更以支持实时编辑）</summary>
    public class ProtocolCommandRow : ViewModelBase
    {
        private string _name = string.Empty;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private byte _code;
        public byte Code { get => _code; set => SetProperty(ref _code, value); }

        private byte _funcCode = 0x07;
        public byte FuncCode { get => _funcCode; set => SetProperty(ref _funcCode, value); }

        private string _description = string.Empty;
        public string Description { get => _description; set => SetProperty(ref _description, value); }
    }
}
