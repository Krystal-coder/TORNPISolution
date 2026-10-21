using TORNPISolution.GoogolEtherCAT.GEN;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Hardware
{
    /// <summary>
    /// 硬件设备统一管理（Lazy 单例）：各测试模块共用同一份硬件实例（固高IO模块、AECG100 等）。
    /// 由"初始化仪器"模块负责打开，应用退出时统一释放。
    /// </summary>
    public sealed class HardwareManager : IDisposable
    {
        private static readonly Lazy<HardwareManager> _instance = new(() => new HardwareManager());

        public static HardwareManager Instance => _instance.Value;

        private HardwareManager()
        {
        }

        /// <summary>固高刀锋IO模块（EtherCAT）</summary>
        public GENControl GenControl { get; } = new();

        /// <summary>鲸扬 AECG100 多功能综合性能测试仪：由 Aecg100InitModule 调用 ConnectAsync 连接，连接后 ECG 测试模块复用该实例输出波形</summary>
        public Aecg100Tester? Aecg100 { get; private set; }

        public async Task<bool> ConnectAecg100Async(Aecg100Settings settings)
        {
            if (Aecg100 != null && Aecg100.IsConnected)
                return true;

            Aecg100?.Dispose();
            Aecg100 = new Aecg100Tester(settings);
            return await Aecg100.ConnectAsync();
        }

        public void Dispose()
        {
            Aecg100?.Dispose();
            Aecg100 = null;
            GenControl.Dispose();
        }
    }
}
