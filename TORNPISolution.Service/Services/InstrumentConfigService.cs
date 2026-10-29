using System.Text.Json;
using System.Text.Json.Nodes;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 仪器链接配置服务：配置以 JSON 存于程序目录 Config\instruments.json，读取失败时回退默认配置并记录日志，不影响程序启动
    /// </summary>
    public class InstrumentConfigService : IInstrumentConfigService
    {
        private static readonly string ConfigPath =
            Path.Combine(AppContext.BaseDirectory, "Config", "instruments.json");

        // 覆盖前留下的上一份（主文件不可用时回退）与写盘用的临时文件，都跟主文件同目录（同盘才能改名替换）
        private static readonly string BackupPath = ConfigPath + ".bak";
        private static readonly string TempPath = ConfigPath + ".tmp";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        // 内存缓存：原始 JSON + 文件时间戳（静态、跨实例共享，启动时几十个 ViewModel 不再各自读盘）；缓存文本同时是 Save 的合并基线
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
                    // 文件未变：直接反序列化缓存，不重复读盘、不重复记日志（缓存文本已剔过显式 null）
                    return JsonSerializer.Deserialize<InstrumentConnectionConfig>(_cachedJson, JsonOptions)
                           ?? new InstrumentConnectionConfig();
                }
            }

            _log.Info($"{DateTime.Now} Start calling the InstrumentConfigService.Load() method . {Environment.NewLine}");
            try
            {
                // 主文件解析不了（半截、被手工改坏）就退到上一份 .bak：退成全默认会让 ActiveSimulator 回到 None、所有血压项不跑，而日志只有一行反序列化失败
                foreach (var path in new[] { ConfigPath, BackupPath })
                {
                    var node = ReadConfigNode(path);
                    if (node == null) continue;

                    var json = node.ToJsonString(JsonOptions);
                    var config = JsonSerializer.Deserialize<InstrumentConnectionConfig>(json, JsonOptions);
                    if (config == null) continue;

                    _log.Info(path == ConfigPath
                        ? $"仪器链接配置已加载：{path}"
                        : $"仪器链接配置已从备份加载：{path}（主文件不可用，请检查 {ConfigPath}）");
                    lock (CacheLock)
                    {
                        _cachedJson = json;
                        _cachedFileTimeUtc = fileTime;    // 用进来的那份时间戳：主文件不存在而走了 .bak 时才不会每次 Load 都重读+重记日志
                    }
                    return config;
                }

                _log.Info("仪器链接配置文件不存在或不可读，使用默认配置");
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

                // 本次要写的整档（同样剔过显式 null，与磁盘/基线三份文本口径一致，合并时才是同一种形式在比）
                var incoming = StripNulls(JsonSerializer.SerializeToNode(config, JsonOptions)).AsObject();
                var finalNode = incoming;

                // 基线缺失（本进程还没读到过盘，或文件本来就不可用）时无从判断"哪些字段是本窗口改的"，按整档覆盖
                var disk = ReadConfigNode(ConfigPath);
                string? baselineText;
                lock (CacheLock) { baselineText = _cachedJson; }
                if (disk != null && baselineText != null)
                    finalNode = MergeChangedFields(disk.AsObject(), JsonNode.Parse(baselineText), incoming);

                var json = finalNode.ToJsonString(JsonOptions);
                WriteAtomic(json);
                _log.Info($"仪器链接配置已保存：{ConfigPath}");

                // 同步更新缓存：本进程后续 Load 直接取到本次保存的值，不必等文件时间戳变化
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

        /// <summary>读一份配置文件并剔掉显式 null；不存在或解析不了返回 null（解析失败记 Error，不抛）</summary>
        private JsonNode? ReadConfigNode(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var node = JsonNode.Parse(File.ReadAllText(path));
                if (node is not JsonObject) { _log.Error($"{path} 内容不是 JSON 对象，已忽略"); return null; }
                return StripNulls(node);
            }
            catch (Exception ex)
            {
                _log.Error($"{path} 解析失败：{ex.Message}");
                return null;
            }
        }

        /// <summary>递归剔除显式 null：字段按缺失处理，反序列化时走属性初始化器里的默认值（默认点位清单也在里面）；可空数值（double? 规格上下限）初始化值本就是 null，"留空不判"语义不变</summary>
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
                        // 数组里的 null 元素换成空对象，建出来就是该行的默认参数，执行端枚举时不会碰到 null
                        if (array[i] is null) array[i] = new JsonObject();
                        else StripNulls(array[i]);
                    return array;
                default:
                    return node!;    // 标量原样返回
            }
        }

        /// <summary>
        /// 三方合并：结果以磁盘最新那份为底，与基线（调用方当初 Load 到的快照）相同的字段说明本窗口没改过、保留磁盘那份，不同的才用 incoming。
        /// 对象往下递归逐字段比（同一节点内两个窗口各改自己的字段不会互相覆盖），数组整份替换（配置页列所见即所存）；同一窗口连续两次保存时基线以上一次落盘内容为准，本窗口内存里的值覆盖一切。
        /// </summary>
        private static JsonObject MergeChangedFields(JsonObject disk, JsonNode? baseline, JsonObject incoming)
        {
            var baseObj = baseline as JsonObject;

            foreach (var property in incoming)
            {
                var name = property.Key;
                var value = property.Value;

                JsonNode? baseValue = null;
                var inBaseline = baseObj is not null && baseObj.TryGetPropertyValue(name, out baseValue);

                if (inBaseline && JsonNode.DeepEquals(baseValue, value))
                    continue;

                var current = disk.TryGetPropertyValue(name, out var existing) ? existing : null;
                JsonNode? merged = value is JsonObject valueObj
                    ? MergeChangedFields((current as JsonObject) ?? new JsonObject(), baseValue, valueObj)
                    : value;
                if (merged == null) { disk.Remove(name); continue; }
                disk[name] = merged.DeepClone();    // DeepClone：merged 可能挂在另一棵树上，不能直接接过来
            }

            // 基线里有、本次整档里没有的字段 = 调用方已把它清成 null（可空规格留空即不判）：磁盘上那份一并删掉，否则下次读回来还是旧值、清空等于没保存
            if (baseObj != null)
                foreach (var name in baseObj.Select(pair => pair.Key).ToList())
                    if (!incoming.ContainsKey(name)) disk.Remove(name);

            return disk;
        }

        /// <summary>临时文件 + 改名替换：不存在"已截断原文件、新内容还没写完"的中间状态；覆盖前把旧档存成 .bak 供 Load 回退；目标被其它进程占用时直接抛出，磁盘上原有配置仍完整可用</summary>
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
