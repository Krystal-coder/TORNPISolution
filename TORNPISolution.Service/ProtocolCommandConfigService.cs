using System.Text.Json;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 产品协议命令码配置服务：
    /// 以 JSON 形式保存在程序目录 Config\protocol_commands.json，
    /// 文件不存在时自动生成默认配置；缺 key 回退默认值并记录日志。
    /// </summary>
    public class ProtocolCommandConfigService : IProtocolCommandConfigService
    {
        private static readonly string ConfigPath =
            Path.Combine(AppContext.BaseDirectory, "Config", "protocol_commands.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly ILogService _log = new LogService(nameof(ProtocolCommandConfigService));

        /// <summary>硬编码默认值（与原 ProductProtocol const 完全一致）</summary>
        private static readonly Dictionary<string, ProtocolCommandEntry> Defaults = new()
        {
            ["CtrlKeepAlive"]      = new() { Code = 0x01, FuncCode = 0x07, Description = "心跳保活（防止产品休眠）" },
            ["CtrlGetVersion"]     = new() { Code = 0x72, Description = "读版本/MAC" },
            ["CtrlSetMode"]        = new() { Code = 0x73, Description = "设置测试模式" },
            ["CtrlGetState"]       = new() { Code = 0x74, Description = "读测试状态" },
            ["CtrlStartInflate"]   = new() { Code = 0x75, FuncCode = 0x05, Description = "开始充气" },
            ["CtrlGetPressure"]    = new() { Code = 0x76, FuncCode = 0x05, Description = "读当前压力值" },
            ["CtrlStartDeflate"]   = new() { Code = 0x77, FuncCode = 0x05, Description = "开始放气" },
            ["CtrlInflateStop"]    = new() { Code = 0x78, Description = "充放气停止" },
            ["CtrlStartBpMeasure"] = new() { Code = 0x79, Description = "开始血压测量" },
            ["CtrlGetBpValue"]     = new() { Code = 0x7A, Description = "读瞬时血压值" },
            ["CtrlGetBpResult"]    = new() { Code = 0x7B, Description = "读血压测量最终结果/测量完成主动推送" },
            ["CtrlGetHrValue"]     = new() { Code = 0x7C, Description = "读心率值" },
            ["CtrlStopBpMeasure"]  = new() { Code = 0x7D, Description = "停止血压测量" },
            ["CtrlGetStaticPress"] = new() { Code = 0x7E, Description = "读静态压力值" },
            ["CtrlStartSpo2"]      = new() { Code = 0x80, Description = "开始/停止血氧测量" },
            ["CtrlGetSpo2Value"]   = new() { Code = 0x81, Description = "读血氧值" },
            ["CtrlGetLeadState"]   = new() { Code = 0x88, Description = "读导联状态" },
            ["CtrlGetBatteryInfo"]  = new() { Code = 0x6C, FuncCode = 0x07, Description = "读电池信息（电压+百分比）" },
            ["CtrlGetChargingStatus"] = new() { Code = 0x6D, FuncCode = 0x07, Description = "读充电状态" },
        };

        public Dictionary<string, ProtocolCommandEntry> Load()
        {
            _log.Info($"{DateTime.Now} Start calling the ProtocolCommandConfigService.Load() method . {Environment.NewLine}");
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    _log.Info("协议命令配置文件不存在，自动生成默认配置");
                    Save(Defaults);
                    return new Dictionary<string, ProtocolCommandEntry>(Defaults);
                }

                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, ProtocolCommandEntry>>(json, JsonOptions);
                if (loaded == null)
                {
                    _log.Warn("协议命令配置文件反序列化为 null，使用默认配置");
                    return new Dictionary<string, ProtocolCommandEntry>(Defaults);
                }

                var result = new Dictionary<string, ProtocolCommandEntry>(Defaults);
                foreach (var kvp in loaded)
                {
                    if (result.ContainsKey(kvp.Key))
                        result[kvp.Key] = kvp.Value;
                    else
                        _log.Warn($"协议命令配置包含未知键 '{kvp.Key}'，已忽略");
                }

                foreach (var key in Defaults.Keys)
                {
                    if (!loaded.ContainsKey(key))
                        _log.Warn($"协议命令配置缺少键 '{key}'，使用默认值 0x{Defaults[key].Code:X2}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in ProtocolCommandConfigService.class Load method, message: {ex}", ex);
                return new Dictionary<string, ProtocolCommandEntry>(Defaults);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the ProtocolCommandConfigService.Load() method . {Environment.NewLine}");
            }
        }

        public void Save(Dictionary<string, ProtocolCommandEntry> commands)
        {
            _log.Info($"{DateTime.Now} Start calling the ProtocolCommandConfigService.Save() method . {Environment.NewLine}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(commands, JsonOptions));
                _log.Info($"协议命令配置已保存：{ConfigPath}");
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in ProtocolCommandConfigService.class Save method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the ProtocolCommandConfigService.Save() method . {Environment.NewLine}");
            }
        }
    }
}
