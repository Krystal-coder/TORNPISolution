using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 迈斯威志 WizPro 系列烧录器设备接口。
    /// 一路一台烧录器、各占一个 COM，所以各通道可以完全并发、互不影响；
    /// 过程状态由设备主动连发，进度经 IProgress 报给界面。
    /// </summary>
    public interface IWizProProgrammer : IDisposable
    {
        /// <summary>通道号（从 1 起，界面与日志用，不参与寻址）</summary>
        int Channel { get; }

        /// <summary>本通道烧录器的串口号</summary>
        string PortName { get; }

        /// <summary>串口是否已打开</summary>
        bool IsOpen { get; }

        /// <summary>打开本通道烧录器的串口</summary>
        Task OpenAsync(CancellationToken ct = default);

        /// <summary>关闭串口（不动烧录器内部已下载的工程数据，下次开完可直接自检烧写）</summary>
        Task CloseAsync();

        /// <summary>
        /// 读设备自报的通道号与预置产品信息，用来把 COM 与通道对上；
        /// 这台设备不支持该指令或没读到时返回 null。
        /// </summary>
        Task<WizProIdentity?> QueryIdentityAsync(CancellationToken ct = default);

        /// <summary>
        /// 让烧录器复位并自检内部已下载的工程数据：true 表示数据正常可烧，
        /// false 表示内部数据异常、必须重新下载工程（这时直接开烧只会整批失败且看不出真因）。
        /// </summary>
        /// <exception cref="TimeoutException">到时间既没说可烧也没说数据异常，多半是串口连的不是这台设备</exception>
        Task<bool> SelfCheckAsync(CancellationToken ct = default);

        /// <summary>
        /// 启动一轮烧写并一直读到结束帧。烧写失败不抛异常，统一用返回结果的 Success 与错误码表达；只有取消往上抛。
        /// </summary>
        Task<FlashResult> ProgramAsync(IProgress<FlashProgress>? onProgress, CancellationToken ct = default);
    }
}
