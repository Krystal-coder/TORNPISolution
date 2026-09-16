using System;
using System.Threading;
using System.Threading.Tasks;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.TestSteps
{
    /// <summary>
    /// BP-009 过压保护：持续充气到触发保护，读取触发压力（动态规格 threshold1~threshold2）。
    /// </summary>
    public sealed class BP009_OverpressureStep : ITestStep
    {
        public string Name => "BP-009过压保护";

        public async Task<TestItemResult> ExecuteAsync(IBpaContext context, CancellationToken token)
        {
            var op = context.BpaSettings.TestParams.Overpressure;
            var watch = System.Diagnostics.Stopwatch.StartNew();

            void OnSample(double pressure)
            {
                context.OnWaveformSample?.Invoke(Name, new PressureSamplePoint(watch.Elapsed.TotalSeconds, pressure));
            }

            context.Simulator.PressureSampled += OnSample;
            try
            {
                var trigger = await context.Simulator.RunOverpressureTestAsync(
                    op.TargetPressure, op.Threshold1, op.Threshold2, op.BpaInflation, ct: token);
                return TestItemResult.WithDynamicSpec(Math.Round(trigger, 1), op.Threshold1, op.Threshold2);
            }
            finally
            {
                context.Simulator.PressureSampled -= OnSample;
            }
        }
    }
}
