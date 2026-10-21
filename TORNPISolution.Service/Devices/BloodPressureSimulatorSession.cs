using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 血压模拟仪租约会话：按仪器链接配置 ActiveSimulator 分发实例（组合根工厂逻辑唯一来源）。
    /// 康伟达：方案级共享 + 懒建立——第一个需要它的血压模块借用时才打开，之后各模块复用同一连接，RunPlan 结束才关闭（CloseAsync 发 0x00 归还本地控制）。
    /// 不可逐模块开关串口：设备被 0x00 踢回本地模式后需数秒恢复，期间 ACK 正常但不执行控制指令（实测 BP-003 压力源帧全部无应答、后续模块失联）。
    /// BPA700：保持逐模块自建自关生命周期（SDK 设备状态/阀门逻辑不变），租约 Dispose 时真关闭；纯 NFC/ECG 等不用模拟仪的方案不会碰任何血压设备。
    /// </summary>
    public sealed class BloodPressureSimulatorSession
    {
        private readonly ILogService _log = new LogService(nameof(BloodPressureSimulatorSession));
        private readonly IInstrumentConfigService _configService;
        private IBloodPressureSimulator? _shared;
        private int _leaseCount;

        public BloodPressureSimulatorSession(IInstrumentConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>康伟达共享会话是否已建立（BPA700 逐模块临时连接，永远为 false）</summary>
        public bool IsOpen => _shared is { IsOpen: true };

        /// <summary>借用模拟仪：康伟达→懒建立共享实例（Dispose 仅归还计数不关连接）；BPA700→新建并已打开的临时实例（Dispose 时真正关闭）；未选择模拟仪抛异常</summary>
        public async Task<SimulatorLease> RentAsync(CancellationToken ct = default)
        {
            var config = _configService.Load();
            if (config.ActiveSimulator == SimulatorType.Kangwenda)
            {
                await EnsureOpenAsync(config, ct).ConfigureAwait(false);
                _leaseCount++;
                return new SimulatorLease(_shared!, onDispose: ReleaseLease);
            }

            var temp = CreateSimulator(config);
            await temp.OpenAsync(ct).ConfigureAwait(false);
            _log.Info($"血压模拟仪（{config.ActiveSimulator}）：本模块使用独立连接（结束即关闭，保持原有逐模块生命周期）。");
            return new SimulatorLease(temp, onDispose: async () =>
            {
                try { await temp.CloseAsync().ConfigureAwait(false); }
                catch { /* 释放路径忽略 */ }
                finally { temp.Dispose(); }
            });
        }

        /// <summary>康伟达共享会话懒建立：首次借用时创建并打开，之后所有租约复用同一实例</summary>
        private async Task EnsureOpenAsync(InstrumentConnectionConfig config, CancellationToken ct)
        {
            if (IsOpen) return;

            var simulator = CreateSimulator(config);
            try
            {
                await simulator.OpenAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                simulator.Dispose();
                throw;
            }
            _shared = simulator;
            _log.Info($"康伟达血压模拟仪共享会话已建立（懒打开），方案内各血压模块复用同一连接");
        }

        /// <summary>连接活性校验：康伟达重发复位并等待回显（无应答抛异常）、BPA700 仅确认已打开；A01 在会话已建立时用它做真实连接测试</summary>
        public async Task VerifyConnectionAsync(CancellationToken ct = default)
        {
            if (_shared is KangwendaNIBPSimulator kw)
                await kw.VerifyAliveAsync(ct).ConfigureAwait(false);
            else if (!IsOpen)
                throw new InvalidOperationException("血压模拟仪会话未建立。");
        }

        /// <summary>关闭康伟达共享实例（RunPlan 结束调用；从未懒建立过则空操作）</summary>
        public async Task CloseAsync()
        {
            var shared = _shared;
            _shared = null;
            if (shared == null) return;
            try { await shared.CloseAsync().ConfigureAwait(false); }
            catch (Exception ex) { _log.Warn($"血压模拟仪会话关闭异常：{ex.Message}"); }
            finally { shared.Dispose(); }
            _log.Info("血压模拟仪会话已关闭。");
        }

        /// <summary>按配置创建模拟仪实例（未打开）：Kangwenda→串口 232；BPA700→正式版 SDK。</summary>
        private IBloodPressureSimulator CreateSimulator(InstrumentConnectionConfig config)
            => config.ActiveSimulator switch
            {
                SimulatorType.Kangwenda => new KangwendaNIBPSimulator(
                    config.KangwendaSimulator.PortName, config.KangwendaSimulator.BaudRate, _log),
                SimulatorType.BPA700 => new BPA700Simulator(config.BPA700Simulator.PortName, _log),
                _ => throw new InvalidOperationException("未选择血压模拟仪（仪器链接配置 → 模拟仪类型）")
            };

        private Task ReleaseLease()
        {
            if (_leaseCount > 0) _leaseCount--;
            return Task.CompletedTask;
        }
    }

    /// <summary>模拟仪租约：using 结束时按来源决定归还（共享实例空操作 / 临时实例真关闭）。</summary>
    public sealed class SimulatorLease : IAsyncDisposable, IDisposable
    {
        private readonly Func<Task> _onDispose;
        private bool _done;

        internal SimulatorLease(IBloodPressureSimulator simulator, Func<Task> onDispose)
        {
            Simulator = simulator;
            _onDispose = onDispose;
        }

        /// <summary>借用的模拟仪（会话共享实例或临时实例，均已 Open 完成）</summary>
        public IBloodPressureSimulator Simulator { get; }

        public async ValueTask DisposeAsync()
        {
            if (_done) return;
            _done = true;
            await _onDispose().ConfigureAwait(false);
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
