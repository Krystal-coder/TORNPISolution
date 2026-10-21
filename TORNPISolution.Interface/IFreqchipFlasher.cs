using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 主控 MCU 的命令行烧录设备接口（调厂商烧录工具进程，逐行读它输出的 JSON 进度与结论）。
    /// 一路一个独立串口、一个进程，所以八条路可以并发跑、互不影响。
    /// </summary>
    public interface IFreqchipFlasher : IDisposable
    {
        /// <summary>通道号（从 1 起，界面与日志用）</summary>
        int Channel { get; }

        /// <summary>本通道烧录用的串口号（工具命令行的 -p 参数）</summary>

        string PortName { get; }

        /// <summary>
        /// 起进程烧这一路：工具回 Ready 后要等芯片复位才能握手，这段等待周期性经 onProgress 报剩余秒数，界面拿它提示操作人员按复位。
        /// 烧录失败不抛异常，统一用返回结果的 Success 与错误码表达；取消时进程会被杀掉并往上抛。
        /// </summary>
        Task<FlashResult> FlashAsync(IProgress<FlashProgress>? onProgress, CancellationToken ct = default);
    }
}
