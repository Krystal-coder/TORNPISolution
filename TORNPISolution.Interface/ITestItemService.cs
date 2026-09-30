using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 测试项目执行服务接口（由 TestItem 类实现）。
    /// 按方案拖拽排序后的执行序号（xx，从 1 开始）调用 TestItemxx(xx)，
    /// 一次执行完整个模块流程，返回该模块全部测试项结果（测试值，可附带动态规格）。
    /// </summary>
    public interface ITestItemService
    {
        /// <summary>
        /// 设备连接失败通知（模块名 + 失败原因），UI 层订阅后弹出提示。
        /// </summary>
        event Action<string, string>? DeviceConnectionFailed;
        /// <summary>
        /// 压力采样点实时上抛（测试项名 + 采样点，仅 BPA700 支持的测试项在加压期间触发，
        /// 供 UI 在对应测试项行下绘制压力波形）。在后台线程触发，UI 层需自行调度到主线程并做节流。
        /// </summary>
        event Action<string, PressureSamplePoint>? PressureSampled;
        /// <summary>
        /// 按方案序号执行测试模块（对应模板的 TestItemxx()）。
        /// index 为模块在方案中拖拽排序后的执行序号（从 1 开始），
        /// 一次性执行完整模块流程，返回全部测试项结果（测试项名称 → 结果）：
        /// 每项含测试值（产品读回），需要按仪器值现算规格的项可同时带回动态规格
        /// （TestItemResult.SpecMin/SpecMax，如血压 = 仪器读回值 ± 容差），
        /// 判定由 UI 层完成（动态规格优先，否则用 Spec 静态规格）。
        /// </summary>
        /// <param name="index">方案执行序号（拖拽排序生成，从 1 开始），如初始化仪器排第 2 则传 2</param>
        /// <param name="module">该序号对应的测试模块定义</param>
        /// <param name="onItemCompleted">逐项完成回调：每个测试小项得出结果后立即触发，UI 层可立即回填显示；null 则等模块全部完成后一次性返回</param>
        /// <param name="token">取消令牌</param>
        Task<Dictionary<string, TestItemResult>> TestItemxx(int index, TestModuleModel module, Func<string, TestItemResult, Task>? onItemCompleted, CancellationToken token);

        /// <summary>
        /// 方案开始执行前复位方案级状态（产品通讯可用性记录等），
        /// 保证上一轮方案的 M16 握手记录不残留到本轮。
        /// </summary>
        void BeginPlanRun();
    }
}
