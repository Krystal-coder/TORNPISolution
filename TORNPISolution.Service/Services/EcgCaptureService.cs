using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;
using TORNPISolution.Service.Devices;

namespace TORNPISolution.Service
{
    /// <summary>
    /// ECG 波形采集的运行层：常驻持有产品（DUT）串口，管心电模式（0x73 mode=5）启停与 0x8D 波形帧解码上抛
    /// 串口参数取仪器配置里的被测产品段；同一个 COM 口只能有一个持有者，连着时整机/单板/加速度不得开方案
    /// 进入产测在连接、退出产测只在断开（与加速度充电检查同口径）；波形缓冲、降采样显示与导出都在界面层
    /// </summary>
    public sealed class EcgCaptureService : IDisposable
    {
        private readonly IInstrumentConfigService _configService;
        private readonly ILogService _log = new LogService(nameof(EcgCaptureService));
        private readonly object _gate = new();

        private ProductStreamController? _channel;

        /// <summary>连接进行中：打开串口改到线程池后界面不再定住，此时间段串口既没连上也还没交出去</summary>
        private int _connecting;

        /// <summary>断开进行中：停推流与交口要几秒，这期间再点一次只会把这些重发一遍</summary>
        private int _disconnecting;

        /// <summary>凑不出整组的尾字节是否已记过第一帧原文</summary>
        private bool _unpairedLogged;

        public EcgCaptureService(IInstrumentConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>一帧解出的波形点</summary>
        public event Action<EcgWavePoint[]>? WaveReceived;

        /// <summary>连接、采集等状态变化</summary>
        public event Action? StateChanged;

        public bool IsConnected { get; private set; }

        /// <summary>是否处于采集态：以 0x73 开测命令拿到应答为准，产品真值拿不到</summary>
        public bool IsCapturing { get; private set; }

        public bool IsDisconnecting => Volatile.Read(ref _disconnecting) > 0;

        /// <summary>这个 COM 口是否还归这一路：连着就归，只有「断开」才交</summary>
        public bool IsPortBusy => IsConnected || Volatile.Read(ref _connecting) > 0 || Volatile.Read(ref _disconnecting) > 0;

        /// <summary>串口参数摘要</summary>
        public string PortSummary { get; private set; } = "未连接";

        /// <summary>本次连接累计收到的 0x8D 波形帧数</summary>
        public long FrameCount { get; private set; }

        /// <summary>本次连接累计解出的采样点数</summary>
        public long SampleCount { get; private set; }

        /// <summary>参数区凑不出完整 8 字节组的残留字节数</summary>
        public long UnpairedBytes { get; private set; }

        /// <summary>波形帧的时间轴基准（采样点序号 ÷ 本值 = 秒）</summary>
        public int SampleRateHz => ProductProtocol.EcgWavePairSampleRate;

        // ==================== 连接 ====================

        /// <summary>按仪器配置里的产品串口参数建立常驻通道，并进入产测模式 + 清掉残留推流。</summary>
        /// <returns>给操作人员看的一句话回执</returns>
        public async Task<string> ConnectAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} Start calling the EcgCaptureService.ConnectAsync() method . {Environment.NewLine}");
            var claimed = false;
            try
            {
                if (IsConnected) return string.Empty;
                if (Interlocked.CompareExchange(ref _connecting, 1, 0) != 0)
                    return "这一路正在连接（最多 5 秒），等它给回执再点";
                claimed = true;

                var settings = _configService.Load().Product;
                var channel = new ProductStreamController(settings);
                channel.EcgWaveFrameReceived += OnEcgWaveFrame;
                await Task.Run(channel.Open).ConfigureAwait(false);

                lock (_gate)
                {
                    _channel = channel;
                    IsConnected = true;
                    PortSummary = $"{settings.PortName} @ {settings.BaudRate}";
                    ResetCounters();
                }

                // 产品要在产测态才会推 0x8D 波形；这一下压在刚上口的 20 秒窗口内
                var enter = await channel.RequestAsync(ProductProtocol.EnterProductionTestMode, "进入产测",
                                                    timeoutMs: 2000, ct: ct).ConfigureAwait(false);

                // 建立时先关一次推流：上一轮异常退出可能把产品留在推流态，火发不等应答（与加速度充电检查同一口径）
                await channel.SendAsync(ProductProtocol.StopEcgTest, "关闭心电推流(建立清场)",
                    ProductProtocol.ResolveCmdData(ProductProtocol.StopEcgTestKey,
                        new[] { ProductProtocol.ModeEcg, (byte)1 })).ConfigureAwait(false);

                _log.Info($"ECG 波形采集已连接 {PortSummary}，进入产测{(enter == null ? "无应答" : "有应答")}");
                StateChanged?.Invoke();
                return enter == null
                    ? "串口已连上，但产品没应答进入产测：点「断开」再连一次（进入产测只在上口后 20 秒的窗口内有效）"
                    : string.Empty;
            }
            finally
            {
                if (claimed) Interlocked.Exchange(ref _connecting, 0);
                _log.Info($"{DateTime.Now} Ended calling the EcgCaptureService.ConnectAsync() method . {Environment.NewLine}");
            }
        }

        public async Task DisconnectAsync()
        {
            _log.Info($"{DateTime.Now} Start calling the EcgCaptureService.DisconnectAsync() method . {Environment.NewLine}");
            var claimed = Interlocked.CompareExchange(ref _disconnecting, 1, 0) == 0;
            try
            {
                if (!claimed)
                {
                    _log.Info("断开已在收尾中，这一路不再重复收尾");
                    return;
                }

                var channel = _channel;
                try
                {
                    await StopCaptureAsync().ConfigureAwait(false);

                    // 退出产测压在断开这一下：等 1 秒拿个确认就够，发不出去也不能挡住把口交出去
                    if (channel != null)
                    {
                        var exit = await channel.RequestAsync(ProductProtocol.ExitProductionTestMode, "退出产测",
                                                              timeoutMs: 1000).ConfigureAwait(false);
                        _log.Info(exit == null ? "退出产测无应答：产品可能仍在产测态" : "产品已退出产测态");
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn($"断开前收尾异常，仍继续交出口：{ex.GetType().Name} {ex.Message}");
                }
                finally
                {
                    ProductStreamController? owned;
                    lock (_gate)
                    {
                        owned = _channel;
                        _channel = null;
                        IsConnected = false;
                        PortSummary = "未连接";
                    }

                    if (owned != null)
                    {
                        owned.EcgWaveFrameReceived -= OnEcgWaveFrame;
                        owned.Close();
                        owned.Dispose();
                    }
                }

                _log.Info("ECG 波形采集已断开产品串口");
                StateChanged?.Invoke();
            }
            finally
            {
                if (claimed) Interlocked.Exchange(ref _disconnecting, 0);
                _log.Info($"{DateTime.Now} Ended calling the EcgCaptureService.DisconnectAsync() method . {Environment.NewLine}");
            }
        }

        // ==================== 采集 ====================

        /// <summary>进入心电测试模式（0x73 参数 05 00，参数优先取协议命令配置页）：产品开始持续推送 0x8D 波形帧</summary>
        public async Task<bool> StartCaptureAsync()
        {
            _log.Info($"{DateTime.Now} Start calling the EcgCaptureService.StartCaptureAsync() method . {Environment.NewLine}");
            try
            {
                var channel = _channel;
                if (channel == null) throw new InvalidOperationException("产品串口未连接");

                var resp = await channel.RequestAsync(ProductProtocol.StartEcgTest, "开始心电测试",
                    ProductProtocol.ResolveCmdData(ProductProtocol.StartEcgTestKey,
                        new[] { ProductProtocol.ModeEcg, (byte)0 })).ConfigureAwait(false);

                IsCapturing = resp != null;
                if (resp == null) _log.Warn("开始心电测试无应答：界面按未启动处理，可再点一次");
                else _log.Info("产品已进入心电测试模式，开始收波形帧");

                StateChanged?.Invoke();
                return resp != null;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the EcgCaptureService.StartCaptureAsync() method . {Environment.NewLine}");
            }
        }

        /// <summary>退出心电测试模式（0x73 参数 05 01）</summary>
        public async Task StopCaptureAsync()
        {
            _log.Info($"{DateTime.Now} Start calling the EcgCaptureService.StopCaptureAsync() method . {Environment.NewLine}");
            try
            {
                IsCapturing = false;
                var channel = _channel;
                if (channel == null) return;

                var resp = await channel.RequestAsync(ProductProtocol.StopEcgTest, "关闭心电测试",
                    ProductProtocol.ResolveCmdData(ProductProtocol.StopEcgTestKey,
                        new[] { ProductProtocol.ModeEcg, (byte)1 })).ConfigureAwait(false);
                if (resp == null) _log.Warn("关闭心电测试无应答：产品可能还在推波形");
                else _log.Info("产品已退出心电测试模式");

                StateChanged?.Invoke();
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the EcgCaptureService.StopCaptureAsync() method . {Environment.NewLine}");
            }
        }

        // ==================== 波形入口 ====================

        /// <summary>读线程回调：解成对帧式 → 计数 → 整批上抛，全程不碰界面</summary>
        private void OnEcgWaveFrame(ProductProtocol.ParsedFrame frame)
        {
            var points = ProductProtocol.ParseEcgWavePairs(frame.Payload);
            var leftover = frame.Payload.Length % ProductProtocol.EcgWavePairBytes;
            if (leftover > 0)
            {
                UnpairedBytes += leftover;
                if (!_unpairedLogged)
                {
                    _unpairedLogged = true;
                    _log.Warn($"0x8D 参数区 {frame.Payload.Length} 字节凑不出 8 字节整组（残留 {leftover}），"
                        + $"按成对帧式解析可能不对，首帧原文 [{BitConverter.ToString(frame.RawFrame ?? frame.Payload)}]");
                }
            }
            if (points == null) return;

            FrameCount++;
            SampleCount += points.Length;
            WaveReceived?.Invoke(points);
        }

        private void ResetCounters()
        {
            FrameCount = 0;
            SampleCount = 0;
            UnpairedBytes = 0;
            _unpairedLogged = false;
        }

        // ==================== 收尾 ====================

        /// <summary>关窗口前调用：限时收尾，超时只记账不拦退出</summary>
        public void StopAndAwaitWrapUp(int timeoutMs = 5_000)
        {
            if (!IsConnected) return;

            try
            {
                Task.Run(() => DisconnectAsync()).Wait(timeoutMs);
            }
            catch (Exception ex)
            {
                _log.Warn($"退出时 ECG 波形采集收尾未完成（{ex.GetType().Name} {ex.Message}），产品可能仍在推波形");
            }
        }

        public void Dispose() => StopAndAwaitWrapUp();
    }
}
