using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.TestSteps
{
    /// <summary>
    /// BP-008 快释放气功能：充气至目标压力（配置，默认 300mmHg）后开快释阀，测量归零耗时（规格 ≤10s）。
    /// 目标压力/归零判定压力/归零最长等待见配置 → BPA700 → 测试参数 → 压力控制。
    /// </summary>
    public sealed class BP008_FastReleaseStep : ITestStep
    {
        public string Name => "BP-008快释放气功能";

        public async Task<TestItemResult> ExecuteAsync(IBpaContext context, CancellationToken token)
        {
            await context.Simulator.StopSimulationAsync(token);
            await context.Simulator.SetFastValveAsync(false, token);
            await context.Simulator.SetSlowValveAsync(false, token);
            await context.Simulator.ResetAsync(token);
            await Task.Delay(2000, token);

            var pc = context.BpaSettings.TestParams.PressureControl;
            var targetPressure = pc.BP008TargetPressure;
            bool inflationOk = false;
            var timeout = context.BpaSettings.StaticPressureTimeoutSeconds;

            try
            {
                await context.Simulator.SetStaticPressureAsync(targetPressure, stableSeconds: 0, inflation: true, token, timeoutSeconds: timeout);
                inflationOk = true;
            }
            catch (TimeoutException)
            {
                // 重试一次
                try
                {
                    await context.Simulator.StopSimulationAsync(token);
                    await context.Simulator.ResetAsync(token);
                    await Task.Delay(3000, token);
                    await context.Simulator.SetStaticPressureAsync(targetPressure, stableSeconds: 0, inflation: true, token, timeoutSeconds: timeout);
                    inflationOk = true;
                }
                catch (TimeoutException)
                {
                    // 回退到上次稳压值
                }
            }

            if (inflationOk)
                await context.Simulator.WaitStaticPressureFinishAsync(ct: token);

            await context.Simulator.SetFastValveAsync(true, token);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var samples = new List<(double T, double P)>();

            void OnSample(double pressure)
            {
                lock (samples) samples.Add((watch.Elapsed.TotalSeconds, pressure));
                context.OnWaveformSample?.Invoke(Name, new PressureSamplePoint(watch.Elapsed.TotalSeconds, pressure));
            }

            context.Simulator.PressureSampled += OnSample;
            double zeroTime;
            try
            {
                // 调用静态压力测试使快释阀状态生效；inflation=false 不主动充气
                await context.Simulator.StartStaticPressureTestAsync(targetPressure, stableSeconds: 10, inflation: false, token);

                var zeroAt = -1.0;
                while (zeroAt < 0 && watch.Elapsed.TotalSeconds < pc.BP008ZeroTimeoutSeconds)
                {
                    token.ThrowIfCancellationRequested();
                    lock (samples)
                    {
                        foreach (var (t, p) in samples)
                        {
                            if (p <= pc.BP008ZeroPressure) { zeroAt = t; break; }
                        }
                    }
                    if (zeroAt < 0) await Task.Delay(100, token);
                }
                zeroTime = zeroAt >= 0 ? zeroAt : watch.Elapsed.TotalSeconds;
            }
            finally
            {
                context.Simulator.PressureSampled -= OnSample;
            }

            await context.Simulator.StopSimulationAsync(token);
            await context.Simulator.SetFastValveAsync(false, token);
            await context.Simulator.ResetAsync(token);
            return TestItemResult.Of(Math.Round(zeroTime, 2));
        }
    }
}
