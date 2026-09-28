using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Communication
{
    /// <summary>
    /// 新产品串口通讯协议（二进制帧）：
    ///   帧结构 = [Head 0xEF] [Len] [Payload...] [CRC16-MODBUS 2字节] [Tail 0xFE]
    ///   Payload = [功能码] [控制码 1字节] [参数 N字节]
    ///   Len     = Payload 长度（不含 CRC / Tail）
    ///   CRC     = CRC-16/MODBUS，计算范围 = Head + Len + Payload（从帧头到载荷末尾）
    ///   总帧长  = Payload.Length + 5（Head 1 + Len 1 + CRC 2 + Tail 1）
    /// 串口参数：115200 8N1，无流控。
    /// 功能码按命令区分：0x02 产品信息查询 / 0x05 测量控制 / 0x06 主动推送 / 0x07 硬件控制与产测。
    /// 控制码与功能码均从 Config\protocol_commands.json 加载，支持运行时修改后热重载。
    /// </summary>
    public static class ProductProtocol
    {
        // ==================== 命令码运行时字典 ====================

        /// <summary>命令名 → (FuncCode, Code)。键为命令名，天然避免同 Code 不同 Func 的冲突。</summary>
        private static readonly Dictionary<string, ProductCmd> _commands = new();
        private static readonly object _lock = new();

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
                foreach (var kvp in entries)
                    _commands[kvp.Key] = new ProductCmd(kvp.Value.FuncCode, kvp.Value.Code);
            }
        }

        /// <summary>重新从 JSON 加载命令码（配置窗口保存后调用）</summary>
        public static void ReloadCommands() => LoadFromConfig();

        /// <summary>按命令名取 (Func, Code)；未配置时返回 Func=0x07/Code=0x00 的无效命令并记录。</summary>
        private static ProductCmd GetCmd(string name)
        {
            lock (_lock)
                return _commands.TryGetValue(name, out var cmd) ? cmd : new ProductCmd(FuncRead, 0x00);
        }

        // ==================== 帧常量 ====================

        public const byte Head = 0xEF;
        public const byte Tail = 0xFE;

        // ==================== 功能码 ====================

        /// <summary>ACK 应答（写命令返回）</summary>
        public const byte FuncAck = 0x00;

        /// <summary>产品信息查询（0x30~0x37）</summary>
        public const byte FuncProductInfo = 0x02;

        /// <summary>测量控制（0x72~0x8C）</summary>
        public const byte FuncMeasure = 0x05;

        /// <summary>产品主动推送（0x10~0x13）</summary>
        public const byte FuncPush = 0x06;

        /// <summary>硬件控制与产测（0x62~0xB0），亦为缺省功能码</summary>
        public const byte FuncRead = 0x07;

        // ==================== 命令（从 JSON 加载，每项含 Func + Code） ====================

        // ---- 进入产测模式（Func=0x07/Code=0x01）：打开串口后立即发送防产品休眠，须在 0x72/0x74 之前 ----
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

        // ---- 主动推送（Func=0x06，产品→上位机，仅用于识别收到的帧）----
        public static ProductCmd PushEcgWave  => GetCmd("CtrlPushEcgWave");
        public static ProductCmd PushEcgHr    => GetCmd("CtrlPushEcgHr");
        public static ProductCmd PushPpgHr    => GetCmd("CtrlPushPpgHr");
        public static ProductCmd PushPpgSpo2  => GetCmd("CtrlPushPpgSpo2");

        // ---- 硬件控制/产测（Func=0x07）----
        public static ProductCmd SetSampleRate          => GetCmd("CtrlSetSampleRate");
        public static ProductCmd StartPressureSensor    => GetCmd("CtrlStartPressureSensor");
        public static ProductCmd CalibratePressureSensor=> GetCmd("CtrlCalibratePressureSensor");
        public static ProductCmd ClosePressureSensor    => GetCmd("CtrlClosePressureSensor");
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
        public static ProductCmd FactoryReset           => GetCmd("CtrlFactoryReset");

        // ==================== 测试模式枚举（0x73 参数） ====================

        public const byte ModeIdle     = 0;
        public const byte ModeCuffTest = 1;   // 袖带充放气测试（BP-001/002）
        public const byte ModeBpTest   = 2;   // 血压测试（BP-004/005）
        public const byte ModeStatic   = 3;   // 静态压力检测（BP-003）
        public const byte ModeSpo2     = 4;   // 血氧测试
        public const byte ModeEcg      = 5;   // 心电测试（ECG-001~012）
        public const byte ModeAccel    = 6;   // 加速度测试

        // ==================== 包构建 ====================

        /// <summary>
        /// 构建完整协议帧：Head + Len + Payload + CRC16-MODBUS + Tail。
        /// Len = Payload 长度（Func + Ctrl + Data），不含 CRC。
        /// 总帧长 = Payload.Length + 5（Head 1 + Len 1 + CRC 2 + Tail 1）。
        /// </summary>
        /// <param name="cmd">命令标识（功能码 + 控制码）</param>
        /// <param name="data">参数数据（可为 null 或空）</param>
        public static byte[] BuildPacket(ProductCmd cmd, byte[]? data = null)
        {
            var dataLen = data?.Length ?? 0;
            var payloadLen = 2 + dataLen;                    // Func + Ctrl + Data
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

        /// <summary>
        /// 解析一帧完整数据。
        /// </summary>
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

            return new ParsedFrame(funcCode, ctrlCode, payload);
        }

        /// <summary>解析后的协议帧</summary>
        public record ParsedFrame(byte FunctionCode, byte ControlCode, byte[] Payload);

        // ==================== CRC-16/MODBUS ====================

        /// <summary>
        /// CRC-16/MODBUS（多项式 0xA001，初始值 0xFFFF），
        /// 与 ModbusRtuClient 使用同一算法。
        /// </summary>
        /// <param name="data">数据源</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="length">计算长度</param>
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
