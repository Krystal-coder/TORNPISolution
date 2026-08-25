using System.IO.Ports;
using TORNPISolution.Interface;

namespace TORNPISolution.Service.Communication
{
    /// <summary>
    /// Modbus RTU 主站客户端（RS485 串口）。
    /// 支持功能码：0x03 读保持寄存器、0x04 读输入寄存器、
    /// 0x06 写单寄存器、0x10 写多寄存器。
    /// 所有请求经 SemaphoreSlim 串行化（485 半双工总线一问一答），
    /// 并以异步方式执行，不阻塞调用线程。
    /// </summary>
    public class ModbusRtuClient : IDisposable
    {
        private readonly SerialPort _port;
        private readonly SemaphoreSlim _busLock = new(1, 1);
        private readonly ILogService _log;
        private readonly int _responseTimeoutMs;

        public ModbusRtuClient(
            string portName,
            int baudRate = 9600,
            Parity parity = Parity.None,
            StopBits stopBits = StopBits.One,
            int responseTimeoutMs = 1000,
            ILogService? log = null)
        {
            _log = log ?? new LogService(nameof(ModbusRtuClient));
            _responseTimeoutMs = responseTimeoutMs;
            _port = new SerialPort(portName, baudRate, parity, 8, stopBits)
            {
                ReadTimeout = responseTimeoutMs,
                WriteTimeout = responseTimeoutMs
            };
        }

        /// <summary>串口是否已打开</summary>
        public bool IsOpen => _port.IsOpen;

        /// <summary>打开串口</summary>
        public Task OpenAsync(CancellationToken ct = default) => Task.Run(() =>
        {
            if (!_port.IsOpen)
            {
                _port.Open();
                _log.Info($"串口 {_port.PortName} 已打开（{_port.BaudRate},{_port.Parity},{_port.StopBits}）");
            }
        }, ct);

        /// <summary>关闭串口</summary>
        public Task CloseAsync() => Task.Run(() =>
        {
            if (_port.IsOpen)
            {
                _port.Close();
                _log.Info($"串口 {_port.PortName} 已关闭");
            }
        });

        /// <summary>0x03 读保持寄存器</summary>
        public Task<ushort[]> ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken ct = default)
            => ReadRegistersAsync(slaveId, 0x03, startAddress, count, ct);

        /// <summary>0x04 读输入寄存器</summary>
        public Task<ushort[]> ReadInputRegistersAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken ct = default)
            => ReadRegistersAsync(slaveId, 0x04, startAddress, count, ct);

        /// <summary>0x06 写单个寄存器</summary>
        public Task WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value, CancellationToken ct = default)
        {
            var frame = new byte[8];
            frame[0] = slaveId;
            frame[1] = 0x06;
            frame[2] = (byte)(address >> 8);
            frame[3] = (byte)address;
            frame[4] = (byte)(value >> 8);
            frame[5] = (byte)value;
            AppendCrc(frame);

            // 响应为请求原样回显，共 8 字节
            return TransceiveAsync(frame, 8, ct);
        }

        /// <summary>0x10 写多个寄存器</summary>
        public Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, ushort[] values, CancellationToken ct = default)
        {
            var byteCount = (byte)(values.Length * 2);
            var frame = new byte[9 + byteCount];
            frame[0] = slaveId;
            frame[1] = 0x10;
            frame[2] = (byte)(startAddress >> 8);
            frame[3] = (byte)startAddress;
            frame[4] = (byte)(values.Length >> 8);
            frame[5] = (byte)values.Length;
            frame[6] = byteCount;
            for (var i = 0; i < values.Length; i++)
            {
                frame[7 + i * 2] = (byte)(values[i] >> 8);
                frame[8 + i * 2] = (byte)values[i];
            }
            AppendCrc(frame);

            // 响应：从站号 + 功能码 + 起始地址 + 数量 + CRC = 8 字节
            return TransceiveAsync(frame, 8, ct);
        }

        private async Task<ushort[]> ReadRegistersAsync(byte slaveId, byte function, ushort startAddress, ushort count, CancellationToken ct)
        {
            var frame = new byte[8];
            frame[0] = slaveId;
            frame[1] = function;
            frame[2] = (byte)(startAddress >> 8);
            frame[3] = (byte)startAddress;
            frame[4] = (byte)(count >> 8);
            frame[5] = (byte)count;
            AppendCrc(frame);

            // 响应：从站号 + 功能码 + 字节数 + 数据 + CRC
            var response = await TransceiveAsync(frame, 5 + count * 2, ct).ConfigureAwait(false);

            var registers = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                registers[i] = (ushort)((response[3 + i * 2] << 8) | response[4 + i * 2]);
            }
            return registers;
        }

        /// <summary>发送请求帧并接收指定长度的响应帧（总线级串行化）</summary>
        private async Task<byte[]> TransceiveAsync(byte[] request, int expectedLength, CancellationToken ct)
        {
            await _busLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    if (!_port.IsOpen)
                    {
                        throw new InvalidOperationException($"串口 {_port.PortName} 未打开");
                    }

                    _port.DiscardInBuffer();
                    _port.Write(request, 0, request.Length);

                    var buffer = new byte[expectedLength];
                    var received = 0;
                    var deadline = Environment.TickCount64 + _responseTimeoutMs;

                    while (received < expectedLength)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (Environment.TickCount64 > deadline)
                        {
                            throw new TimeoutException(
                                $"Modbus 响应超时（从站 {request[0]}，功能码 0x{request[1]:X2}，已收 {received}/{expectedLength} 字节）");
                        }

                        if (_port.BytesToRead > 0)
                        {
                            received += _port.Read(buffer, received, expectedLength - received);

                            // 从站返回异常帧（功能码最高位置 1），长度固定 5 字节
                            if (received >= 5 && (buffer[1] & 0x80) != 0)
                            {
                                var code = buffer[2];
                                throw new IOException($"Modbus 异常响应：功能码 0x{buffer[1]:X2}，异常码 0x{code:X2}");
                            }
                        }
                        else
                        {
                            Thread.Sleep(2);
                        }
                    }

                    if (!VerifyCrc(buffer))
                    {
                        throw new IOException("Modbus 响应 CRC 校验失败");
                    }

                    return buffer;
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error($"Modbus 通讯失败（从站 {request[0]}，功能码 0x{request[1]:X2}）", ex);
                throw;
            }
            finally
            {
                _busLock.Release();
            }
        }

        #region CRC16 (Modbus)

        private static void AppendCrc(byte[] frame)
        {
            var crc = ComputeCrc(frame, frame.Length - 2);
            frame[^2] = (byte)crc;          // CRC 低字节在前
            frame[^1] = (byte)(crc >> 8);
        }

        private static bool VerifyCrc(byte[] frame)
        {
            var crc = ComputeCrc(frame, frame.Length - 2);
            return frame[^2] == (byte)crc && frame[^1] == (byte)(crc >> 8);
        }

        private static ushort ComputeCrc(byte[] data, int length)
        {
            ushort crc = 0xFFFF;
            for (var i = 0; i < length; i++)
            {
                crc ^= data[i];
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            return crc;
        }

        #endregion

        #region 浮点/寄存器转换辅助

        /// <summary>两个寄存器转 float：高字在前（大端字序，Alicat 使用）</summary>
        public static float ToFloatHighWordFirst(ushort high, ushort low)
        {
            var bits = ((uint)high << 16) | low;
            return BitConverter.UInt32BitsToSingle(bits);
        }

        /// <summary>两个寄存器转 float：低字在前（美仪变送器使用，如 0x0000 0x4080 = 4.0f）</summary>
        public static float ToFloatLowWordFirst(ushort first, ushort second)
            => ToFloatHighWordFirst(second, first);

        /// <summary>float 转两个寄存器：高字在前（Alicat 使用）</summary>
        public static ushort[] FromFloatHighWordFirst(float value)
        {
            var bits = BitConverter.SingleToUInt32Bits(value);
            return new[] { (ushort)(bits >> 16), (ushort)bits };
        }

        /// <summary>float 转两个寄存器：低字在前（美仪变送器使用）</summary>
        public static ushort[] FromFloatLowWordFirst(float value)
        {
            var regs = FromFloatHighWordFirst(value);
            return new[] { regs[1], regs[0] };
        }

        /// <summary>32 位整数转两个寄存器：低字在前（美仪变送器使用）</summary>
        public static ushort[] FromInt32LowWordFirst(int value)
            => new[] { (ushort)value, (ushort)((uint)value >> 16) };

        #endregion

        public void Dispose()
        {
            _busLock.Dispose();
            if (_port.IsOpen)
            {
                _port.Close();
            }
            _port.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
