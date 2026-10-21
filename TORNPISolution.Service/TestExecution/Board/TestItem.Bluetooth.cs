using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：B03 蓝牙功能测试（单板工位）。
    /// 一台已启用网关出一个槽位、两项结果，分两步（槽位号口径与目录构建同一个函数 <see cref="SpecItems.BoardBluetoothSlots"/>）：
    ///   ① 识别广播地址：按该行 ExpectedMac 开过滤扫一段时间；ExpectedMac 留空就不指定地址，取识别窗口内信号强度最高的那条广播当本行要收的地址；
    ///   ② 采信号强度：把定下来的地址交给 <see cref="IBluetoothGateway.ReadRssiAsync"/> 攒样本，上报均值。
    /// 逐台并发（每台一条独立 TCP 连接、各自扫描与复位），采集结果串行上报（结果字典不是线程安全的）；某台连不上只判它自己那两项 FAIL，不拦别的网关。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>识别窗口下限：配置填 0 或负数也按这个下限扫，不让这条指令空跑一轮</summary>
        private const int MinIdentifyWindowMs = 200;

        //测试项目**************************---B03---**************************蓝牙功能测试（单板）
        public async Task BoardBluetoothModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("蓝牙功能测试")) return;

            var config = _configService.Load();
            // 按本轮范围筛槽位：单板页右键“只跑通道 3”时只连那一台网关，其余网关不建链也不扫
            var slots = WithinSlotScope(SpecItems.BoardBluetoothSlots(config), s => s.Slot);
            if (slots.Count == 0)
            {
                _log.Warn("蓝牙功能测试：本轮要的槽位里没有可对上的已启用网关（仪器连接配置→蓝牙网关），不采集");
                return;
            }

            var probes = await Task.WhenAll(slots.Select(s =>
                ProbeGatewayAsync(config.BluetoothGateways[s.GatewayIndex], token))).ConfigureAwait(false);

            // 上报串行：各台并发采集，写结果字典必须一个一个来（Dictionary 并发写会炸，逐项回调也要按目录顺序回填）
            var failed = 0;
            string? firstFailure = null;
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i].Slot;
                var probe = probes[i];

                var gotMac = probe.Failure == null && !string.IsNullOrWhiteSpace(probe.Mac);
                await collector.SetAsync(SpecItems.BluetoothMac(slot), gotMac
                    ? TestItemResult.OfText(1, $"{probe.Mac}{(probe.FromExpectedMac ? "" : "（识别窗口内信号最强）")}")
                    : TestItemResult.OfText(0, probe.Failure ?? "识别窗口内没扫到广播"));

                var samples = probe.Samples;
                if (samples.Count > 0)
                {
                    var mean = samples.Average();
                    await collector.SetAsync(SpecItems.BluetoothRssi(slot), new TestItemResult
                    {
                        Value = mean,
                        InstrumentText = $"均值 {mean:0.#}dBm，最差 {samples.Min()} / 最好 {samples.Max()}dBm，{samples.Count} 条广播"
                    });
                }
                else
                {
                    await collector.SetAsync(SpecItems.BluetoothRssi(slot),
                        TestItemResult.OfText(0, probe.Failure ?? "采信号强度时没收到该地址的广播"));
                }

                if (probe.Failure != null)
                {
                    failed++;
                    firstFailure ??= $"{probe.GatewayName}：{probe.Failure}";
                }
                _log.Info($"蓝牙槽位 {slot}（{probe.GatewayName}）：地址 {(gotMac ? probe.Mac : "未识别")}，样本 {samples.Count} 条");
            }

            // 一次模块只弹一条通知：几台网关同时没连上时不该刷出同样多个 Growl
            if (failed > 0)
                DeviceConnectionFailed?.Invoke("蓝牙功能测试", $"{failed}/{slots.Count} 台网关没采到（{firstFailure}）");

            _log.Info($"蓝牙功能测试完成：{slots.Count - failed}/{slots.Count} 个槽位识别到广播地址");
        }

        /// <summary>一台网关跑完「识别地址 → 采信号强度」，任何异常都收进返回的 Failure 里（不让整模块抛）；建链这一步已经确认网关在 AT 模式下应答 AT，后面每条指令才不会各自等满超时。</summary>
        private async Task<BluetoothProbe> ProbeGatewayAsync(BluetoothGatewaySettings settings, CancellationToken token)
        {
            var probe = new BluetoothProbe(settings.Name);
            using IBluetoothGateway gateway = new BluetoothGateway(settings, _log);
            try
            {
                await gateway.OpenAsync(token).ConfigureAwait(false);

                var expected = NormalizeMacAddress(settings.ExpectedMac);
                var found = await gateway.ScanAsync(Math.Max(MinIdentifyWindowMs, settings.IdentifyWindowMs), expected, token)
                                      .ConfigureAwait(false);
                // 填了预期地址时 ScanAsync 已经只留该地址的广播，没填就在全部广播里取最强的一条。
                // 广播记录是值类型，扫空时直接取第一条拿到的是 Mac 为空的默认值而不是 null，判不出「没扫到」，所以先判集合非空再把结果装进可空变量。
                BluetoothScanInfo? target = found.Count > 0 ? found.MaxBy(r => r.Rssi) : null;
                if (target == null)
                {
                    probe.Failure = expected == null
                        ? $"{settings.Name} 识别窗口 {settings.IdentifyWindowMs}ms 内没扫到任何广播"
                        : $"{settings.Name} 识别窗口内没扫到预期地址 {expected} 的广播";
                    return probe;
                }

                probe.Mac = target.Value.Mac;
                probe.FromExpectedMac = expected != null;

                // 采强度前网关侧会自己开过滤、复位再开扫描，这里只负责把样本收回来
                probe.Samples = await gateway.ReadRssiAsync(target.Value.Mac, ct: token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                probe.Failure = ex.Message;
            }
            finally
            {
                try { await gateway.CloseAsync().ConfigureAwait(false); }
                catch (Exception ex) { _log.Warn($"{settings.Name} 关闭连接异常：{ex.Message}"); }
            }

            return probe;
        }

        /// <summary>地址归一：去掉分隔符与空白、转大写（网关侧过滤也是这个写法）；全空按未填处理</summary>
        private static string? NormalizeMacAddress(string? mac)
        {
            if (string.IsNullOrWhiteSpace(mac)) return null;
            var digits = new string(mac.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            return digits.Length == 0 ? null : digits;
        }

        /// <summary>一个槽位的采集结果：并发采集阶段各写各的这份，上报阶段串行取用</summary>
        private sealed class BluetoothProbe(string gatewayName)
        {
            /// <summary>本槽位用的网关位号（日志与提示里点名是哪台）</summary>
            public string GatewayName { get; } = gatewayName;

            /// <summary>识别到的广播地址</summary>
            public string? Mac { get; set; }

            /// <summary>这个地址是来自配置里的 ExpectedMac（true）还是识别窗口里挑的最强广播（false）</summary>
            public bool FromExpectedMac { get; set; }

            /// <summary>信号强度样本（dBm），空集合 = 一条都没采到</summary>
            public IReadOnlyList<int> Samples { get; set; } = Array.Empty<int>();

            /// <summary>整体失败原因：建链失败、没扫到广播、采强度抛错都记在这里；null = 一切正常</summary>
            public string? Failure { get; set; }
        }
    }
}
