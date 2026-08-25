
using Serilog;

namespace TORNPISolution.GoogolEtherCAT.GEN
{
    /// <summary>
    /// 固高刀锋IO模块操作类
    /// </summary>
    public class GENControl : IDisposable
    {
        #region 常量与私有成员（线程隔离+线程安全）
        private const short _core = 1;
        private const ushort _slave = 0;
        private readonly object _lockObj = new(); // 硬件操作独占锁，！
        //private readonly ILogger<GENControl> _logger; //= LogManager.GetCurrentClassLogger(); // 日志对象
        private bool _isDisposed = false; // 释放标记
        public bool IsOpen { get; private set; }
        #endregion
        public int DISize;
        public int DOSize;
        public int ADISize;
        public int ADVSize;
        public ushort DIStartAddress;
        public ushort DOStartAddress;
        public ushort ADIStartAddress;
        public ushort ADVStartAddress;

        #region 构造函数
        public GENControl(/*ILogger<GENControl> logger*/)
        {
            //_logger = logger;
        }
        #endregion
        #region 打开总线+初始化（实例方法，支持取消+无CPU空转）
        public short GTN_Open(CancellationToken token)
        {
            short sRtn = 0;
            short sEcatSts = 0;

            // 加锁：保证初始化操作独占执行，防止多线程重复打开硬件
            lock (_lockObj)
            {
                if (IsOpen) return 0; // 已打开，直接返回成功

                // 1. 打开硬件卡
                sRtn = mc.GTN_Open(5, 1);
                if (sRtn != 0)
                {
                    var errorMsg = $"固高IO模块打开失败，错误码：{sRtn}";
                    Log.Error(errorMsg);
                    Console.WriteLine(errorMsg);
                    return sRtn;
                }

                // 2. 初始化EtherCAT总线
                sRtn = mc.GTN_InitEcatComm(_core);
                if (sRtn != 0)
                {
                    var errorMsg = $"EtherCAT总线初始化失败，错误码：{sRtn}";
                    Log.Error(errorMsg);
                    Console.WriteLine(errorMsg);
                    return sRtn;
                }

                // 3. 等待总线就绪（优化：加Task.Delay，避免CPU空转）
                DateTime startTime = DateTime.Now;
                do
                {
                    // 支持取消/超时（5秒）
                    if (token.IsCancellationRequested)
                    {
                        Log.Warning("EtherCAT总线初始化被手动取消");
                        return -1;
                    }
                    if ((DateTime.Now - startTime).TotalMilliseconds > 5000)
                    {
                        var errorMsg = "EtherCAT总线初始化超时（5秒），未就绪";
                        Log.Error(errorMsg);
                        Console.WriteLine(errorMsg);
                        return -1;
                    }

                    sRtn = mc.GTN_IsEcatReady(_core, out sEcatSts);
                    Task.Delay(10).Wait(); // 休眠10ms
                } while (sEcatSts != 1);

                // 4. 启动总线+同步IO
                sRtn = mc.GTN_StartEcatComm(_core);
                #region 映射DI通道
                short index = 0;//映射通道
                for (short i = 0; i < DISize/8; i++)
                {
                    for (short j = 1; j < 9; j++)
                    {
                        index++;
                        if (index == 33) index = 1;
                        sRtn = mc.GTN_RelateEcatSlaveToMcGpiBit(_core, index, 0, 0, (short)(j - 1), (short)(DIStartAddress + i));
                        if (sRtn != 0)
                        {
                            var errorMsg = $"EtherCAT映射DI失败,模块{index}，通道{j}，错误码：{sRtn}";
                            Log.Error(errorMsg);
                            Console.WriteLine(errorMsg);
                            return sRtn;
                        }
                    }
                }
                #endregion
                sRtn = mc.GTN_EcatIOSynch(_core);
                IsOpen = true;
                Log.Information("固高IO模块初始化成功，EtherCAT总线已就绪");
            }
            return sRtn;
        }
        #endregion

        #region DI/DO读写方法（实例方法+加锁+参数校验+日志）
        /// <summary>
        /// 读取数字量输入DI（32路）
        /// </summary>
        public bool[]? ReadDIInputValue()
        {
            try
            {
                lock (_lockObj)
                {
                    CheckDisposed();
                    if (!IsOpen) return null;

                    byte[] byDInput = new byte[DISize/8];
                    short sRtn = mc.GTN_EcatIOReadInput(_core, _slave, DIStartAddress, (ushort)(DISize / 8), out byDInput[0]);
                    if (sRtn != 0)
                    {
                        Log.Error($"读取DI失败，错误码：{sRtn}");
                        return null;
                    }
                    return ConvertHelper.Convert4BytesTo32BoolArray(byDInput, DISize);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "读取DI时发生未处理异常");
                return null;
            }
        }

        /// <summary>
        /// 读取数字量输出DO（32路）
        /// </summary>
        public bool[]? ReadDOOutPutValue()
        {
            try
            {
                lock (_lockObj)
                {
                    CheckDisposed();
                    if (!IsOpen) return null;

                    byte[] byDInput = new byte[DOSize/8];
                    short sRtn = mc.GTN_EcatIOReadInput(_core, _slave, DOStartAddress, (ushort)(DOSize / 8), out byDInput[0]);
                    if (sRtn != 0)
                    {
                        Log.Error($"读取DO失败，错误码：{sRtn}");
                        return null;
                    }
                    return ConvertHelper.Convert4BytesTo32BoolArray(byDInput,DOSize);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "读取DO时发生未处理异常");
                return null;
            }
        }
        #endregion

        #region ADI/ADV读方法（实例方法+加锁+参数校验+日志）
        /// <summary>
        /// 读取AD值 type等于 0：ADV  1:ADI
        /// </summary>
        /// <param name="type">0：ADV;  1:ADI</param>
        /// <param name="num"></param>
        /// <returns></returns>
        public double ReadADInputValue(int type, int channel)
        {
            try
            {
                lock (_lockObj)
                {
                    ushort offset = type == 0 ? (ushort)(ADVStartAddress + channel * 2) : (ushort)(ADIStartAddress + channel * 2);
                    CheckDisposed();
                    if (!IsOpen) return 0;
                    byte[] byADInput = new byte[2];
                    short sRtn = mc.GTN_EcatIOReadInput(_core, _slave, offset, 2, out byADInput[0]);
                    if (sRtn != 0)
                    {
                        Log.Error($"读取{type}失败，错误码：{sRtn}");
                        return 0.0;
                    }
                    ushort rawAD = (ushort)(byADInput[1] << 8 | byADInput[0]);

                    return Math.Round(rawAD / 65535.00 * 10.24, 2);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"读取AD时发生未处理异常：{type}");
                return 0.0;
            }
        }
        #endregion

        #region DO按位输出方法（实例方法+加锁+参数校验+日志）
        /// <summary>
        /// DO按位输出（手动参数校验+线程安全）
        /// </summary>
        public bool WriteDOBitOutputValue(ushort DOOutput, byte DOValue)
        {
            // 手动参数校验（替代无效的Range特性）
            if (DOOutput < 0 || DOOutput > DOSize)
            {
                Log.Error($"DO通道号非法：{DOOutput}，合法范围0-31");
                return false;
            }
            if (DOValue is not 0 and not 1)
            {
                Log.Error($"DO输出值非法：{DOValue}，合法值0或1");
                return false;
            }

            try
            {
                lock (_lockObj)
                {
                    CheckDisposed();
                    if (!IsOpen) return false;

                    ushort offset = (ushort)(DOOutput / 8);
                    short index = (short)(DOOutput % 8);
                    short sRtn = mc.GTN_EcatIOBitWriteOutput(_core, _slave, offset, index, DOValue);
                    bool result = sRtn == 0;
                    if (!result) Log.Error($"DO位输出失败，通道：{DOOutput}，值：{DOValue}，错误码：{sRtn}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"DO位输出异常，通道：{DOOutput}，值：{DOValue}");
                return false;
            }
        }
        #endregion

        #region DI映射
        /// <summary>
        /// 获取DI次数
        /// </summary>
        /// <param name="channel">读取通道，在Open时将DI的通道全部映射过来了，通道是0-31，或更多，跟实际的DI硬件多少而决定</param>
        /// <returns></returns>
        public uint GetDiReverseCount(short channel)
        {
            // 通用输入读取值
            try
            {
                lock (_lockObj)
                {
                    CheckDisposed();
                    if (!IsOpen) return 0;
                    uint lGpiValue;
                    short sRtn = mc.GTN_GetDiReverseCount(_core, mc.MC_GPI, channel, out lGpiValue, 1);
                    if (sRtn != 0)
                    {
                        Log.Error($"读取通道{channel}DI变化次数失败，错误码：{sRtn}");
                        return 0;
                    }
                    return lGpiValue;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"读取通道{channel}DI变化次数时发生未处理异常");
                return 0;
            }

        }

        /// <summary>
        /// 设置通道IO计数归零
        /// </summary>
        /// <param name="channel">通道，在Open时将DI的通道全部映射过来了，通道是0-31，或更多，跟实际的DI硬件多少而决定</param>
        /// <param name="lGpiValue">值，默认为0</param>
        /// <returns></returns>
        public uint SetDiReverseCount(short channel, uint lGpiValue = 0)
        {
            try
            {
                lock (_lockObj)
                {
                    CheckDisposed();
                    if (!IsOpen) return 0;
                    short sRtn = mc.GTN_SetDiReverseCount(_core, mc.MC_GPI, channel, ref lGpiValue, 1);
                    if (sRtn != 0)
                    {
                        Log.Error($"设置通道{channel}DI变化次数失败，错误码：{sRtn}");
                        return 0;
                    }
                    return lGpiValue;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"设置通道{channel}DI变化次数发生未处理异常");
                return 0;
            }
        }


        #endregion

        #region IDisposable实现（遵循官方释放模式，安全释放硬件）
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this); // 通知GC无需执行终结器
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            if (disposing)
            {
                // 释放托管资源（此处无，预留）
            }

            // 释放非托管资源：关闭硬件（加锁保证线程安全）
            lock (_lockObj)
            {
                if (IsOpen)
                {
                    mc.GTN_Close();
                    IsOpen = false;
                    Log.Information("固高IO模块已安全关闭");
                }
            }

            _isDisposed = true;
        }

        // 终结器：防止未手动调用Dispose时，硬件资源泄漏
        ~GENControl() => Dispose(false);

        // 检查是否已释放，防止释放后调用方法
        private void CheckDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(GENControl), "IO模块已释放，无法执行操作");
        }
        #endregion
    }
}