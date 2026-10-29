using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：跨模块共用辅助（PPG 在位检测、心率回读、导联脱落判定、SpO2 精度扫描、波形构建与采集，
    /// </summary>
    public partial class TestItem
    {
        /// <summary>
        /// ECG-002 导联脱落检测：输出波形后读取产品导联状态（0x88）；lead_state = 0 表示脱落（PASS），非 0 表示未脱落（FAIL）。
        /// </summary>
        private async Task RunEcgLeadOffDetectionAsync(TestItemResultCollector collector, CancellationToken token)
        {
            // 给操作人员留时间拔掉 LA 导联
            try { await Task.Delay(2000, token); }
            catch (OperationCanceledException) { throw; }

            var config = _configService.Load();
            using var product = _productFactory();
            try
            {
                await product.OpenAsync(token);
                var leadState = await product.ReadLeadStateAsync(token);
                var hex = BitConverter.ToString(leadState).Replace("-", " ");
                _log.Info($"ECG-002：读取到产品导联状态 0x88 = {hex}");

                if (leadState.Length > 0 && leadState[0] == 0)
                {
                    await collector.SetAsync(SpecItems.Ecg002LeadOff, TestItemResult.Of(1));
                    _log.Info("ECG-002 PASS：产品检测到导联脱落");
                }
                else
                {
                    await collector.SetAsync(SpecItems.Ecg002LeadOff, TestItemResult.Of(0));
                    _log.Error("ECG-002 FAIL：产品未检测到导联脱落");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"ECG-002 FAIL：读取产品导联状态失败：{ex.Message}");
                await collector.SetAsync(SpecItems.Ecg002LeadOff, TestItemResult.Of(0));
            }
        }

        /// <summary>
        /// SpO2 精度扫描公共逻辑：逐点输出红光/红外双通道信号，产品回读比对，按最大偏差点上报双值。
        /// sweepPulseRate=false：points 为血氧设定点（%），脉搏率固定 cfg.Spo2PulseRate，回读产品血氧（0x82）；
        /// sweepPulseRate=true ：points 为脉搏率设定点（BPM），血氧固定 cfg.Spo2PulseRateRefSpo2，回读产品脉搏率（0x7C）。
        /// 产品不可用/回读失败时本次比对不计入；全程没取到一次有效回读则按“无上报值”判 FAIL（与 BP-003 同口径）。
        /// </summary>
        private async Task RunSpo2AccuracySweepAsync(
            InstrumentConnectionConfig config, TestItemResultCollector collector, string itemName,
            List<int> points, bool sweepPulseRate, double tolerance, bool productAvailable, CancellationToken token)
        {
            if (points.Count == 0)
            {
                _log.Warn($"{itemName} 跳过：未配置测试点（AECG100参数配置 → SpO2血氧）");
                await collector.SetAsync(itemName, TestItemResult.Of(0));
                return;
            }

            var cfg = config.Aecg100Tester;
            double maxDeviation = 0;
            double worstSet = double.NaN, worstRead = double.NaN;

            for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                var point = points[pointIndex];
                token.ThrowIfCancellationRequested();

                var spo2Value = sweepPulseRate ? cfg.Spo2PulseRateRefSpo2 : point;
                var pulseBpm = sweepPulseRate ? point : cfg.Spo2PulseRate;
                var frequency = pulseBpm / 60.0;   // 脉搏率 BPM → Hz

                // 按产品标定曲线 SpO2 = A - B×R 反解 R，再换算红光通道 AC 幅度（红外 AC 固定）
                var rValue = cfg.Spo2CalibrationB == 0 ? 0 : (cfg.Spo2CalibrationA - spo2Value) / cfg.Spo2CalibrationB;
                var redAc = rValue * cfg.Spo2IrAcAmplitude;
                var (redWave, irWave) = BuildSpo2Waveforms(cfg, frequency, redAc);
                _log.Info($"{itemName} SpO2={spo2Value}% 脉搏率={pulseBpm}BPM：R={rValue:0.000}，红光 AC={redAc:0.00}mV，红外 AC={cfg.Spo2IrAcAmplitude:0.00}mV");

                var outputOk = await OutputPpgExWithCaptureAsync(
                    new[] { itemName }, redWave, irWave, cfg.Spo2CaptureTimeMs, token, pointIndex);
                if (!outputOk)
                {
                    _log.Error($"{itemName} FAIL：SpO2={spo2Value}% 脉搏率={pulseBpm}BPM 双通道输出失败（WTQOutputPPGEx 返回 false）");
                    _hw.Aecg100!.StopOutputWaveform();
                    continue;
                }

                if (productAvailable)
                {
                    using var productRead = _productFactory();
                    try
                    {
                        await Task.Delay(1500, token);   // 等产品测量稳定
                        await productRead.OpenAsync(token);

                        var readValue = sweepPulseRate
                            ? await productRead.ReadHeartRateAsync(token)
                            : await productRead.ReadSpo2ValueAsync(token);
                        if (!double.IsNaN(readValue) && readValue > 0)
                        {
                            var deviation = Math.Abs(readValue - point);
                            if (double.IsNaN(worstRead) || deviation > maxDeviation)
                            {
                                maxDeviation = deviation;
                                worstSet = point;
                                worstRead = readValue;
                            }
                            _log.Info($"{itemName} 设定={point} 回读={readValue:0.00} 偏差={deviation:0.00}");
                        }
                        else
                        {
                            _log.Warn($"{itemName} 设定={point}：产品回读无效（{readValue}），本次不计入偏差统计");
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _log.Warn($"{itemName} 设定={point}：产品回读失败（本次不计入偏差统计）：{ex.Message}");
                    }
                }

                _hw.Aecg100!.StopOutputWaveform();
                await Task.Delay(500, token);
            }

            // 取最大偏差点上报双值：仪器列=设定值，产品列=回读值，动态规格=设定值±容差。
            if (double.IsNaN(worstRead))
            {
                _log.Warn($"{itemName} FAIL：全程未取到产品有效回读（无响应/回读为 0/读取异常），不按偏差 0 判定");
                await collector.SetAsync(itemName, new TestItemResult
                {
                    Value = double.NaN,
                    InstrumentValue = double.IsNaN(worstSet) ? null : worstSet,
                    ProductText = productAvailable ? "无产品有效回读" : "产品未连接，无回读"
                });
                return;
            }

            await collector.SetAsync(itemName, TestItemResult.WithDualValues(
                worstSet, worstRead, worstSet - tolerance, worstSet + tolerance));
        }

        /// <summary>
        /// PPG 模块在位检测（测一个调一个）：PPG/SpO2 测试模块开测前调用；AECG100 同时只接一个 PPG 模块，在位模块与需求不匹配时写 0 并提示更换。
        /// </summary>
        private async Task<bool> EnsurePpgModuleAsync(AecgPpgModuleKind required, string itemName, string requiredName, TestItemResultCollector collector)
        {
            var kind = await Task.Run(() => _hw.Aecg100!.DetectPpgModuleKind());
            if (kind == required)
            {
                await collector.SetAsync(itemName, TestItemResult.Of(1));
                _log.Info($"{itemName} PASS：检测到 {_hw.Aecg100!.DetectPpgModule()}");
                return true;
            }

            await collector.SetAsync(itemName, TestItemResult.Of(0));
            var detected = _hw.Aecg100!.DetectPpgModule() ?? "无";
            _log.Error($"{itemName} FAIL：当前在位模块为 [{detected}]，本测试需要 [{requiredName}]。请更换模块后重跑本模块（换模块无需重连主机，直接重跑即可）");
            return false;
        }

        /// <summary>
        /// 回读产品心率（0x7C），返回产品上报的原始心率值；产品不可用或回读失败时返回 NaN，由调用方决定预留上报方式。
        /// </summary>
        private async Task<double> ReadProductHeartRateAsync(bool productAvailable, CancellationToken token)
        {
            if (!productAvailable) return double.NaN;

            var config = _configService.Load();
            using var product = _productFactory();
            try
            {
                await Task.Delay(1500, token);   // 等产品测量稳定（0.67~1Hz 心率需数个周期）
                await product.OpenAsync(token);
                var hr = await product.ReadHeartRateAsync(token);
                if (double.IsNaN(hr) || hr <= 0)
                {
                    _log.Warn($"产品心率回读无效（{hr}），本次比对预留");
                    return double.NaN;
                }
                return hr;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warn($"产品心率回读失败（比对预留）：{ex.Message}");
                return double.NaN;
            }
        }

        /// <summary>波形曲线编号：仪器输出曲线（PressureSamplePoint.Series 默认值，其余模块的单波形项一律用本值）</summary>
        protected const int WaveformSeriesInstrument = 0;

        /// <summary>波形曲线编号：产品回传曲线（仅 A10 ECG-009 用，UI 与仪器输出曲线叠在同一张图上异色对照）</summary>
        protected const int WaveformSeriesProduct = 1;

        /// <summary>
        /// 输出 ECG 波形并采集回调数据，经 PressureSampled 事件推送给 UI；回调参数 (time, ac, dc) 中 ac+dc 为波形幅度（μV），SDK time 为时间轴（s）。
        /// ECG 波形持续输出直至主动停止，此处按 <paramref name="captureMs"/> 固定时长采集后返回，由调用方调用 StopOutputWaveform。
        /// </summary>
        private async Task<bool> OutputEcgWithCaptureAsync(string itemName, AecgEcgWaveform waveform, int captureMs, CancellationToken token, int segment = 0)
        {
            var ok = await Task.Run(() => _hw.Aecg100!.OutputEcg(waveform, (time, ac, dc) =>
            {
                PressureSampled?.Invoke(itemName, new PressureSamplePoint(time, ac + dc, segment));
            }), token).ConfigureAwait(false);

            if (!ok) return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(captureMs);
            try { await Task.Delay(captureMs, cts.Token); }
            catch (OperationCanceledException) { }
            return true;
        }

        /// <summary>
        /// 输出频率扫描波形并采集回调数据，经 PressureSampled 事件推送给 UI；频率扫描持续输出，故按 <paramref name="captureMs"/> 固定时长采集后返回。
        /// </summary>
        private async Task<bool> OutputFrequencyScanWithCaptureAsync(string itemName, AecgFrequencyScan scan, int captureMs, CancellationToken token)
        {
            var ok = await Task.Run(() => _hw.Aecg100!.OutputFrequencyScan(scan, (time, ac, dc) =>
            {
                PressureSampled?.Invoke(itemName, new PressureSamplePoint(time, ac + dc));
            }), token).ConfigureAwait(false);

            if (!ok) return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(captureMs);
            try { await Task.Delay(captureMs, cts.Token); }
            catch (OperationCanceledException) { }
            return true;
        }

        /// <summary>
        /// 输出单通道 PPG 波形并采集回调数据，经 PressureSampled 事件推送给 UI；与 ECG 一致，按 <paramref name="captureMs"/> 固定时长采集后返回，由调用方停止输出。
        /// </summary>
        private async Task<bool> OutputPpgWithCaptureAsync(string itemName, AecgPpgChannel channel, AecgPpgWaveform waveform, int captureMs, CancellationToken token, int segment = 0)
        {
            var ok = await Task.Run(() => _hw.Aecg100!.OutputPpg(channel, waveform, (time, ac, dc) =>
            {
                PressureSampled?.Invoke(itemName, new PressureSamplePoint(time, ac + dc, segment));
            }), token).ConfigureAwait(false);

            if (!ok) return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(captureMs);
            try { await Task.Delay(captureMs, cts.Token); }
            catch (OperationCanceledException) { }
            return true;
        }

        /// <summary>
        /// 输出双通道 PPG（SpO2 红光/红外）并采集红光通道回调数据推送 UI；同一轮采样可同时归属多个测试项（如 SPO2-001 血氧与 SPO2-002 脉搏率共用同轮波形）。
        /// </summary>
        private async Task<bool> OutputPpgExWithCaptureAsync(string[] itemNames, AecgPpgWaveform redWave, AecgPpgWaveform irWave, int captureMs, CancellationToken token, int segment = 0)
        {
            var ok = await Task.Run(() => _hw.Aecg100!.OutputPpgEx(redWave, irWave,
                onData1: (time, ac, dc) =>
                {
                    foreach (var name in itemNames)
                        PressureSampled?.Invoke(name, new PressureSamplePoint(time, ac + dc, segment));
                }), token).ConfigureAwait(false);

            if (!ok) return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(captureMs);
            try { await Task.Delay(captureMs, cts.Token); }
            catch (OperationCanceledException) { }
            return true;
        }

        /// <summary>
        /// 根据 AECG100 配置构建默认 PPG 波形（PPG-001 脉搏率测试用）；高心率时波形周期变短，主波/重搏时刻自动夹到周期内保证波形合法。
        /// </summary>
        private static AecgPpgWaveform BuildDefaultPpgWaveform(Share.Models.Aecg100Settings cfg, double frequency)
        {
            var timePeriod = frequency > 0 ? (int)(1000.0 / frequency) : 1000;
            var timeSp = Math.Min(cfg.PpgTimeSP, Math.Max(timePeriod / 4, 10));
            var timeDn = Math.Min(cfg.PpgTimeDN, Math.Max(timePeriod - 20, timeSp + 10));
            var timeDp = Math.Min(cfg.PpgTimeDP, Math.Max(timePeriod - 10, timeDn + 10));
            return new AecgPpgWaveform
            {
                WaveformType = AecgPpgWaveformType.Ppg,
                Frequency = frequency,
                VolDC = cfg.PpgVolDC,
                VolSP = cfg.PpgVolSP,
                VolDN = cfg.PpgVolDN,
                VolDP = cfg.PpgVolDP,
                TimeSP = timeSp,
                TimeDN = timeDn,
                TimeDP = timeDp,
                TimePeriod = timePeriod
            };
        }

        /// <summary>
        /// 构建 SpO2 双通道波形：ch1=红光（AC 按 R 比例）、ch2=红外（AC 固定），两通道 DC 与脉搏率一致（产品按 R=(AC红/DC)/(AC红外/DC) 换算）。
        /// </summary>
        private static (AecgPpgWaveform Red, AecgPpgWaveform Ir) BuildSpo2Waveforms(Share.Models.Aecg100Settings cfg, double frequency, double redAcMv)
        {
            var timePeriod = frequency > 0 ? (int)(1000.0 / frequency) : 800;
            var timeSp = Math.Min(120, Math.Max(timePeriod / 4, 10));
            var timeDn = Math.Min(300, Math.Max(timePeriod - 20, timeSp + 10));
            var timeDp = Math.Min(400, Math.Max(timePeriod - 10, timeDn + 10));

            AecgPpgWaveform Build(double ac) => new()
            {
                WaveformType = AecgPpgWaveformType.Ppg,
                Frequency = frequency,
                VolDC = cfg.Spo2VolDC,
                VolSP = ac,
                VolDN = ac * 0.6,
                VolDP = ac * 0.7,
                TimeSP = timeSp,
                TimeDN = timeDn,
                TimeDP = timeDp,
                TimePeriod = timePeriod
            };

            return (Build(redAcMv), Build(cfg.Spo2IrAcAmplitude));
        }

        /// <summary>
        /// 根据 AECG100 配置构建默认 ECG 波形；frequency 参数覆盖配置中的默认频率（心率测试按 BPM/60 计算，其他场景传 1.0 即 60BPM）。
        /// </summary>
        private static AecgEcgWaveform BuildDefaultEcgWaveform(Share.Models.Aecg100Settings cfg, double frequency)
        {
            var timePeriod = frequency > 0 ? (int)(1000.0 / frequency) : cfg.DefaultTimePeriod;
            return new AecgEcgWaveform
            {
                WaveformType = (AecgEcgWaveformType)cfg.DefaultWaveformType,
                Frequency = frequency,
                Amplitude = cfg.DefaultAmplitude,
                TWave = cfg.DefaultTWave,
                PWave = cfg.DefaultPWave,
                STSegment = cfg.DefaultSTSegment,
                DCOffsetVariable = cfg.DCOffsetVariable,
                DCOffset = cfg.DCOffset,
                TimePeriod = timePeriod,
                PRInterval = cfg.DefaultPRInterval,
                QRSDuration = cfg.DefaultQRSDuration,
                TDuration = cfg.DefaultTDuration,
                QTInterval = cfg.DefaultQTInterval,
                Impedance = cfg.ImpedanceEnabled ? AecgEcgImpedance.On : AecgEcgImpedance.Off,
                Electrode = (AecgElectrode)cfg.DefaultElectrode,
                PulseWidth = cfg.DefaultPulseWidth,
                NoiseAmplitude = cfg.NoiseAmplitude,
                NoiseFrequency = (AecgEcgNoiseFrequency)cfg.NoiseFrequency,
                PacingEnabled = cfg.PacingEnabled,
                PacingAmplitude = cfg.PacingAmplitude,
                PacingDuration = cfg.PacingDuration,
                PacingRate = cfg.PacingRate,
                RespirationMode = (AecgRespirationMode)cfg.DefaultRespirationMode,
                RespirationAmplitude = cfg.DefaultRespirationAmplitude,
                RespirationRate = cfg.DefaultRespirationRate,
                RespirationRatio = cfg.DefaultRespirationRatio
            };
        }

        /// <summary>
        /// 按测试项名读取 AECG100 波形配置并覆盖到波形对象上，返回（应用配置后的波形，采集时长 ms）；配置中 Frequency ≤0 时保留传入值，CaptureTimeMs ≤0 时使用 defaultCaptureMs。
        /// </summary>
        private static (AecgEcgWaveform waveform, int captureMs) ApplyWaveformConfig(
            Share.Models.Aecg100Settings cfg, string itemName, AecgEcgWaveform waveform, int defaultCaptureMs = 3000)
        {
            var config = cfg.GetWaveformConfig(itemName);
            if (config.Frequency > 0) waveform.Frequency = config.Frequency;
            var captureMs = config.CaptureTimeMs > 0 ? config.CaptureTimeMs : defaultCaptureMs;
            return (waveform, captureMs);
        }

        /// <summary>读取指定 ECG 测试项的采集时长；未配置时使用 defaultMs</summary>
        private static int GetWaveformCaptureMs(Share.Models.Aecg100Settings cfg, string itemName, int defaultMs = 3000)
        {
            var config = cfg.GetWaveformConfig(itemName);
            return config.CaptureTimeMs > 0 ? config.CaptureTimeMs : defaultMs;
        }

        /// <summary>
        /// 无仪器演示模式拦截：返回 true = 已被本模式拦截，调用方模块应立即 return；跳过本模块全部仪器控制（模拟仪/AECG/NFC/电源等），不打开产品串口（产品通讯集中在 A16 产品通讯初始化模块），模块内未报告项由 MainViewModel 收尾扫描按读回缺失统一判 FAIL。
        /// </summary>
        private bool NoInstrumentDemo(string moduleName)
        {
            if (!_configService.Load().NoInstrumentDemoMode) return false;

            _log.Info($"{moduleName}：无仪器演示模式，跳过仪器控制（产品通讯集中在 A16 产品通讯初始化）");
            return true;
        }

        /// <summary>
        /// 本轮产品可用性：以 A16 产品通讯初始化握手后的记录为准（不再逐模块重复 0x72/0x74 问询）；为 null（本轮方案未包含初始化模块）时按模式推断——正式模式视为可用、由实际通讯结果决定，无产品演示模式视为不可用（走仪器侧演示分支）。
        /// </summary>
        private bool GetProductAvailable(bool noProductDemoMode)
            => _productCommOk ?? !noProductDemoMode;

        /// <summary>
        /// 让测试员动手操作（按产品物理键等）：UI 弹模态框，点确定才返回；提示语同时进日志，界面未订阅时只记日志不阻断。
        /// token 取消后不再等人工确认（已弹出的框仍需人点掉），否则按下“停止”要等框点完流程才继续。
        /// </summary>
        private async Task PromptOperatorAsync(string moduleName, string message, CancellationToken token)
        {
            _log.Info($"{moduleName}：{message}");

            var handler = OperatorPromptRequested;
            if (handler == null)
            {
                _log.Warn($"{moduleName}：界面无接收操作提示，未确认测试员是否已按提示操作");
                return;
            }
            await handler(moduleName, message).WaitAsync(token).ConfigureAwait(false);
        }
    }
}
