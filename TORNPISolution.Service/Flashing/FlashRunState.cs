namespace TORNPISolution.Service.Flashing
{
    /// <summary>
    /// 烧录轮次运行态：一轮开跑/结束时置位，与 <see cref="TestRunState"/> 用途相同但各存一份（测试与烧录是两套治具、两条执行链，所以不是一把总闸）。
    /// 两侧各自读对方的标志，不在界面层传引用，页面切换与配置保存都靠它挡。
    /// 测试方案跑着时不让开烧录轮（操作人员的手被占着，且主控烧录要求"芯片刚上电那一刻"按复位）；烧录轮跑着时不让启动测试方案（工程数据下载会让厂商上位机搜占所有烧录器的 COM 口）。
    /// </summary>
    public static class FlashRunState
    {
        public static bool IsRunning { get; private set; }

        /// <summary>更新运行态（仅烧录页的开轮/收轮流程调用）</summary>
        public static void SetRunning(bool running) => IsRunning = running;
    }
}
