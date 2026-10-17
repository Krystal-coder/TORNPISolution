using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// 落盘两处护住现场：保存前先重读磁盘、只把本次真改过的字段写回去（多个配置窗口各存一份整档快照，
    /// 整档覆盖会把别的窗口或手工改的内容抹掉）；写盘走临时文件+改名替换，覆盖前把旧档留成 .bak，
    /// 中途断电不会留下半截 JSON，真被改坏了 Load 还能退回上一份。
    /// 反序列化前先剔掉 JSON 里的显式 null：属性初始化器只在"缺字段"时生效，显式 null 会原样赋进去，
    /// 执行端 config.X.Y 直取就 NRE（目录端读同一节点是有兜底的），剔掉后两边都走默认值。
    /// </summary>
    public class InstrumentConfigService : IInstrumentConfigService
    {
        // 配置文件保存在程序目录 Config\instruments.json
        private static readonly string ConfigPath =
            Path.Combine(AppContext.BaseDirectory, "Config", "instruments.json");

        // 覆盖前留下的上一份（主文件不可用时回退）与写盘用的临时文件，都跟主文件同目录（同盘才能改名替换）
        private static readonly string BackupPath = ConfigPath + ".bak";
        private static readonly string TempPath = ConfigPath + ".tmp";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        // 内存缓存：原始 JSON + 文件时间戳（跨实例共享，避免启动时几十个 ViewModel 各自读盘）
        // 缓存里存的文本同时当 Save 的合并基线（就是发给调用方的那一份）
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
                // 主文件解析不了（半截、被手工改坏）就退到上一份 .bak：退成全默认意味着 ActiveSimulator 回到 None，
                // 现场表现为所有血压项突然不跑，而日志只有一行反序列化失败
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

        /// <summary>
        /// 递归剔掉显式 null：字段当缺失处理，反序列化时走属性初始化器里的默认值（默认点位清单也在里面）。
        /// 可空数值（double? 规格上下限）初始化值本就是 null，缺字段与显式 null 结果一样，“留空不判”的语义不变。
        /// </summary>
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
        /// 三方合并：结果以磁盘最新那份为底，逐字段定活用谁的值 ——
        /// incoming 与基线（调用方当初 Load 到的快照）相同的字段说明本窗口没改过，保留磁盘那份；不同的才用 incoming。
        /// 对象往下递归比（所以同一节点里两个窗口各改自己的字段不会互抹），数组整份替换（配置页列所见即所存）。
        /// 注：同一个窗口连着点两次保存时，基线以上一次落盘内容为准，本窗口内存里的值覆盖一切（界面看到什么就存什么）。
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

                // 与当初读到的快照一致 = 这一项本窗口没碰过，保留磁盘上那份（可能是别的窗口或手工刚写的）
                if (inBaseline && JsonNode.DeepEquals(baseValue, value))
                    continue;

                var current = disk.TryGetPropertyValue(name, out var existing) ? existing : null;
                JsonNode? merged = value is JsonObject valueObj
                    ? MergeChangedFields((current as JsonObject) ?? new JsonObject(), baseValue, valueObj)
                    : value;
                if (merged == null) { disk.Remove(name); continue; }
                disk[name] = merged.DeepClone();    // DeepClone：merged 可能挂在另一棵树上，不能直接接过来
            }

            // 基线里有、本次整档里没有的字段：调用方把它清成 null 了（可空规格留空=不判），落盘时要把磁盘上那份也抹掉，
            // 否则界面上下一次读回来还是旧值，"清规格"这一下等于没保存
            if (baseObj != null)
                foreach (var name in baseObj.Select(pair => pair.Key).ToList())
                    if (!incoming.ContainsKey(name)) disk.Remove(name);

            return disk;
        }

        /// <summary>
        /// 临时文件 + 改名替换：不存在"已经把原文件截断、新内容还没写完"的中间状态；覆盖前把旧档存成 .bak 供 Load 回退。
        /// 目标文件被其它进程占用时直接抛（保存失败），磁盘上原有的配置仍是完整可用的。
        /// </summary>
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
                // 同盘改名，不存在"截断后写一半"的中间状态；顺带把旧档留成 .bak 供 Load 回退
                File.Replace(TempPath, ConfigPath, BackupPath);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(TempPath, ConfigPath, overwrite: true);   // 少数文件系统不支持替换（FAT 等）
            }
        }
    }
}
