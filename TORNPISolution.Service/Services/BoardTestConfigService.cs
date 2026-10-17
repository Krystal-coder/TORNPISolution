using System.Text.Json;
using System.Text.Json.Nodes;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 单板运行配置服务：保存在程序目录 Config\board-test.json。
    /// 落盘护住现场的三样与烧录那份服务同口径：
    ///   反序列化前剔掉显式 null（属性初始化器只在缺字段时生效，显式 null 会原样赋进去，界面直取就 NRE）；
    ///   主文件解析不了退回上一份 .bak（退成默认会让现场以为勾选项被清空了）；
    ///   写盘走临时文件 + 改名替换，中途断电不会留下半截 JSON。
    /// 每次读盘不缓存：这一页与菜单各写各的（勾选项在菜单里改、通道在卡片右键改），缓存只会让两边看到不同的旧值。
    /// </summary>
    public class BoardTestConfigService : IBoardTestConfigService
    {
        private static readonly string ConfigPath =
            Path.Combine(AppContext.BaseDirectory, "Config", "board-test.json");

        private static readonly string BackupPath = ConfigPath + ".bak";
        private static readonly string TempPath = ConfigPath + ".tmp";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly ILogService _log = new LogService(nameof(BoardTestConfigService));

        public BoardTestConfig Load()
        {
            try
            {
                foreach (var path in new[] { ConfigPath, BackupPath })
                {
                    var json = ReadConfigJson(path);
                    if (json == null) continue;

                    var config = JsonSerializer.Deserialize<BoardTestConfig>(json, JsonOptions);
                    if (config == null) continue;

                    _log.Info(path == ConfigPath
                        ? $"单板运行配置已加载：{path}"
                        : $"单板运行配置已从备份加载（主文件不可用，请检查 {ConfigPath}）");

                    // 通道清单被手工删空就没有卡片可点；勾选项为空则一轮都跑不起来，两者都补回默认值并说一声
                    if (config.Channels.Count == 0)
                    {
                        _log.Warn("单板运行配置里通道清单是空的，已按默认 8 路补回");
                        config.Channels = new BoardTestConfig().Channels;
                    }
                    if (config.SelectedItems.Count == 0)
                        _log.Info("单板运行配置里没勾任何测试项（这一页会拦住开跑，等人在菜单「测试项」里勾）");

                    return config;
                }

                _log.Info("单板运行配置不存在，使用默认配置（勾一次测试项或保存一次通道即会生成）");
            }
            catch (Exception ex)
            {
                _log.Error($"读取单板运行配置失败，按默认配置继续：{ex.Message}", ex);
            }

            return new BoardTestConfig();
        }

        public void Save(BoardTestConfig config)
        {
            _log.Info($"{DateTime.Now} Start calling the BoardTestConfigService.Save() method . {Environment.NewLine}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

                var node = StripNulls(JsonSerializer.SerializeToNode(config, JsonOptions));
                WriteAtomic(node!.ToJsonString(JsonOptions));
                _log.Info($"单板运行配置已保存：勾选项 {config.SelectedItems.Count} 项，通道 {config.Channels.Count} 路");
            }
            catch (Exception ex)
            {
                _log.Error($"保存单板运行配置失败：{ex.Message}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the BoardTestConfigService.Save() method . {Environment.NewLine}");
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

        /// <summary>递归剔掉显式 null：字段当缺失处理，反序列化时走属性初始化器里的默认值</summary>
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

        /// <summary>临时文件 + 改名替换：不存在"已截断原文件、新内容还没写完"的中间状态；覆盖前把旧档存成 .bak</summary>
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
