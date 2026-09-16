using System;
using System.Threading;
using System.Threading.Tasks;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.TestSteps
{
    /// <summary>
    /// BP-006 最高充气压力：从产品读取预估收缩压，充气目标 = 收缩压 + 偏移（配置，默认 35mmHg），
    /// 规格为 [收缩压+30, 收缩压+40] mmHg（测试要求文档固定，判定基准不入配置）。
    /// 兜底收缩压/稳压秒数/推送超时见配置 → BPA700 → 测试参数 → 压力控制。
    /// </summary>
    public sealed class BP006_HighestInflationStep : ITestStep
    {
        public string Name => "BP-006最高充气压力";

        public async Task<TestItemResult> ExecuteAsync(IBpaContext context, CancellationToken token)
        {
            var timeout = context.BpaSettings.StaticPressureTimeoutSeconds;
            var pc = context.BpaSettings.TestParams.PressureControl;

            // 从被测产品读取预估收缩压
            var systolic = await ReadProductSystolicAsync(context, token);
            var target = systolic + pc.InflationTargetOffset;   // 测试要求 +30~+40，默认取中间值 35（可配置）
            var specMin = systolic + 30.0;
            var specMax = systolic + 40.0;

            // 确保气阀关闭后再充气
            await context.Simulator.SetFastValveAsync(false, token);
            await context.Simulator.SetSlowValveAsync(false, token);
            await Task.Delay(500, token);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            void OnSample(double pressure) =>
                context.OnWaveformSample?.Invoke(Name, new PressureSamplePoint(watch.Elapsed.TotalSeconds, pressure));

            context.Simulator.PressureSampled += OnSample;
            try
            {
                await context.Simulator.SetStaticPressureAsync(target, stableSeconds: pc.InflateStableSeconds, inflation: true, token, timeoutSeconds: timeout);
                var value = Math.Round(context.Simulator.LastStaticPressure, 1);
                await context.Simulator.WaitStaticPressureFinishAsync(ct: token);
                await context.Simulator.ResetAsync(token);
                return TestItemResult.WithDynamicSpec(value, specMin, specMax);
            }
            finally
            {
                context.Simulator.PressureSampled -= OnSample;
            }
        }

        /// <summary>
        /// 读取被测产品的收缩压；若产品未连接或读取失败，返回配置的兜底收缩压（默认 120）。
        /// </summary>
        private static async Task<double> ReadProductSystolicAsync(IBpaContext context, CancellationToken token)
        {
            var pc = context.BpaSettings.TestParams.PressureControl;
            var product = context.Product;
            if (product == null || !product.IsOpen)
                return pc.BP006DefaultSystolic;

            try
            {
                // 先尝试读取已推送的测量结果
                var reading = await product.ReadBpResultAsync(token);
                if (reading != null)
                    return reading.Systolic;

                // 没有已推送结果时，触发一次测量并等待推送
                await product.StartBpMeasureAsync(token);
                var pushed = await product.WaitForBpPushResultAsync(pc.ProductBpPushTimeoutSeconds * 1000, token);
                if (pushed != null)
                    return pushed.Systolic;
            }
            catch (Exception ex)
            {
                // 产品读数失败，使用兜底值继续，避免整个模块中断
                System.Diagnostics.Debug.WriteLine($"[BP-006] 读取产品收缩压失败：{ex.Message}");
            }

            return pc.BP006DefaultSystolic;
        }
    }
}
