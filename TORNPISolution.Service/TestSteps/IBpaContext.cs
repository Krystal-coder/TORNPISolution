using System;
using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.TestSteps
{
    /// <summary>
    /// 血压测试步骤执行上下文：封装 BPA700 模拟仪、产品、配置、事件通道等共享资源。
    /// </summary>
    public interface IBpaContext
    {
        /// <summary>BPA700 模拟仪实例（Beta SDK）</summary>
        BPA700BetaSimulator Simulator { get; }

        /// <summary>被测产品控制器（可能未连接）</summary>
        IProductController? Product { get; }

        /// <summary>血压测试配置</summary>
        Bpa700Settings BpaSettings { get; }

        /// <summary>当前压力单位文本（如 mmHg / kPa）</summary>
        string PressureUnit { get; }

        /// <summary>波形采样上报委托：项名 + 数据点</summary>
        Action<string, PressureSamplePoint> OnWaveformSample { get; }
    }
}
