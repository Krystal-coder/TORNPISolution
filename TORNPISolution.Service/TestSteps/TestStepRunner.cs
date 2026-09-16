using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.TestSteps
{
    /// <summary>
    /// 测试步骤统一执行器：负责异常捕获、结果上报、日志记录。
    /// 每个步骤成功/失败都会立即调用 collector.SetAsync，确保 UI 实时显示。
    /// </summary>
    public sealed class TestStepRunner
    {
        private readonly ILogService _log;

        public TestStepRunner(ILogService log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// 单步执行：成功返回步骤结果，失败返回 Value=0 的 FAIL 结果。
        /// 异常被捕获在本方法内，不会向上传播。
        /// </summary>
        public async Task<TestItemResult> RunAsync(ITestStep step, IBpaContext context, CancellationToken token)
        {
            try
            {
                _log.Info($"步骤开始：{step.Name}");
                var result = await step.ExecuteAsync(context, token).ConfigureAwait(false);
                _log.Info($"步骤完成：{step.Name}");
                return result;
            }
            catch (OperationCanceledException)
            {
                // 用户点“停止”：继续向上传递，不吞信号
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"步骤失败：{step.Name}，{ex.Message}");
                return TestItemResult.Of(0);
            }
        }

        /// <summary>
        /// 按顺序执行全部步骤，每步完成后立即上报结果。
        /// </summary>
        public async Task RunAllAsync(
            IEnumerable<ITestStep> steps,
            IBpaContext context,
            TestItemResultCollector collector,
            CancellationToken token)
        {
            foreach (var step in steps)
            {
                token.ThrowIfCancellationRequested();
                var result = await RunAsync(step, context, token).ConfigureAwait(false);
                await collector.SetAsync(step.Name, result).ConfigureAwait(false);
            }
        }
    }
}
