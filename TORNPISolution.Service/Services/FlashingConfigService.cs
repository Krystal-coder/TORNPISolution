using System.Text.Json;
using System.Text.Json.Nodes;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 烧录工位配置服务实现：保存在程序目录 Config\flashing.json。写入口只有烧录页一个，所以不做三方合并、不做内存缓存（手工改文件立即生效）。
    /// 剔显式 null、.bak 兜底、临时文件+改名替换这三样做法同 InstrumentConfigService。
    /// </summary>
    public class FlashingConfigService : IFlashingConfigService
    {
        private static readonly string ConfigPath =
            Path.Combine(AppContext.BaseDirectory, "Config", "flashing.json");

        // 备份与临时文件都跟主文件同目录：同盘才能用改名替换
        private static readonly string BackupPath = ConfigPath + ".bak";
        private static readonly string TempPath = ConfigPath + ".tmp";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly ILogService _log = new LogService(nameof(FlashingConfigService));

        public FlashingConfig Load()
        {
            try
            {
                foreach (var path in new[] { ConfigPath, BackupPath })
                {
                    var json = ReadConfigJson(path);
                    if (json == null) continue;

                    var config = JsonSerializer.Deserialize<FlashingConfig>(json, JsonOptions);
                    if (config == null) continue;

                    if (path != ConfigPath)
                        _log.Warn($"烧录配置已从备份加载（主文件不可用，请检查 {ConfigPath}）");
                    else
                        _log.Info($"烧录配置已加载：{path}");

                    // 通道清单被手工删空或被写成 null 时，界面就没有通道可点了；补回默认清单
                    if (config.Channels.Count == 0)
                    {
                        _log.Warn("烧录配置里通道清单是空的，已按默认 8 路补回");
                        config.Channels = new FlashingConfig().Channels;
                    }
                    return config;
                }

                _log.Info("烧录配置文件不存在，使用默认配置（烧录页保存一次即会生成）");
            }
            catch (Exception ex)
            {
                _log.Error($"读取烧录配置失败，按默认配置继续：{ex.Message}", ex);
            }

            return new FlashingConfig();
        }

        public void Save(FlashingConfig config)
        {
            _log.Info($"{DateTime.Now} Start calling the FlashingConfigService.Save() method . {Environment.NewLine}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

                var node = StripNulls(JsonSerializer.SerializeToNode(config, JsonOptions));
                WriteAtomic(node!.ToJsonString(JsonOptions));
                _log.Info($"烧录配置已保存：{ConfigPath}");
            }
            catch (Exception ex)
            {
                _log.Error($"保存烧录配置失败：{ex.Message}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the FlashingConfigService.Save() method . {Environment.NewLine}");
            }
        }

        /// <summary>读一份配置文件并剔掉显式 null；不存在或解析不了返回 null（记日志不抛）</summary>
        private string? ReadConfigJson(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var parsed = JsonNode.Parse(File.ReadAllText(path));
                if (parsed is not JsonObject)
                {
                    _log.Error($"{path} 内容不是 JSON 对象，已忽略");
                    return null;
                }
                return StripNulls(parsed).ToJsonString(JsonOptions);
            }
            catch (Exception ex)
            {
                _log.Error($"{path} 解析失败：{ex.Message}");
                return null;
            }
        }

        /// <summary>递归剔掉显式 null：字段当缺失处理，反序列化时走属性初始化器里的默认值；数组里的 null 元素换成空对象（建出来即该行的默认参数），界面枚举通道时不会碰到 null。</summary>
        private static JsonNode StripNulls(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var name in obj.Select(pair => pair.Key).ToList())   // 先取键，下面要删
                    {
                        if (obj[name] is null) { obj.Remove(name); continue; }
                        StripNulls(obj[name]);
                    }
                    return obj;
                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                    {
                        if (array[i] is null) array[i] = new JsonObject();
                        else StripNulls(array[i]);
                    }
                    return array;
                default:
                    return node!;    // 标量原样返回
            }
        }

        /// <summary>临时文件 + 改名替换：不存在"已截断原文件、新内容还没写完"的中间状态，覆盖前把旧档存成 .bak；目标被其它进程占用时直接抛（保存失败），磁盘上原有的配置仍是完整可用的。</summary>
        private static void WriteAtomic(string json)
        {
            File.WriteAllText(TempPath, json);
            if (!File.Exists(ConfigPath))
            {
                File.Move(TempPath, ConfigPath);      // 首次落盘，没有要替换的目标
                return;
            }

            try
            {
                File.Replace(TempPath, ConfigPath, BackupPath);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(TempPath, ConfigPath, overwrite: true);   // 少数文件系统不支持替换（FAT 等）
            }
        }
    }
}
