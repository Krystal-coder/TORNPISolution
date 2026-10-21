using System.Text.Json;
using System.Text.Json.Serialization;
using TORNPISolution.Interface;
using TORNPISolution.Share.Enum;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 应用工位状态服务：程序目录 Config\app_state.json 存"上次停留的页面"与"模块库上次的工位类别"两个字段。
    /// 文件只读一次进内存，之后重复读不再开文件；写盘走临时文件 + 改名替换，文件不可用时按默认值（整机测试）启动、不抛。
    /// </summary>
    public class AppStateService : IAppStateService
    {
        private static readonly string StatePath =
            Path.Combine(AppContext.BaseDirectory, "Config", "app_state.json");

        private static readonly string TempPath = StatePath + ".tmp";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly ILogService _log = new LogService(nameof(AppStateService));

        private ModuleCategory _lastCategory = ModuleCategory.WholeMachine;
        private WorkbenchPage _lastPage = WorkbenchPage.WholeMachine;
        private bool _read;
        private readonly object _sync = new();

        /// <inheritdoc/>
        public ModuleCategory LoadLastCategory()
        {
            lock (_sync)
            {
                Read();
                return _lastCategory;
            }
        }

        /// <inheritdoc/>
        public WorkbenchPage LoadLastPage()
        {
            lock (_sync)
            {
                Read();
                return _lastPage;
            }
        }

        /// <summary>读一次盘（已读过直接返回）：两个字段同属一份文件，谁先要都只开一次</summary>
        private void Read()
        {
            if (_read) return;
            _read = true;

            try
            {
                if (File.Exists(StatePath))
                {
                    var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath), JsonOptions);
                    if (state != null)
                    {
                        _lastCategory = state.LastModuleCategory;
                        _lastPage = state.LastWorkbenchPage;
                    }
                }
            }
            catch (Exception ex)
            {
                // 半截或被手工改坏的文件不影响使用：按整机测试页开，下次切页会重写
                _log.Warn($"工位状态文件不可用，按{WorkbenchPage.WholeMachine.ToDisplay()}启动：{ex.Message}");
            }

            _log.Info($"本机工位页面：{_lastPage.ToDisplay()}，模块类别：{_lastCategory.ToDisplay()}（{StatePath}）");
        }

        /// <inheritdoc/>
        public void SaveLastCategory(ModuleCategory category)
        {
            lock (_sync)
            {
                _lastCategory = category;
                _read = true;
                Write();
            }
        }

        /// <inheritdoc/>
        public void SaveLastPage(WorkbenchPage page)
        {
            lock (_sync)
            {
                _lastPage = page;
                _read = true;
                Write();
            }
        }

        /// <summary>整档写盘：先写临时文件再改名替换，写一半被断电不会留下不可用的状态文件</summary>
        private void Write()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
                var json = JsonSerializer.Serialize(new AppState
                {
                    LastModuleCategory = _lastCategory,
                    LastWorkbenchPage = _lastPage,
                }, JsonOptions);

                File.WriteAllText(TempPath, json);
                File.Move(TempPath, StatePath, true);
                _log.Info($"工位状态已保存：页面 {_lastPage.ToDisplay()}，模块类别 {_lastCategory.ToDisplay()}");
            }
            catch (Exception ex)
            {
                _log.Error($"工位状态保存失败（本次运行内仍生效，重启后会回到上次的页面）：{ex.Message}");
            }
        }

        private sealed class AppState
        {
            public ModuleCategory LastModuleCategory { get; set; } = ModuleCategory.WholeMachine;

            /// <summary>上次停留的工位页面；老文件里没这个字段时按默认值落回整机测试页</summary>
            public WorkbenchPage LastWorkbenchPage { get; set; } = WorkbenchPage.WholeMachine;
        }
    }
}
