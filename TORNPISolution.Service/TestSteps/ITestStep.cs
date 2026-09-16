using System.Threading;
using System.Threading.Tasks;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.TestSteps
{
    /// <summary>
    /// 血压测试步骤接口：每个 BP-XXX 子项实现此接口。
    /// </summary>
    public interface ITestStep
    {
        /// <summary>步骤名称，与 Spec 中的测试项名称对应</summary>
        string Name { get; }

        /// <summary>
        /// 执行该测试步骤并返回结果。
        /// 由 TestStepRunner 统一处理异常和结果上报。
        /// </summary>
        Task<TestItemResult> ExecuteAsync(IBpaContext context, CancellationToken token);
    }
}
