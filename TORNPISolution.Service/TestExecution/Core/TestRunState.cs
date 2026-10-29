namespace TORNPISolution.Service
{
    /// <summary>
    /// 全局测试运行态：整机方案与单板轮次开始/收尾都置位
    /// </summary>
    public static class TestRunState
    {
        /// <summary>测试方案是否正在运行</summary>
        public static bool IsRunning { get; private set; }

        /// <summary>更新运行态；新增工位时须在其开始/收尾处一并置位</summary>
        public static void SetRunning(bool running) => IsRunning = running;
    }
}
