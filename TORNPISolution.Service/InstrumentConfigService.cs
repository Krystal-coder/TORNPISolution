using System.Text.Json;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 仪器链接配置服务实现：
    /// 以 JSON 形式保存在程序目录 Config\instruments.json，
    /// 读取失败时回退默认配置并记录日志，不影响程序启动。
    /// </summary>
    public class InstrumentConfigService : IInstrumentConfigService
    {
        // 配置文件存到用户 AppData 目录，避免编译清理输出目录时丢失
        private static readonly string ConfigPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TORNPISolution", "Config", "instruments.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly ILogService _log = new LogService(nameof(InstrumentConfigService));

        public InstrumentConnectionConfig Load()
        {
            _log.Info($"{DateTime.Now} Start calling the InstrumentConfigService.Load() method . {Environment.NewLine}");
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<InstrumentConnectionConfig>(json, JsonOptions);
                    if (config != null)
                    {
                        _log.Info($"仪器链接配置已加载：{ConfigPath}");
                        return config;
                    }
                }
                else
                {
                    _log.Info("仪器链接配置文件不存在，使用默认配置");
                }
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in InstrumentConfigService.class Load method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the InstrumentConfigService.Load() method . {Environment.NewLine}");
            }

            return new InstrumentConnectionConfig();
        }

        public void Save(InstrumentConnectionConfig config)
        {
            _log.Info($"{DateTime.Now} Start calling the InstrumentConfigService.Save() method . {Environment.NewLine}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
                _log.Info($"仪器链接配置已保存：{ConfigPath}");
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in InstrumentConfigService.class Save method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the InstrumentConfigService.Save() method . {Environment.NewLine}");
            }
        }
    }
}
