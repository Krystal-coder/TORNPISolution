using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.TestSteps
{
    /// <summary>
    /// BP-007 匀速放气控制：从产品实际测量压力开始，开慢释阀后计算平均放气速率（规格 2~6 mmHg/s）。
    /// 起始压力偏移/兜底值/速率计算区间/结束判定等过程参数见配置 → BPA700 → 测试参数 → 压力控制。
    /// </summary>
    public sealed class BP007_UniformDeflationStep : ITestStep
    {
        public string Name => "BP-007匀速放气控制";

        public async Task<TestItemResult> ExecuteAsync(IBpaContext context, CancellationToken token)
        {
            var timeout = context.BpaSettings.StaticPressureTimeoutSeconds;
            var pc = context.BpaSettings.TestParams.PressureControl;

            // 从产品读取实际测量压力作为起始压力；读取失败时回退到配置的兜底压力（默认 150 mmHg）
            var startPressure = await ReadProductMeasurementPressureAsync(context, token);

            // 确保气阀全部关闭后再充气，避免上一测试项残留阀门状态导致漏气
            await context.Simulator.SetFastValveAsync(false, token);
            await context.Simulator.SetSlowValveAsync(false, token);
            await Task.Delay(500, token);

            await context.Simulator.SetStaticPressureAsync(startPressure, stableSeconds: pc.InflateStableSeconds, inflation: true, token, timeoutSeconds: timeout);
            await context.Simulator.WaitStaticPressureFinishAsync(ct: token);

            // 开慢释阀，开始匀速放气
            await context.Simulator.SetSlowValveAsync(true, token);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var samples = new List<(double T, double P)>();

            void OnSample(double pressure)
            {
                lock (samples) samples.Add((watch.Elapsed.TotalSeconds, pressure));
                context.OnWaveformSample?.Invoke(Name, new PressureSamplePoint(watch.Elapsed.TotalSeconds, pressure));
            }

            context.Simulator.PressureSampled += OnSample;
            try
            {
                // 调用一次静态压力测试使气阀状态生效；inflation=false 不主动充气，
                // 仅让慢释阀在测试过程中保持泄放。
                await context.Simulator.StartStaticPressureTestAsync(startPressure, stableSeconds: pc.BP007DeflateHoldSeconds, inflation: false, token);

                // 至少采集 3 秒，确保波形能完整展示放气过程
                var minEnd = watch.Elapsed.Add(TimeSpan.FromSeconds(3));
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    double latest;
                    lock (samples) latest = samples.Count > 0 ? samples[^1].P : startPressure;
                    if (watch.Elapsed >= minEnd && (latest <= pc.BP007DeflateEndPressure || watch.Elapsed.TotalSeconds >= pc.BP007MaxDeflateSeconds)) break;
                    await Task.Delay(200, token);
                }
            }
            finally
            {
                context.Simulator.PressureSampled -= OnSample;
            }

            double rate = 0;
            lock (samples)
            {
                if (samples.Count >= 2)
                {
                    // 取配置的区间（默认 130~30 mmHg）计算平均放气速率，避开起点波动和接近 0 的噪声
                    var segment = samples.Where(s => s.P <= pc.BP007RateSegmentHigh && s.P >= pc.BP007RateSegmentLow).ToList();
                    if (segment.Count >= 2)
                    {
                        var (t0, p0) = segment[0];
                        var (t1, p1) = segment[^1];
                        if (t1 > t0) rate = Math.Abs((p0 - p1) / (t1 - t0));
                    }
                }
            }

            await context.Simulator.StopSimulationAsync(token);
            await context.Simulator.SetSlowValveAsync(false, token);
            return TestItemResult.Of(Math.Round(rate, 2));
        }

        /// <summary>
        /// 读取被测产品的实际测量压力（收缩压 + 充气目标偏移作为峰值）；
        /// 若产品未连接或读取失败，返回配置的兜底起始压力（默认 150）。
        /// </summary>
        private static async Task<double> ReadProductMeasurementPressureAsync(IBpaContext context, CancellationToken token)
        {
            var pc = context.BpaSettings.TestParams.PressureControl;
            var product = context.Product;
            if (product == null || !product.IsOpen)
                return pc.BP007DefaultStartPressure;

            try
            {
                var reading = await product.ReadBpResultAsync(token);
                if (reading != null)
                    return reading.Systolic + pc.InflationTargetOffset;

                await product.StartBpMeasureAsync(token);
                var pushed = await product.WaitForBpPushResultAsync(pc.ProductBpPushTimeoutSeconds * 1000, token);
                if (pushed != null)
                    return pushed.Systolic + pc.InflationTargetOffset;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BP-007] 读取产品测量压力失败：{ex.Message}");
            }

            return pc.BP007DefaultStartPressure;
        }
    }
}
