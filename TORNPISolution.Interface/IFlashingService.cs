using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 烧录编排服务：一轮只做一种动作（先 SPI Flash、后主控），每路独立并发、互不牵连。
    /// </summary>
    public interface IFlashingService
    {
        /// <summary>
        /// 跑一轮烧录，返回这一轮每条路的最终状态。
        /// 两条动作的流程不一样：SPI Flash 要先调厂商上位机把工程数据下载进烧录器（一次覆盖所有烧录器），
        /// 成功后才各通道并发烧写；主控是纯每路独立，各起一个命令行进程。
        /// </summary>
        /// <param name="action">本轮烧哪一颗</param>
        /// <param name="channels">要烧的通道号；为空表示配置里全部已启用的通道</param>
        /// <param name="onChannelUpdate">任一路状态变化时的回调，在采集线程上直接调用（界面自己回 UI 线程）</param>
        Task<IReadOnlyList<ChannelFlashReport>> RunRoundAsync(
            FlashAction action,
            IReadOnlyList<int>? channels,
            Action<ChannelFlashReport>? onChannelUpdate,
            CancellationToken ct = default);

        /// <summary>
        /// 只停某一路：其余路照常跑完。
        /// 现场确实需要这个：八块板里有一块接线歪了，不应该为了它把其余七块正在烧的也打断。
        /// 不在这一轮里的通道编号静默忽略（没得停就是没得停，不拿它报错）。
        /// </summary>
        void CancelChannel(int channel);
    }

    /// <summary>
    /// 烧录记录服务：一轮里每通道一行，落到本地 CSV。
    /// MES 与数据库的接口还没定，先用它把追溯撑住——产线上事后要查某块板子当时烧成什么样，全靠这份文件。
    /// </summary>
    public interface IFlashRecordService
    {
        /// <summary>记录所在目录（界面留一个打开文件夹的入口）</summary>
        string StorageDirectory { get; }

        /// <summary>
        /// 追加一轮的记录。写失败只记日志，绝不抛到界面上——烧录结果已经出来了，
        /// 不能因为文件被 Excel 占着就让操作员以为烧录失败。
        /// </summary>
        /// <param name="roundId">轮次号，把同一轮的几行关联起来</param>
        /// <param name="reports">本轮各路结果</param>
        /// <param name="barcodes">通道号到条码的映射，没扫码就传 null</param>
        void AppendRound(string roundId, IReadOnlyList<ChannelFlashReport> reports, IReadOnlyDictionary<int, string>? barcodes = null);
    }
}
