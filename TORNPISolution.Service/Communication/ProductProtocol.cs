namespace TORNPISolution.Service.Communication
{
    /// <summary>
    /// 新产品串口通讯协议（二进制帧）：
    ///   帧结构 = [Head 0xEF] [Len] [Payload...] [CRC16-MODBUS 2字节] [Tail 0xFE]
    ///   Payload = [功能码 1字节] [控制码 1字节] [参数 N字节]
    ///   Len     = Payload 长度 + CRC 2 字节（不含 Head / Len / Tail）
    ///   CRC     = CRC-16/MODBUS，计算范围 = Len + Payload（即 Head 之后、CRC 之前的全部字节）
    ///   总帧长  = Payload.Length + 5
    /// 串口参数：115200 8N1，无流控。
    /// </summary>
    public static class ProductProtocol
    {
        // ==================== 帧常量 ====================

        public const byte Head = 0xEF;
        public const byte Tail = 0xFE;

        // ==================== 功能码 ====================

        /// <summary>ACK 应答（写命令返回）</summary>
        public const byte FuncAck = 0x00;

        /// <summary>读命令响应</summary>
        public const byte FuncReadResponse = 0x05;

        // ==================== 控制码 ====================

        public const byte CtrlGetVersion     = 0x72;  // 读版本/MAC
        public const byte CtrlSetMode        = 0x73;  // 设置测试模式
        public const byte CtrlGetState       = 0x74;  // 读测试状态
        public const byte CtrlStartInflate   = 0x75;  // 开始充气
        public const byte CtrlGetPressure    = 0x76;  // 读当前压力值
        public const byte CtrlStartDeflate   = 0x77;  // 开始放气
        public const byte CtrlInflateStop    = 0x78;  // 充放气停止
        public const byte CtrlStartBpMeasure = 0x79;  // 开始血压测量
        public const byte CtrlGetBpValue     = 0x7A;  // 读瞬时血压值
        public const byte CtrlGetBpResult    = 0x7B;  // 读血压测量最终结果 / 测量完成主动推送
        public const byte CtrlGetHrValue     = 0x7C;  // 读心率值
        public const byte CtrlStopBpMeasure  = 0x7D;  // 停止血压测量
        public const byte CtrlGetStaticPress = 0x7E;  // 读静态压力值
        public const byte CtrlStartSpo2      = 0x80;  // 开始/停止血氧测量
        public const byte CtrlGetSpo2Value   = 0x81;  // 读血氧值
        public const byte CtrlGetLeadState   = 0x87;  // 读导联状态

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
        /// </summary>
        /// <param name="controlCode">控制码</param>
        /// <param name="data">参数数据（可为 null 或空）</param>
        public static byte[] BuildPacket(byte controlCode, byte[]? data = null)
        {
            var payloadLen = 2 + (data?.Length ?? 0);   // 功能码 + 控制码 + 参数
            var frame = new byte[payloadLen + 5];        // Head + Len + Payload + CRC(2) + Tail

            frame[0] = Head;
            frame[1] = (byte)(payloadLen + 2);           // Len = Payload + CRC
            frame[2] = FuncReadResponse;                  // 功能码（上位机发送统一用 0x05）
            frame[3] = controlCode;

            if (data != null && data.Length > 0)
                Array.Copy(data, 0, frame, 4, data.Length);

            var crc = ComputeCrc16Modbus(frame, 1, payloadLen + 1);   // 从 Len 开始算到 Payload 末尾
            var crcIdx = 4 + (data?.Length ?? 0);                      // CRC 起始位置 = Head + Len + Func + Ctrl + Data
            frame[crcIdx]     = (byte)(crc & 0xFF);                    // CRC 低字节
            frame[crcIdx + 1] = (byte)((crc >> 8) & 0xFF);             // CRC 高字节
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

            var len = frame[1];                                       // Len = Payload + CRC
            if (frame.Length < len + 3) return null;                  // 总长 = Head + Len + Payload + CRC + Tail
            if (frame[len + 2] != Tail) return null;

            var funcCode = frame[2];
            var ctrlCode = frame[3];
            var dataLen = len - 2;                                    // 去掉功能码 + 控制码
            var payload = new byte[dataLen];
            if (dataLen > 0)
                Array.Copy(frame, 4, payload, 0, dataLen);

            // CRC 校验：从 Len 到 Payload 末尾
            var expectedCrc = ComputeCrc16Modbus(frame, 1, len + 1);
            var actualCrc = (ushort)(frame[len] | (frame[len + 1] << 8));
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
    }
}
