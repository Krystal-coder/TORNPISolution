namespace TORNPISolution.Service
{
    /// <summary>
    /// 全局测试运行态：MainViewModel.RunPlan 启动/结束时置位。
    /// 配置类窗口（血压模拟仪测试配置等）打开时读取，运行期间锁定编辑与保存，
    /// 避免测试执行中途参数被改导致本轮判定口径不一致。
    /// </summary>
    public static class TestRunState
    {
        /// <summary>测试方案是否正在运行</summary>
        public static bool IsRunning { get; private set; }

        /// <summary>更新运行态（仅 MainViewModel 运行/停止流程调用）</summary>
        public static void SetRunning(bool running) => IsRunning = running;
    }
}
