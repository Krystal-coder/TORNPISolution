using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Communication
{
    /// <summary>
    /// 串口通讯协议（二进制帧）：
    ///   帧结构 = [Head 0xEF] [Len] [Payload...] [CRC16-MODBUS 2字节] [Tail 0xFE]
    ///   Payload = [功能码] [控制码 1字节] [参数 N字节]
    ///   Len     = Payload 长度（不含 CRC / Tail）
    ///   CRC     = CRC-16/MODBUS，计算范围 = Head + Len + Payload（从帧头到载荷末尾）
    ///   总帧长  = Payload.Length + 5（Head 1 + Len 1 + CRC 2 + Tail 1）
    /// ProductProtocol只做帧编解码、不控制串口；串口号与通讯参数取仪器配置 Product 段（SerialLinkSettings），收发在 ProductController。
    /// 功能码按命令区分：0x02 产品信息查询 / 0x05 测量控制 / 0x06 主动推送 / 0x07 硬件控制与产测。
    /// 控制码与功能码均从 Config\protocol_commands.json 加载，支持运行时修改后热重载。
    /// </summary>
    public static class ProductProtocol
    {
        // ==================== 命令码运行时字典 ====================

        /// <summary>命令名 → (FuncCode, Code)。键为命令名，避免同 Code 不同 Func 的冲突。</summary>
        private static readonly Dictionary<string, ProductCmd> _commands = new();

        /// <summary>命令名 → 默认参数字节（取配置项 Data）；只给同 Func+Ctrl 靠参数区分语义的命令，无参命令不入表。</summary>
        private static readonly Dictionary<string, byte[]> _commandData = new();
        private static readonly object _lock = new();

        /// <summary>已告警过的缺失命令名</summary>
        private static readonly HashSet<string> _warnedMissingNames = new();

        private static readonly ILogService _log = new LogService(nameof(ProductProtocol));

        static ProductProtocol()
        {
            LoadFromConfig();
        }

        private static void LoadFromConfig()
        {
            var service = new ProtocolCommandConfigService();
            var entries = service.Load();
            lock (_lock)
            {
                _commands.Clear();
                _commandData.Clear();
                foreach (var kvp in entries)
                {
                    _commands[kvp.Key] = new ProductCmd(kvp.Value.FuncCode, kvp.Value.Code);
                    var data = ParseHexBytes(kvp.Value.Data);
                    if (data != null)
                        _commandData[kvp.Key] = data;
                }
            }
        }

        /// <summary>解析 HEX 参数文本（空格/短横/逗号分隔）；空、非法或无字节返回 null（由调用方回退代码默认值）。</summary>
        private static byte[]? ParseHexBytes(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var tokens = text.Split(new[] { ' ', '-', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte>(tokens.Length);
            try
            {
                foreach (var t in tokens)
                    bytes.Add(Convert.ToByte(t, 16));
            }
            catch (FormatException)
            {
                return null;
            }
            return bytes.Count > 0 ? bytes.ToArray() : null;
        }

        /// <summary>重新从 JSON 加载命令码（配置窗口保存后调用）</summary>
        public static void ReloadCommands() => LoadFromConfig();

        /// <summary>按命令名取 (Func, Code)；未配置时返回 Func=0x07/Code=0x00 的无效命令，每个名字只告警一次。</summary>
        private static ProductCmd GetCmd(string name)
        {
            bool warn;
            lock (_lock)
            {
                if (_commands.TryGetValue(name, out var cmd)) return cmd;
                warn = _warnedMissingNames.Add(name);
            }

            // 日志写在锁外：Serilog 落盘是 IO，压在 _lock 里会拖住整条收发链路
            if (warn)
                _log.Warn($"协议命令 '{name}' 在配置与默认值中都不存在，已退化为无效命令 0x{FuncRead:X2}/0x00，" +
                          "请在协议命令配置页补齐该命令或检查命令名拼写");

            return new ProductCmd(FuncRead, 0x00);
        }

        /// <summary>
        /// 按命令名取配置页里的默认参数字节；未配置或配置为空时用 <paramref name="fallback"/>。
        /// </summary>
        public static byte[] ResolveCmdData(string name, byte[] fallback)
        {
            lock (_lock)
                return _commandData.TryGetValue(name, out var data) && data.Length > 0 ? data : fallback;
        }

        // ==================== 帧常量 ====================

        public const byte Head = 0xEF;
        public const byte Tail = 0xFE;

        // ==================== 功能码 ====================

        /// <summary>产品信息查询（0x30~0x37）</summary>
        public const byte FuncProductInfo = 0x02;

        /// <summary>测量控制（0x72~0x8C）</summary>
        public const byte FuncMeasure = 0x05;

        /// <summary>产品主动推送（0x10~0x13）</summary>
        public const byte FuncPush = 0x06;

        /// <summary>硬件控制与产测（0x62~0xB0），亦为缺省功能码</summary>
        public const byte FuncRead = 0x07;

        // ==================== 命令（从 JSON 加载，每项含 Func + Code） ====================

        // ---- 进入产测模式（Func=0x07/Code=0x01）：打开串口后立即发送防产品休眠 ----
        // 配置键 CtrlKeepAlive 不得改名：改名会让现场已保存的自定义码变成未知键被忽略。
        public static ProductCmd EnterProductionTestMode => GetCmd("CtrlKeepAlive");

        // ---- 产品信息查询（Func=0x02）----
        public static ProductCmd GetProductTime   => GetCmd("CtrlGetProductTime");
        public static ProductCmd GetAllUserParams => GetCmd("CtrlGetAllUserParams");
        public static ProductCmd GetUserCount     => GetCmd("CtrlGetUserCount");
        public static ProductCmd GetProductConfig => GetCmd("CtrlGetProductConfig");
        public static ProductCmd GetProductStatus => GetCmd("CtrlGetProductStatus");
        public static ProductCmd GetDisconnectInfo=> GetCmd("CtrlGetDisconnectInfo");
        public static ProductCmd GetResetInfo     => GetCmd("CtrlGetResetInfo");
        public static ProductCmd GetVersionInfo   => GetCmd("CtrlGetVersionInfo");

        // ---- 测量控制（Func=0x05）----
        public static ProductCmd GetVersion              => GetCmd("CtrlGetVersion");
        public static ProductCmd SetMode                 => GetCmd("CtrlSetMode");
        public static ProductCmd GetState                => GetCmd("CtrlGetState");
        public static ProductCmd StartInflate            => GetCmd("CtrlStartInflate");
        public static ProductCmd GetPressure             => GetCmd("CtrlGetPressure");
        public static ProductCmd StartDeflate            => GetCmd("CtrlStartDeflate");
        public static ProductCmd InflateStop             => GetCmd("CtrlInflateStop");
        public static ProductCmd StartBpMeasure          => GetCmd("CtrlStartBpMeasure");
        public static ProductCmd GetBpValue              => GetCmd("CtrlGetBpValue");
        public static ProductCmd GetBpResult             => GetCmd("CtrlGetBpResult");
        public static ProductCmd GetHrValue              => GetCmd("CtrlGetHrValue");
        public static ProductCmd StopBpMeasure           => GetCmd("CtrlStopBpMeasure");
        public static ProductCmd GetStaticPress          => GetCmd("CtrlGetStaticPress");
        public static ProductCmd SetOverPressureThreshold=> GetCmd("CtrlSetOverPressureThreshold");
        public static ProductCmd GetOverPressureStatus   => GetCmd("CtrlGetOverPressureStatus");
        public static ProductCmd StartSpo2               => GetCmd("CtrlStartSpo2");
        public static ProductCmd GetSpo2Value            => GetCmd("CtrlGetSpo2Value");
        public static ProductCmd SetSpo2AlarmThreshold   => GetCmd("CtrlSetSpo2AlarmThreshold");
        public static ProductCmd GetSpo2AlarmStatus      => GetCmd("CtrlGetSpo2AlarmStatus");
        public static ProductCmd GetPulseRate            => GetCmd("CtrlGetPulseRate");
        public static ProductCmd GetWeakPerfusionAlarm   => GetCmd("CtrlGetWeakPerfusionAlarm");
        public static ProductCmd GetWeakPerfusionIndex   => GetCmd("CtrlGetWeakPerfusionIndex");
        public static ProductCmd GetLeadState            => GetCmd("CtrlGetLeadState");
        public static ProductCmd GetChargingStatus5      => GetCmd("CtrlGetChargingStatus5");
        public static ProductCmd GetAccelTilt            => GetCmd("CtrlGetAccelTilt");
        public static ProductCmd GetSpeakerVolume        => GetCmd("CtrlGetSpeakerVolume");

        // ---- 开始/关闭ECG测试（Func=0x05/Ctrl=0x73，与 CtrlSetMode 同码，参数字节见配置页 Data 列）----
        // A10 ECG-009 只靠这两条控制产品推送：开始 → 产品持续推 0x8D，关闭 → 停止推送
        public static ProductCmd StartEcgTest           => GetCmd("CtrlStartEcgTest");
        public static ProductCmd StopEcgTest            => GetCmd("CtrlStopEcgTest");
        public const string StartEcgTestKey = "CtrlStartEcgTest";
        public const string StopEcgTestKey  = "CtrlStopEcgTest";

        // ---- 主动推送（产品→上位机，仅用于识别收到的帧）----
        // 心电波形实机走 Func=0x05/Ctrl=0x8D（见 protocol_commands.json），其余推送项仍按文档 Func=0x06 登记
        public static ProductCmd PushEcgWave  => GetCmd("CtrlPushEcgWave");
        public static ProductCmd PushEcgHr    => GetCmd("CtrlPushEcgHr");
        public static ProductCmd PushPpgHr    => GetCmd("CtrlPushPpgHr");
        public static ProductCmd PushPpgSpo2  => GetCmd("CtrlPushPpgSpo2");

        // ---- 硬件控制/产测（Func=0x07）----
        public static ProductCmd SetSampleRate          => GetCmd("CtrlSetSampleRate");
        // 加速度传感器启停 + 退出产测：产测态与采集帧都走 0x07（实机由独立工装程序证实）
        public static ProductCmd StartAccelSensor       => GetCmd("CtrlStartAccelSensor");
        public static ProductCmd StopAccelSensor        => GetCmd("CtrlStopAccelSensor");
        public static ProductCmd ExitProductionTestMode => GetCmd("CtrlExitProductionTestMode");
        public static ProductCmd StartPressureSensor    => GetCmd("CtrlStartPressureSensor");
        public static ProductCmd CalibratePressureSensor=> GetCmd("CtrlCalibratePressureSensor");
        public static ProductCmd ClosePressureSensor    => GetCmd("CtrlClosePressureSensor");
        /// <summary>读取压力校准 K/B（Func=0x07/Code=0xC3，无参帧 EF 02 07 C3 D7 51 FE）：校准写完回读校验，应答参数区 = float K + float B（小端）</summary>
        public static ProductCmd ReadPressureKb         => GetCmd("CtrlReadPressureKb");
        public static ProductCmd StartPumpTest          => GetCmd("CtrlStartPumpTest");
        public static ProductCmd StopPumpTest           => GetCmd("CtrlStopPumpTest");
        public static ProductCmd OpenValveTest          => GetCmd("CtrlOpenValveTest");
        public static ProductCmd CloseValveTest         => GetCmd("CtrlCloseValveTest");
        public static ProductCmd GetBatteryInfo         => GetCmd("CtrlGetBatteryInfo");
        public static ProductCmd GetChargingStatus      => GetCmd("CtrlGetChargingStatus");
        public static ProductCmd SoftPowerOff           => GetCmd("CtrlSoftPowerOff");
        public static ProductCmd Play1KHzSine           => GetCmd("CtrlPlay1KHzSine");
        public static ProductCmd StopAudioPlay          => GetCmd("CtrlStopAudioPlay");
        public static ProductCmd StartEcgPpgMeasure     => GetCmd("CtrlStartEcgPpgMeasure");
        public static ProductCmd StopEcgPpgMeasure      => GetCmd("CtrlStopEcgPpgMeasure");
        public static ProductCmd GetEcgPpgResult        => GetCmd("CtrlGetEcgPpgResult");
        // 产测态完整血压测量（实机抓包证实走 0x07，不是 0x05/0x79）：开始=0xCA、结束=0xCC；
        // 测量期间产品还以同一 Ctrl=0xCA 周期推进程帧（2 字节当前压力），测完推 10 字节结果帧，收帧按参数区长度区分
        public static ProductCmd StartFullBpMeasure     => GetCmd("CtrlStartFullBpMeasure");
        public static ProductCmd StopFullBpMeasure      => GetCmd("CtrlStopFullBpMeasure");
        public static ProductCmd FactoryReset           => GetCmd("CtrlFactoryReset");

        /// <summary>0xCA 结果帧参数区长度（字节）：状态码/收缩压/舒张压/平均压/心率 各 uint16 LE；进程推送帧参数区只有 2 字节压力，按长度区分</summary>
        public const int BpResultPayloadBytes = 10;

        // ==================== 测试模式枚举（0x73 参数） ====================

        public const byte ModeIdle     = 0;
        public const byte ModeCuffTest = 1;   // 袖带充放气模式
        public const byte ModeBpTest   = 2;   // 血压测试（BP-004/005）
        public const byte ModeStatic   = 3;   // 静态压力检测（BP-003）
        public const byte ModeSpo2     = 4;   // 血氧测试
        public const byte ModeEcg      = 5;   // 心电测试（ECG-001~012）
        public const byte ModeAccel    = 6;   // 加速度测试

        // ==================== 心电波形上报（0x8D s_test_report_ecg_wave）====================

        /// <summary>心电波形采样率（SPS）：实机 250 点/秒（设置采样率命令 0x62 未启用，按文档固定值推算时间轴）</summary>
        public const int EcgSampleRate = 250;

        /// <summary>单帧上报数据长度（字节）：文档“上报数据长度 150” = 75 点 × 2 字节</summary>
        public const int EcgWavePayloadBytes = 150;

        /// <summary>单帧采样点数：75（= 300ms @ 250SPS）</summary>
        public const int EcgWaveSamplesPerFrame = EcgWavePayloadBytes / 2;

        /// <summary>
        /// 解析 0x8D 帧的心电电压数据：payload = 75 × int16 小端，码值 /100 = mV（1 码值 = 10μV）。
        /// 文档写 uint16_t s_ecg_wave_cache[75]，但实机基线为 F8 FF（按 uint16 明显不成立），故按 int16 补码解析（负值 = 基线以下）。
        /// </summary>
        /// <param name="payload">帧数据区（不含 Func/Ctrl/CRC）</param>
        /// <returns>mV 数组；payload 不足 2 字节返回 null</returns>
        public static double[]? ParseEcgWave(byte[] payload)
        {
            var count = payload.Length / 2;
            if (count == 0) return null;

            var millivolts = new double[count];
            for (var i = 0; i < count; i++)
                millivolts[i] = (short)ReadUInt16LE(payload, i * 2) / 100.0;
            return millivolts;
        }

        /// <summary>成对帧式一个采样点占的字节数：ADC 4 + 电压码值 4（均 int32 小端）</summary>
        public const int EcgWavePairBytes = 8;

        /// <summary>成对帧式的采样率（SPS）：实机 500 点/秒，与上面文档口径的 250 并存，采集页按这条走</summary>
        public const int EcgWavePairSampleRate = 500;

        /// <summary>
        /// 解析 0x8D 的成对帧式：参数区 = N × (int32 LE ADC + int32 LE 电压码值)，电压码值 /100 = mV（1 码值 = 10μV）。
        /// 不足一组的尾字节丢弃。
        /// </summary>
        /// <param name="payload">帧数据区（不含 Func/Ctrl/CRC）</param>
        /// <returns>按到达顺序排列的采样点；参数区凑不出一组时返回 null</returns>
        public static EcgWavePoint[]? ParseEcgWavePairs(byte[] payload)
        {
            if (payload == null) return null;

            var count = payload.Length / EcgWavePairBytes;
            if (count == 0) return null;

            var points = new EcgWavePoint[count];
            for (var i = 0; i < count; i++)
                points[i] = new EcgWavePoint(
                    unchecked((int)ReadUInt32LE(payload, i * EcgWavePairBytes)),
                    unchecked((int)ReadUInt32LE(payload, i * EcgWavePairBytes + 4)) / 100.0);
            return points;
        }

        // ==================== 加速度采样上报（0x60）====================

        /// <summary>0x60 采样帧参数区最短长度：设备序号 4 + X/Y/Z 各 2 + 预留 2 + 设备时标 4（字节）</summary>
        public const int AccelSamplePayloadBytes = 16;

        /// <summary>
        /// 解析 0x60 采样推送帧：payload = uint32 LE 设备序号 + int16 LE X/Y/Z（mg）+ 2 字节未用 + uint32 LE 设备时标。
        /// 与同码的启动应答靠参数区长度区分（应答不足 <see cref="AccelSamplePayloadBytes"/>）。
        /// </summary>
        public static AccelSample? ParseAccelSample(byte[] payload, DateTime receivedAt)
        {
            if (payload == null || payload.Length < AccelSamplePayloadBytes) return null;

            return new AccelSample
            {
                DeviceSeq = ReadUInt32LE(payload, 0),
                X = (short)ReadUInt16LE(payload, 4),
                Y = (short)ReadUInt16LE(payload, 6),
                Z = (short)ReadUInt16LE(payload, 8),
                ReceivedAt = receivedAt,
            };
        }

        // ==================== 压力校准 K/B 回读（0x07 0xC3） ====================

        /// <summary>
        /// 解析 0x07/0xC3 应答参数区：2 × float 小端（BP_K、BP_B）。
        /// 实机样例 EF 0A 07 C3 66 66 08 C2 00 00 E0 C0 97 12 FE → K ≈ -34.1、B = -7.0。
        /// 出厂默认值见 SpecItems.PcalKbDefaultK / PcalKbDefaultB（参数区不足 8 字节返回 null）。
        /// </summary>
        public static (double K, double B)? ParsePressureKb(byte[]? payload)
        {
            if (payload == null || payload.Length < 8) return null;
            return (ReadSingleLE(payload, 0), ReadSingleLE(payload, 4));
        }

        /// <summary>从 payload 按小端序读取 float（IEEE754 位型）</summary>
        public static float ReadSingleLE(byte[] payload, int offset)
            => BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt32LE(payload, offset)));

        // ==================== 包构建 ====================

        /// <param name="cmd">命令标识（功能码 + 控制码）</param>
        /// <param name="data">参数数据（可为 null 或空）</param>
        /// <exception cref="ArgumentException">参数过长导致 Payload 超过 Len 字节的表示范围（255）</exception>
        public static byte[] BuildPacket(ProductCmd cmd, byte[]? data = null)
        {
            var dataLen = data?.Length ?? 0;
            var payloadLen = 2 + dataLen;                    // Func + Ctrl + Data
            // Len 是 1 字节，写不下只能截断，截出来的帧会被产品按错误长度丢弃——宁可在这里就报错
            if (payloadLen > byte.MaxValue)
                throw new ArgumentException(
                    $"协议帧参数过长：Payload={payloadLen} 字节，超过 Len 上限 {byte.MaxValue}（参数区最多 {byte.MaxValue - 2} 字节）",
                    nameof(data));

            var frame = new byte[payloadLen + 5];            // Head + Len + Payload + CRC(2) + Tail

            frame[0] = Head;
            frame[1] = (byte)payloadLen;                     // Len = Payload 长度（不含 CRC）
            frame[2] = cmd.Func;
            frame[3] = cmd.Code;

            if (dataLen > 0)
                Array.Copy(data!, 0, frame, 4, dataLen);

            var crc = ComputeCrc16Modbus(frame, 0, 2 + payloadLen);   // CRC 范围：Head + Len + Payload（从帧头到载荷末尾）
            var crcIdx = 4 + dataLen;                                 // CRC 起始位置 = Head + Len + Func + Ctrl + Data
            frame[crcIdx]     = (byte)(crc & 0xFF);                   // CRC 低字节
            frame[crcIdx + 1] = (byte)((crc >> 8) & 0xFF);            // CRC 高字节
            frame[crcIdx + 2] = Tail;

            return frame;
        }

        // ==================== 帧解析 ====================

        /// <param name="frame">完整帧（含 Head 和 Tail）</param>
        /// <returns>解析结果；CRC 校验失败或帧无效时返回 null</returns>
        public static ParsedFrame? ParseFrame(byte[] frame)
        {
            if (frame == null || frame.Length < 6) return null;
            if (frame[0] != Head) return null;

            var len = frame[1];                                       // Len = Payload 长度（不含 CRC）
            if (frame.Length < len + 5) return null;                  // 总长 = Head + Len + Payload + CRC + Tail
            if (frame[len + 4] != Tail) return null;

            var funcCode = frame[2];
            var ctrlCode = frame[3];
            var dataLen = len - 2;                                    // 去掉功能码 + 控制码
            if (dataLen < 0) return null;                             // Len 异常（<2），非法帧
            var payload = new byte[dataLen];
            if (dataLen > 0)
                Array.Copy(frame, 4, payload, 0, dataLen);

            // CRC 校验：Head + Len + Payload（从帧头到载荷末尾）
            var expectedCrc = ComputeCrc16Modbus(frame, 0, 2 + len);
            var actualCrc = (ushort)(frame[len + 2] | (frame[len + 3] << 8));
            if (expectedCrc != actualCrc) return null;

            return new ParsedFrame(funcCode, ctrlCode, payload, frame);
        }

        /// <summary>解析后的协议帧；RawFrame 为收到的完整原始帧（含头尾与 CRC），供与抓包逐字节比对</summary>
        public record ParsedFrame(byte FunctionCode, byte ControlCode, byte[] Payload, byte[]? RawFrame = null);

        // ==================== CRC-16/MODBUS ====================

        /// <summary>CRC-16/MODBUS（多项式 0xA001，初始值 0xFFFF），与 ModbusRtuClient 同算法。</summary>
        /// <param name="length">从 offset 起参与计算的字节数</param>
        public static ushort ComputeCrc16Modbus(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;
            for (var i = offset; i < offset + length; i++)
            {
                crc ^= data[i];
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc >>= 1;
                }
            }
            return crc;
        }

        // ==================== 字节序辅助 ====================

        /// <summary>从 payload 按大端序读取 uint16（偏移 + 字节数）</summary>
        public static ushort ReadUInt16BE(byte[] payload, int offset = 0)
            => (ushort)((payload[offset] << 8) | payload[offset + 1]);

        /// <summary>从 payload 按大端序读取 uint32（偏移 + 字节数）</summary>
        public static uint ReadUInt32BE(byte[] payload, int offset = 0)
            => (uint)((payload[offset] << 24) | (payload[offset + 1] << 16)
                    | (payload[offset + 2] << 8) | payload[offset + 3]);

        /// <summary>将 uint16 写为大端序字节数组</summary>
        public static byte[] ToBytesBE(ushort value)
            => new[] { (byte)(value >> 8), (byte)(value & 0xFF) };

        /// <summary>从 payload 按小端序读取 uint16（偏移 + 字节数）</summary>
        public static ushort ReadUInt16LE(byte[] payload, int offset = 0)
            => (ushort)(payload[offset] | (payload[offset + 1] << 8));

        /// <summary>从 payload 按小端序读取 uint32（偏移 + 字节数）</summary>
        public static uint ReadUInt32LE(byte[] payload, int offset = 0)
            => (uint)(payload[offset] | (payload[offset + 1] << 8)
                    | (payload[offset + 2] << 16) | (payload[offset + 3] << 24));
    }
}
