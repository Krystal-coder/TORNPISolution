using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>测试项目执行服务接口</summary>
    public interface ITestItemService
    {
        /// <summary>设备连接失败通知（模块名 + 失败原因）</summary>
        event Action<string, string>? DeviceConnectionFailed;

        /// <summary>人工操作提示请求（模块名 + 提示文本）</summary>
        event Func<string, string, Task>? OperatorPromptRequested;

        /// <summary>
        /// 压力采样点实时上抛（测试项名 + 采样点）：仅 BPA700 支持的测试项在加压期间触发，供 UI 在对应测试项行下绘制压力波形；在后台线程触发，UI 层需自行调度到主线程并做节流。
        /// </summary>
        event Action<string, PressureSamplePoint>? PressureSampled;

        /// <summary>
        /// 按方案序号执行一个测试模块，返回该模块全部测试项结果
        /// <param name="index">方案执行序号（拖拽排序生成，从 1 开始），如初始化仪器排第 2 则传 2</param>
        /// <param name="module">该序号对应的测试模块定义</param>
        /// <param name="onItemCompleted">逐项完成回调：每个测试小项得出结果后立即触发，UI 层可立即回填显示；null 则等模块全部完成后一次性返回</param>
        /// <param name="token">取消令牌</param>
        /// </summary>
        Task<Dictionary<string, TestItemResult>> TestItemxx(int index, TestModuleModel module, Func<string, TestItemResult, Task>? onItemCompleted, CancellationToken token);

        /// <summary>
        /// 方案开始执行前复位 <param name="slotScope">单板按通道跑时限定参与的测试槽位</param>
        /// </summary>
        void BeginPlanRun(IReadOnlyList<int>? slotScope = null);

        /// <summary>该模块 Id 在执行端有没有对应实现（只查分派表，不跑测试动作）</summary>
        bool ImplementsModule(string moduleId);
    }
}
