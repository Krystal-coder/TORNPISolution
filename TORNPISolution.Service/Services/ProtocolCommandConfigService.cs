using System.Text.Json;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 产品协议命令码配置服务：以 JSON 保存在程序目录 Config\protocol_commands.json，仅在该文件不存在时自动生成默认配置
    /// Load 以 Defaults 为底、用文件里的同名键覆盖（运行时改过的命令码重启后仍在）
    ///文件缺 key 回退默认值并记录日志，未知键忽略。
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

        /// <summary>硬编码默认值（与 Config\protocol_commands.json 模板保持一致）</summary>
        private static readonly Dictionary<string, ProtocolCommandEntry> Defaults = new()
        {
            // ---- 心跳（Func=0x07）----
            ["CtrlKeepAlive"]               = new() { Code = 0x01, FuncCode = 0x07, Description = "进入产测模式" },//打开串口后立即发送防产品休眠，须在 0x72/0x74 查询前发送
            ["CtrlExitProductionTestMode"]  = new() { Code = 0x10, FuncCode = 0x07, Description = "退出产测模式" },//与进入产测 0x01 成对；样帧 EF 02 07 10 96 CC FE

            // ---- 产品信息查询（Func=0x02）----
            ["CtrlGetProductTime"]          = new() { Code = 0x30, FuncCode = 0x02, Description = "读产品时间" },
            ["CtrlGetAllUserParams"]        = new() { Code = 0x31, FuncCode = 0x02, Description = "读用户所有参数" },
            ["CtrlGetUserCount"]            = new() { Code = 0x32, FuncCode = 0x02, Description = "读取用户数量" },
            ["CtrlGetProductConfig"]        = new() { Code = 0x33, FuncCode = 0x02, Description = "读产品配置参数" },
            ["CtrlGetProductStatus"]        = new() { Code = 0x34, FuncCode = 0x02, Description = "读产品状态" },
            ["CtrlGetDisconnectInfo"]       = new() { Code = 0x35, FuncCode = 0x02, Description = "读产品断联信息" },
            ["CtrlGetResetInfo"]            = new() { Code = 0x36, FuncCode = 0x02, Description = "读产品复位信息" },
            ["CtrlGetVersionInfo"]          = new() { Code = 0x37, FuncCode = 0x02, Description = "读产品版本信息" },

            // ---- 测量控制（Func=0x05）----
            ["CtrlGetVersion"]              = new() { Code = 0x72, FuncCode = 0x05, Description = "读产品嵌软版本+MAC地址" },
            ["CtrlSetMode"]                 = new() { Code = 0x73, FuncCode = 0x05, Description = "设置测试模式" },//参数 2 字节：uint8 test_mode + uint8 status，status 0=进入测量 1=退出测量；袖带/血压/静态压力/血氧等模式走本条
            ["CtrlStartEcgTest"]            = new() { Code = 0x73, FuncCode = 0x05, Data = "05 00", Description = "开始ECG测试" },//样帧 EF 04 05 73 05 00 15 03 FE；产品开始持续推送 0x8D 心电波形，直到收到关闭命令
            ["CtrlStopEcgTest"]             = new() { Code = 0x73, FuncCode = 0x05, Data = "05 01", Description = "关闭ECG测试" },//样帧 EF 04 05 73 05 01 D4 C3 FE；产品停止推送心电波形
            ["CtrlGetState"]                = new() { Code = 0x74, FuncCode = 0x05, Description = "读取产品状态" },//待机/测量中
            ["CtrlStartInflate"]            = new() { Code = 0x75, FuncCode = 0x05, Description = "开始充气 s_test_start_inflate" },//参数 4 字节：uint16 LE 目标压力mmHg + uint16 LE 最长充气时间秒；超时后产品底层自行放气
            ["CtrlGetPressure"]             = new() { Code = 0x76, FuncCode = 0x05, Description = "读取产品压力值" },//静态压力回读命令，帧 EF 02 05 76 17 86 FE
            ["CtrlStartDeflate"]            = new() { Code = 0x77, FuncCode = 0x05, Description = "开始放气" },
            ["CtrlInflateStop"]             = new() { Code = 0x78, FuncCode = 0x05, Description = "写停止命令" },//参数 0充气/1放气
            ["CtrlStartBpMeasure"]          = new() { Code = 0x79, FuncCode = 0x05, Description = "开始/关闭一次完整血压测量" },//s_test_start_bp_measure（参数 uint8 status：0=开始一次测量 1=关闭一次完整测量；测完须发一次关闭才能再次开始）
            ["CtrlGetBpValue"]              = new() { Code = 0x7A, FuncCode = 0x05, Description = "读取当前血压测量值" },
            ["CtrlGetBpResult"]             = new() { Code = 0x7B, FuncCode = 0x05, Description = "读取血压测量结果/测量完成主动推送" },
            ["CtrlGetHrValue"]              = new() { Code = 0x7C, FuncCode = 0x05, Description = "读取当前心率值" },
            ["CtrlStopBpMeasure"]           = new() { Code = 0x7D, FuncCode = 0x05, Description = "已作废" },//已作废：协议文档已删除本命令，停止测量改走 0x79 status=1（本表项仅留档，代码不再下发）
            ["CtrlGetStaticPress"]          = new() { Code = 0x7E, FuncCode = 0x05, Description = "读取当前静态值" },//实测静态压力回读用 0x76，本命令未启用
            ["CtrlSetOverPressureThreshold"]= new() { Code = 0x7F, FuncCode = 0x05, Description = "写过压门限" },
            ["CtrlGetOverPressureStatus"]   = new() { Code = 0x80, FuncCode = 0x05, Description = "读过压状态" },//s_test_get_over_pressure_state（无参数，返回 2 字节 uint16：0=无过压 1=过压；实时状态，泄压完成后回落 0）
            ["CtrlStartSpo2"]               = new() { Code = 0x81, FuncCode = 0x05, Description = "写开始/停止血氧测量" },//参数 0开始/1停止
            ["CtrlGetSpo2Value"]            = new() { Code = 0x82, FuncCode = 0x05, Description = "读取当前血氧值" },
            ["CtrlSetSpo2AlarmThreshold"]   = new() { Code = 0x83, FuncCode = 0x05, Description = "写血氧报警阈值" },
            ["CtrlGetSpo2AlarmStatus"]      = new() { Code = 0x84, FuncCode = 0x05, Description = "读取当前血氧浓度报警状态" },
            ["CtrlGetPulseRate"]            = new() { Code = 0x85, FuncCode = 0x05, Description = "读取当前脉搏频率" },
            ["CtrlGetWeakPerfusionAlarm"]   = new() { Code = 0x86, FuncCode = 0x05, Description = "读取弱灌注告警状态" },
            ["CtrlGetWeakPerfusionIndex"]   = new() { Code = 0x87, FuncCode = 0x05, Description = "读取弱灌注指数" },
            ["CtrlGetLeadState"]            = new() { Code = 0x88, FuncCode = 0x05, Description = "读取心电导联状态" },
            ["CtrlGetChargingStatus5"]      = new() { Code = 0x89, FuncCode = 0x05, Description = "读取充电状态" },//0x05/0x89
            ["CtrlGetAccelTilt"]            = new() { Code = 0x8A, FuncCode = 0x05, Description = "读取加速度倾斜度" },
            ["CtrlGetSpeakerVolume"]        = new() { Code = 0x8C, FuncCode = 0x05, Description = "读取喇叭音量" },

            // ---- 主动推送（产品→上位机，仅用于识别收到的帧）----
            // 心电波形上报：实机抓包证实走 Func=0x05 / Ctrl=0x8D（帧 EF 98 05 8D + 150字节 = 75×int16 小端 + CRC + FE，
            // 码值×0.01 = mV，250SPS 即 300ms/帧）；协议文档旧值 0x06/0x10 见下行注释，实机对不上时换回即可。
            ["CtrlPushEcgWave"]             = new() { Code = 0x8D, FuncCode = 0x05, Description = "上报心电电压值" },//s_test_report_ecg_wave（产品主动推送，实机证实 0x05/0x8D；备用文档值 0x06/0x10）
            ["CtrlPushEcgHr"]               = new() { Code = 0x11, FuncCode = 0x06, Description = "上报ECG测量心率数据" },//产品主动推送
            ["CtrlPushPpgHr"]               = new() { Code = 0x12, FuncCode = 0x06, Description = "上报PPG心率数据" },//产品主动推送
            ["CtrlPushPpgSpo2"]             = new() { Code = 0x13, FuncCode = 0x06, Description = "上报PPG测量血氧数据" },//产品主动推送

            // ---- 硬件控制/产测（Func=0x07）----
            ["CtrlSetSampleRate"]           = new() { Code = 0x62, FuncCode = 0x07, Description = "设置采样频率/上传间隔" },
            ["CtrlStartAccelSensor"]        = new() { Code = 0x60, FuncCode = 0x07, Description = "启动加速度传感器" },//产品随后以 0x07/0x60 持续推采样帧：uint32 序号 + int16 X/Y/Z（mg）+ 2 字节保留 + uint32 时标
            ["CtrlStopAccelSensor"]         = new() { Code = 0x61, FuncCode = 0x07, Description = "关闭加速度传感器" },// 停止 0x60 采样推送
            ["CtrlStartPressureSensor"]     = new() { Code = 0x65, FuncCode = 0x07, Description = "设置启动压力传感器" },//开启压力静态测试
            ["CtrlCalibratePressureSensor"] = new() { Code = 0x66, FuncCode = 0x07, Description = "校准压力传感器" },
            ["CtrlClosePressureSensor"]     = new() { Code = 0x67, FuncCode = 0x07, Description = "设置关闭压力传感器" },
            ["CtrlReadPressureKb"]          = new() { Code = 0xC3, FuncCode = 0x07, Description = "读取压力校准K/B值" },//无参帧 EF 02 07 C3 D7 51 FE；应答参数区 float K + float B（小端）
            ["CtrlStartPumpTest"]           = new() { Code = 0x68, FuncCode = 0x07, Description = "启动充气泵测试" },
            ["CtrlStopPumpTest"]            = new() { Code = 0x69, FuncCode = 0x07, Description = "关闭充气泵测试" },
            ["CtrlOpenValveTest"]           = new() { Code = 0x6A, FuncCode = 0x07, Description = "设置开泄气阀测试" },
            ["CtrlCloseValveTest"]          = new() { Code = 0x6B, FuncCode = 0x07, Description = "设置关闭泄气阀测试" },
            ["CtrlGetBatteryInfo"]          = new() { Code = 0x6C, FuncCode = 0x07, Description = "查询电池电量" },//电压+百分比
            ["CtrlGetChargingStatus"]       = new() { Code = 0x6D, FuncCode = 0x07, Description = "查询当前是否处于充电状态" },
            ["CtrlSoftPowerOff"]            = new() { Code = 0x6E, FuncCode = 0x07, Description = "设置进入软关机" },
            ["CtrlPlay1KHzSine"]            = new() { Code = 0x70, FuncCode = 0x07, Description = "播放1KHz正弦波" },
            ["CtrlStopAudioPlay"]           = new() { Code = 0x71, FuncCode = 0x07, Description = "停止音频播放" },
            ["CtrlStartEcgPpgMeasure"]      = new() { Code = 0x80, FuncCode = 0x07, Description = "启动ECG+PPG测量" },//持续30秒
            ["CtrlStopEcgPpgMeasure"]       = new() { Code = 0x81, FuncCode = 0x07, Description = "停止ECG+PPG测量" },
            ["CtrlGetEcgPpgResult"]         = new() { Code = 0x82, FuncCode = 0x07, Description = "获取ECG/PPG测量结果" },
            ["CtrlStartFullBpMeasure"]      = new() { Code = 0xCA, FuncCode = 0x07, Description = "产测态开始一次完整血压测量" },//无参帧 EF 02 07 CA 17 57 FE；产品自行充放气，期间周期推 0xCA 进程帧带 2 字节当前压力，测完推 10 字节结果帧：状态码/收缩压/舒张压/平均压/心率各 uint16 LE
            ["CtrlStopFullBpMeasure"]       = new() { Code = 0xCC, FuncCode = 0x07, Description = "产测态结束一次完整血压测量" },//无参帧 EF 02 07 CC 97 55 FE；每轮测完或超时中止都必须发一次，否则下一次开始不被接受
            ["CtrlFactoryReset"]            = new() { Code = 0xB0, FuncCode = 0x07, Description = "恢复出厂设置" },
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
                        _log.Warn($"协议命令配置缺少键 '{key}'，使用默认值 Func=0x{Defaults[key].FuncCode:X2}/Code=0x{Defaults[key].Code:X2}");
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
