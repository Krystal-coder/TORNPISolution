using System.Text.Json;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 仪器链接配置服务实现：
    /// 以 JSON 形式保存在程序目录 Config\instruments.json，
    /// 读取失败时回退默认配置并记录日志，不影响程序启动。
    /// 带文件时间戳内存缓存：文件未变时直接从缓存反序列化（不再读盘、不再记日志），
    /// 保证启动时只真正加载一次；Save 或外部修改文件后时间戳变化，下次 Load 自动重新读取。
    /// </summary>
    public class InstrumentConfigService : IInstrumentConfigService
    {
        // 配置文件保存在程序目录 Config\instruments.json
        private static readonly string ConfigPath =
            Path.Combine(AppContext.BaseDirectory, "Config", "instruments.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        // 内存缓存：原始 JSON + 文件时间戳（跨实例共享，避免启动时几十个 ViewModel 各自读盘）
        private static readonly object CacheLock = new();
        private static string? _cachedJson;
        private static DateTime _cachedFileTimeUtc = DateTime.MinValue;

        private readonly ILogService _log = new LogService(nameof(InstrumentConfigService));

        public InstrumentConnectionConfig Load()
        {
            var fileTime = File.Exists(ConfigPath) ? File.GetLastWriteTimeUtc(ConfigPath) : DateTime.MinValue;

            lock (CacheLock)
            {
                if (_cachedJson != null && _cachedFileTimeUtc == fileTime)
                {
                    // 文件未变：直接反序列化缓存，不重复读盘、不重复记日志
                    return JsonSerializer.Deserialize<InstrumentConnectionConfig>(_cachedJson, JsonOptions)
                           ?? new InstrumentConnectionConfig();
                }
            }

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
                        lock (CacheLock)
                        {
                            _cachedJson = json;
                            _cachedFileTimeUtc = fileTime;
                        }
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
                var json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(ConfigPath, json);
                _log.Info($"仪器链接配置已保存：{ConfigPath}");

                // 同步更新缓存：保存后无需重新读盘，配置页修改即时生效
                lock (CacheLock)
                {
                    _cachedJson = json;
                    _cachedFileTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
                }
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
