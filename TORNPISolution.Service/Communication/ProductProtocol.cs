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
    /// 功能码按命令区分：大多数读命令为 0x07，充电状态（0x6D）为 0x06。
    /// 控制码从 Config\protocol_commands.json 加载，支持运行时修改后热重载。
    /// </summary>
    public static class ProductProtocol
    {
        // ==================== 命令码运行时字典 ====================

        private static readonly Dictionary<string, byte> _commandCodes;
        private static readonly Dictionary<byte, byte> _ctrlToFunc;
        private static readonly object _lock = new();

        static ProductProtocol()
        {
            var service = new ProtocolCommandConfigService();
            var entries = service.Load();
            _commandCodes = new Dictionary<string, byte>();
            _ctrlToFunc = new Dictionary<byte, byte>();
            foreach (var kvp in entries)
            {
                _commandCodes[kvp.Key] = kvp.Value.Code;
                _ctrlToFunc[kvp.Value.Code] = kvp.Value.FuncCode;
            }
        }

        /// <summary>重新从 JSON 加载命令码（配置窗口保存后调用）</summary>
        public static void ReloadCommands()
        {
            var service = new ProtocolCommandConfigService();
            var entries = service.Load();
            lock (_lock)
            {
                _commandCodes.Clear();
                _ctrlToFunc.Clear();
                foreach (var kvp in entries)
                {
                    _commandCodes[kvp.Key] = kvp.Value.Code;
                    _ctrlToFunc[kvp.Value.Code] = kvp.Value.FuncCode;
                }
            }
        }

        private static byte GetCmd(string name, byte defaultValue)
        {
            lock (_lock)
                return _commandCodes.TryGetValue(name, out var code) ? code : defaultValue;
        }

        private static byte GetFuncForCtrl(byte ctrlCode)
        {
            lock (_lock)
                return _ctrlToFunc.TryGetValue(ctrlCode, out var func) ? func : FuncRead;
        }

        // ==================== 帧常量 ====================

        public const byte Head = 0xEF;
        public const byte Tail = 0xFE;

        // ==================== 功能码 ====================

        /// <summary>ACK 应答（写命令返回）</summary>
        public const byte FuncAck = 0x00;

        /// <summary>读命令（上位机发送读请求统一用 0x07）</summary>
        public const byte FuncRead = 0x07;

        // ==================== 控制码（从 JSON 加载，缺省回退硬编码默认值） ====================

        public static byte CtrlKeepAlive     => GetCmd("CtrlKeepAlive", 0x01);     // 心跳保活（防止产品休眠）
        public static byte CtrlGetVersion     => GetCmd("CtrlGetVersion", 0x72);     // 读版本/MAC
        public static byte CtrlSetMode        => GetCmd("CtrlSetMode", 0x73);        // 设置测试模式
        public static byte CtrlGetState       => GetCmd("CtrlGetState", 0x74);       // 读测试状态
        public static byte CtrlStartInflate   => GetCmd("CtrlStartInflate", 0x75);   // 开始充气
        public static byte CtrlGetPressure    => GetCmd("CtrlGetPressure", 0x76);    // 读当前压力值
        public static byte CtrlStartDeflate   => GetCmd("CtrlStartDeflate", 0x77);   // 开始放气
        public static byte CtrlInflateStop    => GetCmd("CtrlInflateStop", 0x78);    // 充放气停止
        public static byte CtrlStartBpMeasure => GetCmd("CtrlStartBpMeasure", 0x79); // 开始血压测量
        public static byte CtrlGetBpValue     => GetCmd("CtrlGetBpValue", 0x7A);     // 读瞬时血压值
        public static byte CtrlGetBpResult    => GetCmd("CtrlGetBpResult", 0x7B);    // 读血压测量最终结果 / 测量完成主动推送
        public static byte CtrlGetHrValue     => GetCmd("CtrlGetHrValue", 0x7C);     // 读心率值
        public static byte CtrlStopBpMeasure  => GetCmd("CtrlStopBpMeasure", 0x7D);  // 停止血压测量
        public static byte CtrlGetStaticPress => GetCmd("CtrlGetStaticPress", 0x7E); // 读静态压力值
        public static byte CtrlStartSpo2      => GetCmd("CtrlStartSpo2", 0x80);      // 开始/停止血氧测量
        public static byte CtrlGetSpo2Value   => GetCmd("CtrlGetSpo2Value", 0x81);   // 读血氧值
        public static byte CtrlGetLeadState   => GetCmd("CtrlGetLeadState", 0x88);   // 读导联状态
        public static byte CtrlGetBatteryInfo  => GetCmd("CtrlGetBatteryInfo", 0x6C);  // 读电池信息（电压+百分比）
        public static byte CtrlGetChargingStatus => GetCmd("CtrlGetChargingStatus", 0x6D); // 读充电状态

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
        /// <param name="controlCode">控制码</param>
        /// <param name="data">参数数据（可为 null 或空）</param>
        public static byte[] BuildPacket(byte controlCode, byte[]? data = null)
        {
            var funcCode = GetFuncForCtrl(controlCode);
            var dataLen = data?.Length ?? 0;
            var payloadLen = 2 + dataLen;                    // Func + Ctrl + Data
            var frame = new byte[payloadLen + 5];            // Head + Len + Payload + CRC(2) + Tail

            frame[0] = Head;
            frame[1] = (byte)payloadLen;                     // Len = Payload 长度（不含 CRC）
            frame[2] = funcCode;
            frame[3] = controlCode;

            if (dataLen > 0)
                Array.Copy(data, 0, frame, 4, dataLen);

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
