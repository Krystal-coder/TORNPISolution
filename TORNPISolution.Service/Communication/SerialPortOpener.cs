using System.IO.Ports;
using System.Runtime.ExceptionServices;

namespace TORNPISolution.Service.Communication
{
    /// <summary>
    /// SerialPort.Open() 是阻塞调用：无超时、不可取消。
    /// 项目内所有串口（康伟达模拟仪/产品/Modbus/通用）一律经此处打开。
    /// </summary>
    public static class SerialPortOpener
    {
        /// <summary>默认打开超时（毫秒）：正常打开几十毫秒内完成，5 秒足够覆盖 USB 枚举慢的场合</summary>
        public const int DefaultTimeoutMs = 5000;

        /// <summary>限时打开串口：超时抛 TimeoutException，打开抛异常时原样重抛（保留原始堆栈）</summary>
        public static void Open(SerialPort port, int timeoutMs = DefaultTimeoutMs)
        {
            Exception? error = null;
            // 不能用 using：超时时这里的 Dispose 会和弃管线程的 done.Set() 抢时间，
            // 句柄已释放再 Set 会抛 ObjectDisposedException，而它出现在后台线程的 finally 里
            var done = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try { port.Open(); }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            })
            { IsBackground = true, Name = $"SerialOpen-{port.PortName}" };
            thread.Start();

            if (!done.Wait(timeoutMs))
            {
                // Open 仍未返回：该线程与端口对象在主流程里不能再碰（Close 同样会挂起），
                // 只能另起后台线程尝试回收；回收失败最多漏一个句柄，好过整个软件卡死。
                var abandoned = port;
                new Thread(() =>
                {
                    try { abandoned.Close(); abandoned.Dispose(); }
                    catch { /* 回收失败仅漏句柄，可接受 */ }
                })
                { IsBackground = true, Name = $"SerialReclaim-{port.PortName}" }.Start();

                throw new TimeoutException(
                    $"串口 {port.PortName} 打开超时（{timeoutMs}ms）：端口可能被其他程序占用或驱动处于异常态，请检查设备管理器、拔插 USB 串口或重启电脑。");
            }

            done.Dispose();   // 走到这里说明线程已完成 Set()，此时释放才安全

            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
