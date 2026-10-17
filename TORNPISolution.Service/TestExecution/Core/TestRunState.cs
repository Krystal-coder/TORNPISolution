namespace TORNPISolution.Service
{
    /// <summary>
    /// 全局测试运行态：整机方案与单板轮次开始/收尾都置位（烧录工位另有一份在 FlashRunState）。
    /// 配置类窗口打开时读取，运行期间锁定编辑与保存，避免测试中途参数被改导致本轮判定口径不一致。
    /// </summary>
    public static class TestRunState
    {
        /// <summary>测试方案是否正在运行</summary>
        public static bool IsRunning { get; private set; }

        /// <summary>更新运行态（整机方案与单板轮次的开始/收尾两处都会调，只添新工位时要记得一并置位）</summary>
        public static void SetRunning(bool running) => IsRunning = running;
    }
}
