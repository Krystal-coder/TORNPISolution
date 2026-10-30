using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;
using TORNPISolution.Service.Devices;
using TORNPISolution.Service.Hardware;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：AECG100 心电/血氧类测试模块 A07 初始化连接 / A08 ECG采集与启动 / A09 ECG心率监测 / A10 ECG动态范围和线性 / A11 PPG脉搏率 / A12~A14 SpO2。
    /// 波形构建与采集辅助见 TestItem.Helpers.cs，项名统一引用 <see cref="SpecItems"/>。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>
        /// AECG100 初始化连接：连接 AECG100 多功能综合性能测试仪后读取型号/固件/序列号三个子项；成功写 1，失败/异常保持 0。
        /// </summary>
        //测试项目**************************---A07---**************************AECG100初始化连接
        public async Task Aecg100InitModule(TestItemResultCollector collector, CancellationToken token)
        {
            var config = _configService.Load();
            var settings = config.Aecg100Tester;

            var connected = await _hw.ConnectAecg100Async(settings);
            if (!connected)
            {
                _log.Error("AECG100 初始化连接失败（WTQInit/WTQConnect 返回失败）");
                return;
            }

            await collector.SetAsync(SpecItems.Aecg100InitConnection, TestItemResult.Of(1));
            _log.Info("AECG100 初始化连接成功");

            var model = _hw.Aecg100!.GetDeviceInformation();
            var serial = _hw.Aecg100.GetSerialNumber();
            var hw = _hw.Aecg100.GetHwInformation();

            await collector.SetAsync(SpecItems.Aecg100ModelName, TestItemResult.OfText(model != null ? 1 : 0, model?.ProductName ?? "--"));
            if (model != null)
                _log.Info($"AECG100 设备型号：{model.ProductName}");

            await collector.SetAsync(SpecItems.Aecg100FirmwareVersion, TestItemResult.OfText(
                hw != null ? 1 : 0,
                hw != null ? $"{hw.FWMajorVersion}.{hw.FWMinorVersion}.{hw.FWBuildVersion}" : "--"));
            if (hw != null)
                _log.Info($"AECG100 固件版本：{hw.FWMajorVersion}.{hw.FWMinorVersion}.{hw.FWBuildVersion}");

            await collector.SetAsync(SpecItems.Aecg100SerialNumber, TestItemResult.OfText(serial != null ? 1 : 0, serial ?? "--"));
            _log.Info($"AECG100 序列号：{serial ?? "N/A"}");
        }

        /// <summary>
        /// ECG 采集与启动（ECG-001 测量启动 / ECG-002 导联脱落检测）：
        /// ① 产品可用性以 A16 产品通讯初始化记录为准；
        /// ② ECG-001：AECG100 输出 ECG 波形（WTQOutputECG），SDK 返回成功即 PASS；
        /// ③ ECG-002：输出波形后提示拔掉 LA 导联，读取产品导联状态（0x88）判定脱落。
        /// </summary>
        //测试项目**************************---A08---**************************ECG采集与启动（ECG-001/002）
        public async Task EcgAcquisitionTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (!GetProductAvailable())
                _log.Warn("产品不可用：ECG-001/002 仍将输出 AECG100 波形，但产品回传比对项可能 FAIL");

            if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
            {
                _log.Error("ECG-001/002 跳过：AECG100 未连接，请先执行 A07 AECG100初始化连接");
                return;
            }

            // PPG-1R-525 模块在位检测
            await EnsurePpgModuleAsync(AecgPpgModuleKind.Green, SpecItems.Ppg1R525PresenceCheck, "PPG-1R-525（绿光）", collector);

            var ecgConfig = _configService.Load().Aecg100Tester;
            var ecgWaveform = BuildDefaultEcgWaveform(ecgConfig, 1.0);  // 60BPM = 1Hz

            var (wf001, cap001) = ApplyWaveformConfig(ecgConfig, SpecItems.Ecg001Start, ecgWaveform);
            var outputOk = await OutputEcgWithCaptureAsync(SpecItems.Ecg001Start, wf001, cap001, token);
            if (outputOk)
            {
                await collector.SetAsync(SpecItems.Ecg001Start, TestItemResult.Of(1));
                    _log.Info("ECG-001 PASS：AECG100 ECG 波形输出成功（含波形采集）");
            }
            else
            {
                _log.Error("ECG-001 FAIL：AECG100 ECG 波形输出失败（WTQOutputECG 返回 false）");
            }

            _log.Info("ECG-002：请拔掉 LA 导联，观察产品是否上报导联脱落状态...");
            var ecgWaveform2 = BuildDefaultEcgWaveform(ecgConfig, 1.0);
            var (wf002, cap002) = ApplyWaveformConfig(ecgConfig, SpecItems.Ecg002LeadOff, ecgWaveform2);
            await OutputEcgWithCaptureAsync(SpecItems.Ecg002LeadOff, wf002, cap002, token);
            await RunEcgLeadOffDetectionAsync(collector, token);

            _hw.Aecg100.StopOutputWaveform();
        }

        /// <summary>
        /// ECG 心率监测（ECG-004 心率检测精度）：
        /// ① 产品可用性以 A16 产品通讯初始化记录为准；
        /// ② 按配置页心率点（默认 40/60/80/100/120/160 BPM）逐档输出 ECG 波形，每档重复 3 次（配置可调）；
        /// ③ 测试值 = 各心率点设置下发偏差的最大值（当前仅验证 SDK 返回成功，偏差 = 0；产品回传比对预留）。
        /// </summary>
        //测试项目**************************---A09---**************************ECG心率监测（ECG-004）
        public async Task EcgHeartRateTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            var config = _configService.Load();
            var productAvailable = GetProductAvailable();
            if (!productAvailable)
                _log.Warn("产品不可用：ECG-004 仍将输出 AECG100 波形，但产品回传比对项可能 FAIL");

            // 设置心电测试模式（0x73 mode=5）：此处仅打开串口发指令后关闭，模式设置本身在 SetTestModeAsync 内
            using (var productForMode = _productFactory())
            {
                try
                {
                    await productForMode.OpenAsync(token);
                    await productForMode.SetTestModeAsync(5, ct: token);
                }
                catch { /* 产品不可用时不阻断 AECG100 侧逻辑 */ }
            }

            if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
            {
                _log.Error("ECG-004 跳过：AECG100 未连接，请先执行 A07 AECG100初始化连接");
                return;
            }

            var ecgConfig = config.Aecg100Tester;
            var testPoints = ecgConfig.HeartRateTestPoints;
            var repeats = ecgConfig.HeartRateRepeats;

            if (testPoints.Count == 0)
            {
                _log.Warn("ECG-004 跳过：未配置心率测试点（AECG100 配置页 → 心率测试点）");
                return;
            }

            // ② 逐档心率输出 ECG 波形，每档重复 N 次（段号=测试点序号，波形按段着色）
            double maxDeviation = 0;
            double worstSetBpm = double.NaN, worstReadBpm = double.NaN;
            for (var pointIndex = 0; pointIndex < testPoints.Count; pointIndex++)
            {
                var bpm = testPoints[pointIndex];
                token.ThrowIfCancellationRequested();
                var frequency = bpm / 60.0;   // BPM → Hz（60 BPM = 1Hz）

                for (var r = 1; r <= repeats; r++)
                {
                    var waveform = BuildDefaultEcgWaveform(ecgConfig, frequency);

                    var outputOk = await OutputEcgWithCaptureAsync(SpecItems.Ecg004HeartRateAccuracy, waveform,
                        GetWaveformCaptureMs(ecgConfig, SpecItems.Ecg004HeartRateAccuracy, 2000), token, pointIndex);
                    if (!outputOk)
                    {
                        _log.Error($"ECG-004 FAIL：心率 {bpm}BPM 第 {r}/{repeats} 次输出失败（WTQOutputECG 返回 false）");
                        continue;
                    }

                    // 波形稳定后回读产品心率（0x7C）比对；无有效回读的这一次不计入偏差统计（不参与“取最大偏差”）
                    var readBpm = await ReadProductHeartRateAsync(productAvailable, token);
                    if (double.IsNaN(readBpm))
                    {
                        _log.Warn($"ECG-004 心率 {bpm}BPM 第 {r}/{repeats} 次：产品无有效回读，本次不计入偏差统计");
                    }
                    else
                    {
                        var deviation = Math.Abs(readBpm - bpm);
                        if (double.IsNaN(worstReadBpm) || deviation > maxDeviation)
                        {
                            maxDeviation = deviation;
                            worstSetBpm = bpm;
                            worstReadBpm = readBpm;
                        }
                        _log.Info($"ECG-004 心率 {bpm}BPM 第 {r}/{repeats} 次：仪器设置下发成功，偏差={deviation:0.00}BPM");
                    }

                    await Task.Delay(500, token);
                }

                _hw.Aecg100.StopOutputWaveform();
            }

            // 取最大偏差点上报双值：仪器列=设定值，产品列=回读值，动态规格=设定值±容差。
            if (double.IsNaN(worstReadBpm))
            {
                _log.Warn($"ECG-004 FAIL：{(productAvailable ? "产品" : "产品未连接，")}全程未取到有效心率回读（0x7C），不按偏差 0 判定");
                await collector.SetAsync(SpecItems.Ecg004HeartRateAccuracy, new TestItemResult
                {
                    Value = double.NaN,
                    InstrumentValue = double.IsNaN(worstSetBpm) ? null : worstSetBpm,
                    ProductText = productAvailable ? "无产品有效回读" : "产品未连接，无回读"
                });
            }
            else
                await collector.SetAsync(SpecItems.Ecg004HeartRateAccuracy, TestItemResult.WithDualValues(
                    worstSetBpm, worstReadBpm,
                    worstSetBpm - ecgConfig.HeartRateTolerance, worstSetBpm + ecgConfig.HeartRateTolerance));
            _log.Info($"ECG-004 完成：最大偏差={maxDeviation:0.00}BPM（规格 ≤±{ecgConfig.HeartRateTolerance}BPM）");
        }

        /// <summary>
        /// ECG 动态范围和线性（ECG-009，YY 9706.247-2021 201.12.4.4.101；A10 唯一实现项）：
        /// ① 向产品发开始ECG测试（CtrlStartEcgTest：EF 04 05 73 05 00 15 03 FE）后产品持续推送 0x8D 波形帧（每帧 75×int16、码值÷100=mV），本模块独占产品串口到全部档位结束，全部档位结束后发关闭ECG测试（EF 04 05 73 05 01 D4 C3 FE）；
        /// ② 按配置遍历「幅度档（外层）× 偏置档（内层）」，一项一点（项名与 Spec 目录同源）；每档：AECG100 输出该幅度三角波 + DC 偏置 → 串口丢弃前 N 秒 → 连采 M 秒有效数据 → 停输出进下一档；
        /// ③ 判定量 = 有效窗内全局波峰-波谷（极值需连续 ≥2 点成立，滤单点毛刺），取与设定幅度的绝对偏差，允差走 Aecg100Settings.Ecg009ToleranceMv 分段口径（≤2mV 档→50μV，>2mV 档→设定幅度×10%）；
        /// 波形展示：本项不在行详情内嵌单档曲线，全部档位的仪器输出 / 产品回读曲线在专用波形窗口拼成长带对照（跑本模块时自动弹出，最小化/最大化/关闭不影响方案执行；已打开则复用同一窗口并按新一轮重置长带）。
        /// </summary>
        //测试项目**************************---A10---**************************ECG动态范围和线性（ECG-009）
        public async Task EcgSignalQualityTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
            {
                _log.Error("ECG-009 跳过：AECG100 未连接，请先执行 A07 AECG100初始化连接");
                return;
            }

            var ecgConfig = _configService.Load().Aecg100Tester;
            // 与 Spec 目录构建同口径（去 ≤0 无效档 + 去重），两边项清单才能一一对上
            var amplitudes = ecgConfig.Ecg009Amplitudes?.Where(a => a > 0).Distinct().ToList() ?? new List<double>();
            var offsets = ecgConfig.Ecg009DcOffsets?.Distinct().ToList() ?? new List<int>();
            if (amplitudes.Count == 0 || offsets.Count == 0)
            {
                _log.Warn("ECG-009 跳过：未配置幅度档或偏置档（AECG100 参数配置 → ECG-009 动态范围和线性）");
                return;
            }
            var frequency = ecgConfig.Ecg009Frequency > 0 ? ecgConfig.Ecg009Frequency : 6.25;
            var discardMs = (ecgConfig.Ecg009DiscardSeconds > 0 ? ecgConfig.Ecg009DiscardSeconds : 20) * 1000;
            var recordMs = (ecgConfig.Ecg009RecordSeconds > 0 ? ecgConfig.Ecg009RecordSeconds : 60) * 1000;
            var stepDelayMs = ecgConfig.Ecg009StepDelayMs >= 0 ? ecgConfig.Ecg009StepDelayMs : 500;
            var itemTotal = amplitudes.Count * offsets.Count;
            var estimateMinutes = itemTotal * (discardMs + recordMs + stepDelayMs) / 60000.0;

            using var product = _productFactory();
            var ecgModeOn = false;
            // 线性小结按偏置分组收集（执行顺序为幅度外层，跑完再打，与顺序解耦）
            var linearity = new Dictionary<int, List<string>>();
            try
            {
                // ① 进入心电测量：失败不阻断仪器侧流程，但各点无波形回读会按读回缺失判 FAIL
                try
                {
                    await product.OpenAsync(token);
                    await product.StartEcgTestAsync(token);
                    ecgModeOn = true;
                    _log.Info($"ECG-009 开始：{amplitudes.Count} 个幅度档 × {offsets.Count} 个偏置档 = {itemTotal} 项（{frequency:0.##}Hz，"
                        + $"电极 {(ecgConfig.Ecg009Electrode == (int)AecgElectrode.LeftArm ? "LA/L" : "RA/R")}，"
                        + $"噪声 {Ecg009NoiseLabel(ecgConfig.Ecg009NoiseFrequency)}，阻抗 {(ecgConfig.Ecg009Impedance ? "620kΩ" : "短路")}）；"
                        + $"每项丢弃前 {discardMs / 1000.0:0.#}s + 采 {recordMs / 1000.0:0.#}s 有效数据，预计共 ≈{estimateMinutes:0.0} 分钟");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.Error($"ECG-009：产品心电测量启动失败（开始ECG测试无应答），无波形回传，各点按读回缺失判 FAIL：{ex.Message}");
                }

                // ② 幅度档为外层、偏置档为内层：一个幅度跑完三个偏置再换下一个幅度
                foreach (var amplitude in amplitudes)
                {
                    foreach (var offset in offsets)
                    {
                        token.ThrowIfCancellationRequested();
                        var deviationPercent = await RunEcg009PointAsync(collector, product, ecgConfig,
                            amplitude, offset, frequency, discardMs, recordMs, stepDelayMs, ecgModeOn, token);
                        if (!double.IsNaN(deviationPercent))
                        {
                            if (!linearity.TryGetValue(offset, out var steps))
                                linearity[offset] = steps = new List<string>();
                            steps.Add($"{amplitude:0.##}mV→{deviationPercent:+0.0;-0.0;0.0}%");
                        }
                    }
                    _log.Info($"ECG-009：{amplitude:0.##}mV 一轮完成（已测 {linearity.Values.Sum(v => v.Count)}/{itemTotal} 项）");
                }

                // ③ 线性小结：同一偏置下各幅度偏差的递进趋势（幅度档由小到大即动态范围）
                foreach (var offset in offsets)
                    if (linearity.TryGetValue(offset, out var steps) && steps.Count > 0)
                        _log.Info($"ECG-009 线性小结（{offset:0.##}mV 偏置）：{string.Join("  ", steps)}");
            }
            finally
            {
                // ④ 收尾：取消/异常也要停输出并退出心电测量，否则产品会一直推波形
                try { _hw.Aecg100.StopOutputWaveform(); }
                catch (Exception ex) { _log.Warn($"ECG-009：停止仪器输出失败：{ex.Message}"); }
                if (ecgModeOn) await StopEcgMeasureAsync(product);
            }
        }

        /// <summary>噪声频率档序号→日志显示文本（0=Off 1=50Hz 2=60Hz 3=100Hz 4=120Hz）</summary>
        private static string Ecg009NoiseLabel(int index) => index switch
        {
            1 => "50Hz", 2 => "60Hz", 3 => "100Hz", 4 => "120Hz", _ => "Off"
        };

        /// <summary>
        /// ECG-009 单点（一幅度档×一偏置档）：仪器输出三角波 → 丢弃前 discardMs → 采 recordMs 有效数据 → 波峰-波谷判定并上报。
        /// 仪器回调单位 μV、产品回读单位 mV，上屏与判定统一为 mV（Y 轴 mV、X 轴时间 s，两曲线共用丢弃偏移后的时间轴）；返回产品波峰-波谷相对设定幅度的带符号偏差 %（NaN = 无产品回读，该项已按读回缺失上报 FAIL）。
        /// </summary>
        private async Task<double> RunEcg009PointAsync(TestItemResultCollector collector, IProductController product,
            Share.Models.Aecg100Settings cfg, double amplitude, int dcOffset, double frequency,
            int discardMs, int recordMs, int stepDelayMs, bool productReady, CancellationToken token)
        {
            var itemName = SpecItems.Ecg009Point(amplitude, dcOffset);
            var tolerance = cfg.Ecg009ToleranceMv(amplitude);
            using (TraceScope.Push($"ECG-009@{amplitude:0.##}mV/{dcOffset:0.##}mV"))
            {
                var wave = BuildDefaultEcgWaveform(cfg, frequency);
                // 本项自己的波形条件走 Ecg009* 配置，不吃全局默认值（避免被“噪声/阻抗/起搏”页的勾选污染）：
                wave.WaveformType = (AecgEcgWaveformType)(cfg.Ecg009WaveformType >= 0 ? cfg.Ecg009WaveformType : 1);   // 法规要 Triangle
                wave.Amplitude = amplitude;
                wave.Electrode = (AecgElectrode)cfg.Ecg009Electrode;              // RA/R=0，LA/L=255
                wave.NoiseFrequency = (AecgEcgNoiseFrequency)cfg.Ecg009NoiseFrequency;
                wave.NoiseAmplitude = cfg.Ecg009NoiseFrequency == 0 ? 0 : cfg.NoiseAmplitude;
                wave.Impedance = cfg.Ecg009Impedance ? AecgEcgImpedance.On : AecgEcgImpedance.Off;   // 620kΩ 由本页勾选决定，不勾即短路
                wave.DCOffsetVariable = false;                // 偏置档取配置列表，走仪器固定模式（300/0/-300）
                wave.DCOffset = dcOffset;

                // 产品回读波形全量留存（供波峰-波谷统计），同时按帧推给 UI 画第二条曲线
                var productWave = new List<double>(recordMs / 4 + 128);

                // 仪器输出与产品采集并发跑：AECG100 持续输出、用 StopOutputWaveform 停，WTQOutputECG 何时返回由官方 SDK 决定；串行 await 输出会挡死采集窗口（输出不返回就永远轮不到采集），采完再停输出
                var outputTask = Task.Run(() => _hw.Aecg100!.OutputEcg(wave, (time, ac, dc) =>
                    PressureSampled?.Invoke(itemName,
                        new PressureSamplePoint(time, (ac + dc) / 1000.0, 0, WaveformSeriesInstrument))), token);
                await Task.Delay(300, token);      // 给输出通道一点建立时间（真正的起始瞬态由丢弃窗口吃掉）

                // 有数据窗口内产品持续推送 0x8D（每帧 75 点 = 300ms@250SPS），期间不得并发其他命令
                var sampleCount = productReady
                    ? await product.CaptureEcgWaveAsync(recordMs, discardMs, (frameStartSeconds, millivolts) =>
                    {
                        productWave.AddRange(millivolts);
                        for (var i = 0; i < millivolts.Length; i++)
                            PressureSampled?.Invoke(itemName, new PressureSamplePoint(
                                frameStartSeconds + (double)i / ProductProtocol.EcgSampleRate,
                                millivolts[i], 0, WaveformSeriesProduct));
                    }, token)
                    : 0;

                // 采完立刻停仪器输出进下一档（不等仪器把整段波形放完）
                try { _hw.Aecg100!.StopOutputWaveform(); }
                catch (Exception ex) { _log.Warn($"{itemName}：停止仪器输出异常：{ex.Message}"); }

                var outputOk = true;
                if (await Task.WhenAny(outputTask, Task.Delay(5000)) != outputTask)
                    _log.Warn($"{itemName}：WTQOutputECG 未在 5s 内返回（输出已停，继续下一档）");
                else
                {
                    try { outputOk = outputTask.Result; }
                    catch (AggregateException ex)
                    { _log.Error($"{itemName}：WTQOutputECG 异常：{ex.Flatten().InnerExceptions[0].Message}"); }
                }
                if (!outputOk)
                {
                    _log.Error($"{itemName} FAIL：AECG100 三角波输出失败（WTQOutputECG 返回失败）");
                    await collector.SetAsync(itemName, new TestItemResult { Value = tolerance + 1, InstrumentValue = amplitude });
                    return double.NaN;
                }

                await Task.Delay(stepDelayMs, token);      // 换档间歇：残帧排空 + 下一档输出稳定

                var measured = EcgPeakToPeakMv(productWave);
                var hasMeasured = !double.IsNaN(measured);
                var error = hasMeasured ? Math.Abs(measured - amplitude) : double.NaN;
                var deviationPercent = hasMeasured ? (measured - amplitude) / amplitude * 100.0 : double.NaN;

                _log.Info($"{itemName}：设定={amplitude:0.###}mV 偏置={dcOffset:0.###}mV "
                    + $"有效窗={discardMs / 1000.0:0.#}s~{(discardMs + recordMs) / 1000.0:0.#}s 内 {productWave.Count} 点"
                    + $"产品回读波峰-波谷={(hasMeasured ? measured.ToString("0.###") : "--")}mV（回读 {sampleCount} 点）"
                    + $"误差={(hasMeasured ? error.ToString("0.###") : "--")}mV 允差 ≤{tolerance:0.###}mV "
                    + $"→ {(hasMeasured && error <= tolerance ? "PASS" : "FAIL" + (hasMeasured ? "" : "（无产品波形回读）"))}");

                // 一项一点：测试值=误差（无回读时落到允差外保证 FAIL），仪器列=设定幅度、产品列=回读波峰-波谷
                await collector.SetAsync(itemName, new TestItemResult
                {
                    Value = hasMeasured ? Math.Round(error, 3) : tolerance + 1,
                    InstrumentValue = amplitude,
                    ProductValue = hasMeasured ? Math.Round(measured, 3) : null
                });
                return deviationPercent;
            }
        }

        /// <summary>
        /// 产品回读波形的波峰-波谷幅值（mV）：全局最高点减全局最低点，极值需连续 ≥2 点成立才算（滤单点毛刺）；DC 偏置只平移波形不影响波峰-波谷。
        /// 实现取相邻两点组的最小值中的最大者作“峰”、最大值中的最小者作“谷”，孤立尖峰因此被排除；不足 4 个点（根本没收到波形帧）返回 NaN，调用方按读回缺失判 FAIL。
        /// </summary>
        private static double EcgPeakToPeakMv(List<double> millivolts)
        {
            if (millivolts.Count < 4) return double.NaN;
            var peak = double.MinValue;
            var trough = double.MaxValue;
            for (var i = 0; i + 1 < millivolts.Count; i++)
            {
                var a = millivolts[i];
                var b = millivolts[i + 1];
                if (Math.Min(a, b) > peak) peak = Math.Min(a, b);
                if (Math.Max(a, b) < trough) trough = Math.Max(a, b);
            }
            return peak - trough;
        }

        /// <summary>ECG-009 收尾：发关闭ECG测试（CtrlStopEcgTest：EF 04 05 73 05 01 D4 C3 FE）退出心电测量（失败只记警告，不影响已上报结果）</summary>
        private async Task StopEcgMeasureAsync(IProductController product)
        {
            try { await product.StopEcgTestAsync(CancellationToken.None); }
            catch (Exception ex) { _log.Warn($"ECG-009：退出心电测量失败（关闭ECG测试无应答）：{ex.Message}"); }
        }

        /// <summary>
        /// PPG 脉搏率模拟测试（PPG-001，需 PPG-1R-525 绿光模块在位）：
        /// ① 产品可用性以 A16 产品通讯初始化记录为准；
        /// ② 模块在位检测：AECG100 同时只接一个 PPG 模块，在位模块不是绿光单 LED 模块则拦截并提示更换后重跑；
        /// ③ 按配置心率点逐档输出 PPG 波形（频率 = BPM/60），产品光学传感器计算脉搏率后回读 0x7C 比对。
        /// </summary>
        //测试项目**************************---A11---**************************PPG脉搏率模拟测试（PPG-001）
        public async Task PpgHeartRateTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            var config = _configService.Load();
            var productAvailable = GetProductAvailable();
            if (!productAvailable)
                _log.Warn("产品不可用：PPG-001 仍将输出 AECG100 PPG 波形，但产品回读项会按无回读判 FAIL");

            if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
            {
                _log.Error("PPG-001 跳过：AECG100 未连接，请先执行 A07 AECG100初始化连接");
                return;
            }

            // 模块在位检测：PPG 心率测试需 PPG-1R-525 绿光模块（没接模块根本无法开始测试，直接拦截）
            if (!await EnsurePpgModuleAsync(AecgPpgModuleKind.Green, SpecItems.Ppg1R525PresenceCheck, "PPG-1R-525（绿光）", collector))
                return;

            var cfg = config.Aecg100Tester;
            if (cfg.PpgHeartRateTestPoints.Count == 0)
            {
                _log.Warn("PPG-001 跳过：未配置 PPG 心率测试点（配置 → ECG参数配置 → PPG心率）");
                return;
            }

            // 逐档心率输出 PPG 波形，每档重复 N 次（段号=测试点序号，波形按段着色）
            double maxDeviation = 0;
            double worstSetBpm = double.NaN, worstReadBpm = double.NaN;
            for (var pointIndex = 0; pointIndex < cfg.PpgHeartRateTestPoints.Count; pointIndex++)
            {
                var bpm = cfg.PpgHeartRateTestPoints[pointIndex];
                token.ThrowIfCancellationRequested();

                for (var r = 1; r <= cfg.PpgHeartRateRepeats; r++)
                {
                    var waveform = BuildDefaultPpgWaveform(cfg, bpm / 60.0);
                    var outputOk = await OutputPpgWithCaptureAsync(SpecItems.Ppg001PulseRateAccuracy, AecgPpgChannel.Channel1, waveform, cfg.PpgCaptureTimeMs, token, pointIndex);
                    if (!outputOk)
                    {
                        _log.Error($"PPG-001 FAIL：心率 {bpm}BPM 第 {r}/{cfg.PpgHeartRateRepeats} 次输出失败（WTQOutputPPG 返回 false）");
                        continue;
                    }

                    // 波形稳定后回读产品心率（0x7C）比对；无有效回读的这一次不计入偏差统计（不参与“取最大偏差”）
                    var readBpm = await ReadProductHeartRateAsync(productAvailable, token);
                    if (double.IsNaN(readBpm))
                    {
                        _log.Warn($"PPG-001 心率 {bpm}BPM 第 {r}/{cfg.PpgHeartRateRepeats} 次：产品无有效回读，本次不计入偏差统计");
                    }
                    else
                    {
                        var deviation = Math.Abs(readBpm - bpm);
                        if (double.IsNaN(worstReadBpm) || deviation > maxDeviation)
                        {
                            maxDeviation = deviation;
                            worstSetBpm = bpm;
                            worstReadBpm = readBpm;
                        }
                        _log.Info($"PPG-001 心率 {bpm}BPM 第 {r}/{cfg.PpgHeartRateRepeats} 次：偏差={deviation:0.00}BPM");
                    }
                }

                _hw.Aecg100.StopOutputWaveform();
            }

            if (double.IsNaN(worstReadBpm))
            {
                _log.Warn($"PPG-001 FAIL：{(productAvailable ? "产品" : "产品未连接，")}全程未取到有效脉搏率回读（0x7C），不按偏差 0 判定");
                await collector.SetAsync(SpecItems.Ppg001PulseRateAccuracy, new TestItemResult
                {
                    Value = double.NaN,
                    InstrumentValue = double.IsNaN(worstSetBpm) ? null : worstSetBpm,
                    ProductText = productAvailable ? "无产品有效回读" : "产品未连接，无回读"
                });
            }
            else
                await collector.SetAsync(SpecItems.Ppg001PulseRateAccuracy, TestItemResult.WithDualValues(
                    worstSetBpm, worstReadBpm,
                    worstSetBpm - cfg.PpgHeartRateTolerance, worstSetBpm + cfg.PpgHeartRateTolerance));
            _log.Info($"PPG-001 完成：最大偏差={maxDeviation:0.00}BPM（规格 ≤±{cfg.PpgHeartRateTolerance}BPM）");
        }

        /// <summary>
        /// SpO2 测量模块（对应测试规范 2.2.1，需 PPG-2R-940 红光+红外模块在位）：
        /// ① 产品可用性以 A16 产品通讯初始化记录为准，并设血氧测试模式（0x73 mode=4）；
        /// ② 模块在位检测：在位模块不是红光+红外双 LED 模块则拦截，提示更换模块后重跑；
        /// ③ SPO2-001 测量启动与停止：输出红/红外双通道信号启动测量后停止，验证输出启停控制；SPO2-002 手指脱落检测：正常输出后停止信号模拟脱落（产品脱落判定回读预留）；
        /// ④ SPO2-003 低氧报警测试：预留不判定项（产品报警回读协议未定稿，报“预留（不判定）”）。
        /// </summary>
        //测试项目**************************---A12---**************************SpO2测量模块（SPO2-001~003）
        public async Task Spo2MeasurementModule(TestItemResultCollector collector, CancellationToken token)
        {
            var config = _configService.Load();
            var productAvailable = GetProductAvailable();
            if (!productAvailable)
                _log.Warn("产品不可用：SPO2-001/002 仍将输出 AECG100 双通道信号，产品状态回读预留");

            // SPO2-003 低氧报警为预留不判定项（产品报警回读协议未定稿）：先报不判定，不参与模块判定
            await collector.SetAsync(SpecItems.Spo2003LowOxygenAlarm, TestItemResult.OfNotJudged("预留（不判定）"));

            // 设置血氧测试模式（0x73 mode=4）
            using (var productForMode = _productFactory())
            {
                try
                {
                    await productForMode.OpenAsync(token);
                    await productForMode.SetTestModeAsync(4, ct: token);
                }
                catch { /* 产品不可用时不阻断 AECG100 侧逻辑 */ }
            }

            if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
            {
                _log.Error("SPO2-001/002 跳过：AECG100 未连接，请先执行 A07 AECG100初始化连接");
                return;
            }

            // 模块在位检测：SpO2 测试需 PPG-2R-940 红光+红外模块（没接模块根本无法开始测试，直接拦截）
            if (!await EnsurePpgModuleAsync(AecgPpgModuleKind.RedIr, SpecItems.Ppg2R940PresenceCheck, "PPG-2R-940（红光+红外）", collector))
                return;

            var cfg = config.Aecg100Tester;

            // 正常测量信号：参考血氧值 + 配置脉搏率，按标定曲线反解红光通道幅度
            var spo2 = cfg.Spo2PulseRateRefSpo2;
            var frequency = cfg.Spo2PulseRate / 60.0;
            var rValue = cfg.Spo2CalibrationB == 0 ? 0 : (cfg.Spo2CalibrationA - spo2) / cfg.Spo2CalibrationB;
            var redAc = rValue * cfg.Spo2IrAcAmplitude;
            var (redWave, irWave) = BuildSpo2Waveforms(cfg, frequency, redAc);
            _log.Info($"SPO2 测量信号：SpO2={spo2}% 脉搏率={cfg.Spo2PulseRate}BPM R={rValue:0.000} 红光 AC={redAc:0.00}mV 红外 AC={cfg.Spo2IrAcAmplitude:0.00}mV");

            var startOutputOk = await OutputPpgExWithCaptureAsync(
                new[] { SpecItems.Spo2001StartStop }, redWave, irWave, cfg.Spo2CaptureTimeMs, token);
            _hw.Aecg100.StopOutputWaveform();
            await collector.SetAsync(SpecItems.Spo2001StartStop, TestItemResult.Of(startOutputOk ? 1 : 0));
            if (!startOutputOk)
                _log.Error("SPO2-001 FAIL：双通道输出失败（WTQOutputPPGEx 返回 false）");

            token.ThrowIfCancellationRequested();
            var detachOutputOk = await OutputPpgExWithCaptureAsync(
                new[] { SpecItems.Spo2002FingerOff }, redWave, irWave, Math.Min(cfg.Spo2CaptureTimeMs, 3000), token);
            _hw.Aecg100.StopOutputWaveform();   // 停止输出 = 模拟手指脱落无信号
            await Task.Delay(1500, token);
            await collector.SetAsync(SpecItems.Spo2002FingerOff, TestItemResult.Of(detachOutputOk ? 1 : 0));
            _log.Info("SPO2-002：已停止信号输出模拟手指脱落（产品脱落判定回读预留）");
        }

        /// <summary>
        /// SpO2 精度检测（对应测试规范 2.2.2，需 PPG-2R-940 红光+红外模块在位）：产品可用性以 A16 产品通讯初始化记录为准，并设血氧测试模式（0x73 mode=4）；
        /// SPO2-004 血氧饱和度精度：逐血氧点输出双通道信号，产品回读 0x82 比对（±2%）；
        /// SPO2-005 脉搏率精度：逐脉搏率点输出双通道信号，产品回读 0x7C 比对（±2BPM）；
        /// SPO2-006 低氧范围精度：逐低氧点输出双通道信号，产品回读 0x82 比对（±3%）；仪器列=设定值、产品列=回读值，动态规格=设定值±容差。
        /// </summary>
        //测试项目**************************---A13---**************************SpO2精度检测（SPO2-004~006）
        public async Task Spo2AccuracyModule(TestItemResultCollector collector, CancellationToken token)
        {
            var config = _configService.Load();
            var productAvailable = GetProductAvailable();
            if (!productAvailable)
                _log.Warn("产品不可用：SPO2-004~006 仍将输出 AECG100 双通道信号，但产品回读比对预留（偏差计 0）");

            using (var productForMode = _productFactory())
            {
                try
                {
                    await productForMode.OpenAsync(token);
                    await productForMode.SetTestModeAsync(4, ct: token);
                }
                catch { /* 产品不可用时不阻断 AECG100 侧逻辑 */ }
            }

            if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
            {
                _log.Error("SPO2-004~006 跳过：AECG100 未连接，请先执行 A07 AECG100初始化连接");
                return;
            }

            // 精度测试需红光+红外模块（本模块无在位检测项，不在位直接拦截并提示去 A12 检测）
            var kind = await Task.Run(() => _hw.Aecg100!.DetectPpgModuleKind());
            if (kind != AecgPpgModuleKind.RedIr)
            {
                _log.Error($"SPO2-004~006 跳过：当前在位模块不是 PPG-2R-940（红光+红外），请更换模块后先执行 A12 SpO2测量模块完成在位检测");
                return;
            }

            var cfg = config.Aecg100Tester;

            await RunSpo2AccuracySweepAsync(config, collector, SpecItems.Spo2004SaturationAccuracy,
                cfg.Spo2TestPoints, sweepPulseRate: false, cfg.Spo2Tolerance, productAvailable, token);

            await RunSpo2AccuracySweepAsync(config, collector, SpecItems.Spo2005PulseRateAccuracy,
                cfg.Spo2PulseRateTestPoints, sweepPulseRate: true, cfg.Spo2PulseRateTolerance, productAvailable, token);

            await RunSpo2AccuracySweepAsync(config, collector, SpecItems.Spo2006LowRangeAccuracy,
                cfg.Spo2LowRangeTestPoints, sweepPulseRate: false, cfg.Spo2LowRangeTolerance, productAvailable, token);

            _log.Info($"SPO2 精度检测完成（规格：血氧 ±{cfg.Spo2Tolerance}%，脉搏率 ±{cfg.Spo2PulseRateTolerance}BPM，低氧 ±{cfg.Spo2LowRangeTolerance}%）");
        }

        /// <summary>
        /// SpO2 灌注与抗干扰测试（对应测试规范 2.2.3）：SPO2-008 弱灌注能力 / SPO2-009 弱灌注提示 / SPO2-010 环境光抗干扰。
        /// 仪器 PI（灌注指数）幅度控制与环境光控制暂未具备自动化条件，三项均为预留不判定项（报“预留（不判定）”，不参与判定）；产品通讯集中在 A16 产品通讯初始化。
        /// </summary>
        //测试项目**************************---A14---**************************SpO2灌注与抗干扰测试（SPO2-008/009/010）
        public async Task Spo2PerfusionModule(TestItemResultCollector collector, CancellationToken token)
        {
            _log.Info("SPO2-008/009/010 为预留不判定项（仪器 PI 幅度控制与环境光控制暂未具备自动化条件），报不判定");
            await collector.SetAsync(SpecItems.Spo2008WeakPerfusion, TestItemResult.OfNotJudged("预留（不判定）"));
            await collector.SetAsync(SpecItems.Spo2009PerfusionHint, TestItemResult.OfNotJudged("预留（不判定）"));
            await collector.SetAsync(SpecItems.Spo2010AmbientLight, TestItemResult.OfNotJudged("预留（不判定）"));
        }
    }
}
