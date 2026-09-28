using System.IO.Ports;
using System.Runtime.ExceptionServices;

namespace TORNPISolution.Service.Communication
{
    /// <summary>
    /// SerialPort.Open() 是原生阻塞调用：无超时、不可取消。
    /// 端口处于异常态（上次未正常关闭的残留、USB 转串口芯片挂死、被其他进程占用等）时会无限期挂起：
    /// 模块线程卡死、方案收尾不执行、停止按钮无效、UI 全部按钮保持禁用——整个软件表现为"点不动"。
    /// 故在独立后台线程上打开并限时等待：超时即放弃该线程与端口对象
    /// （对仍在打开中的端口调 Close 同样会挂起），快速失败交上层按测试失败路径处理。
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
            using var done = new ManualResetEventSlim(false);
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

            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
