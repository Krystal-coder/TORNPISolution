using System;
using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.TestSteps
{
    /// <summary>
    /// BPA700 测试上下文实现，包装模拟仪实例、产品控制器与配置。
    /// </summary>
    public sealed class BpaTestContext : IBpaContext
    {
        public BPA700BetaSimulator Simulator { get; }
        public IProductController? Product { get; }
        public Bpa700Settings BpaSettings { get; }
        public string PressureUnit { get; }
        public Action<string, PressureSamplePoint> OnWaveformSample { get; }

        public BpaTestContext(BPA700BetaSimulator simulator, Bpa700Settings bpaSettings, Action<string, PressureSamplePoint>? onWaveformSample = null, IProductController? product = null)
        {
            Simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
            BpaSettings = bpaSettings ?? throw new ArgumentNullException(nameof(bpaSettings));
            PressureUnit = bpaSettings.PressureUnit ?? "mmHg";
            OnWaveformSample = onWaveformSample ?? ((_, _) => { });
            Product = product;
        }
    }
}
