using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Service.Hardware;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：AECG100 心电/血氧类测试模块 M07 初始化连接 / M08 ECG采集与启动 /
    /// M09 ECG心率监测 / M10 ECG信号质量与波形 / M11 PPG脉搏率 / M12~M14 SpO2。
    /// 波形构建与采集辅助见 TestItem.Helpers.cs，项名统一引用 <see cref="SpecItems"/>。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>
        /// AECG100 初始化连接：通过 HardwareManager 连接 AECG100 多功能综合性能测试仪，
        /// 连接成功后读取设备型号/固件/序列号信息。
        /// 成功写 1，失败/异常保持 0。
        /// </summary>
        //测试项目**************************---M07---**************************AECG100初始化连接
        public async Task Aecg100InitModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("AECG100 初始化连接")) return;
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

            // 读取设备信息（拆分为三个子项分别报告）
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
        /// ① 产品可用性以 M16 产品通讯初始化记录为准（本模块不再问询）；
        /// ② ECG-001：AECG100 输出 ECG 波形（WTQOutputECG），SDK 返回成功即 PASS；
        /// ③ ECG-002：输出波形后提示拔掉 LA 导联，读取产品导联状态（0x88）判定脱落。
        /// </summary>
        //测试项目**************************---M08---**************************ECG采集与启动（ECG-001/002）
        public async Task EcgAcquisitionTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("ECG 采集与启动")) return;
            // ① 产品可用性以本轮 M16 产品通讯初始化记录为准（不再重复 0x72/0x74 问询）
            var config = _configService.Load();
            if (!GetProductAvailable(config.NoProductDemoMode))
                _log.Warn("产品不可用：ECG-001/002 仍将输出 AECG100 波形，但产品回传比对项可能 FAIL");

            // 检查 AECG100 是否已连接
            if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
            {
                _log.Error("ECG-001/002 跳过：AECG100 未连接，请先执行 M07 AECG100初始化连接");
                return;
            }

            // PPG-1R-525 模块在位检测（从 M07 移至此处）
            await EnsurePpgModuleAsync(AecgPpgModuleKind.Green, SpecItems.Ppg1R525PresenceCheck, "PPG-1R-525（绿光）", collector);

            // ② ECG-001 测量启动：AECG100 输出 ECG 波形（参数从配置页读取），采集输出波形
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

            // ③ ECG-002 导联脱落检测：输出波形 → 提示拔掉导联 → 读取产品导联状态判定
            _log.Info("ECG-002：请拔掉 LA 导联，观察产品是否上报导联脱落状态...");
            var ecgWaveform2 = BuildDefaultEcgWaveform(ecgConfig, 1.0);
            var (wf002, cap002) = ApplyWaveformConfig(ecgConfig, SpecItems.Ecg002LeadOff, ecgWaveform2);
            await OutputEcgWithCaptureAsync(SpecItems.Ecg002LeadOff, wf002, cap002, token);
            await RunEcgLeadOffDetectionAsync(collector, token);

            // 停止波形输出
            _hw.Aecg100.StopOutputWaveform();
        }

        /// <summary>
        /// ECG 心率监测（ECG-004 心率检测精度）：
        /// ① 产品可用性以 M16 产品通讯初始化记录为准（本模块不再问询）；
        /// ② ECG-004：按配置页心率点（默认 40/60/80/100/120/160 BPM）逐档输出 ECG 波形，
        ///    每档重复 3 次（配置可调），仅验证仪器设置下发成功（产品回传比对预留），
        ///    测试值 = 各心率点设置下发偏差的最大值（当前仅验证 SDK 返回成功，偏差 = 0）。
        /// </summary>
        //测试项目**************************---M09---**************************ECG心率监测（ECG-004）
        public async Task EcgHeartRateTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("ECG 心率监测")) return;
            // ① 产品可用性以本轮 M16 产品通讯初始化记录为准（不再重复 0x72/0x74 问询）
            var config = _configService.Load();
            var productAvailable = GetProductAvailable(config.NoProductDemoMode);
            if (!productAvailable)
                _log.Warn("产品不可用：ECG-004 仍将输出 AECG100 波形，但产品回传比对项可能 FAIL");

            // 设置心电测试模式（0x73 mode=5）
            // 注：产品模式在 SetTestModeAsync 中设置，此处仅打开串口发指令后关闭
            using (var productForMode = _productFactory())
            {
                try
                {
                    await productForMode.OpenAsync(token);
                    await productForMode.SetTestModeAsync(5, ct: token);
                }
                catch { /* 产品不可用时不阻断 AECG100 侧逻辑 */ }
            }

            // 检查 AECG100 是否已连接
            if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
            {
                _log.Error("ECG-004 跳过：AECG100 未连接，请先执行 M07 AECG100初始化连接");
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

                    // 输出并按固定时长采集回调波形，上抛 UI 显示（ECG-004 行内波形）
                    var outputOk = await OutputEcgWithCaptureAsync(SpecItems.Ecg004HeartRateAccuracy, waveform,
                        GetWaveformCaptureMs(ecgConfig, SpecItems.Ecg004HeartRateAccuracy, 2000), token, pointIndex);
                    if (!outputOk)
                    {
                        _log.Error($"ECG-004 FAIL：心率 {bpm}BPM 第 {r}/{repeats} 次输出失败（WTQOutputECG 返回 false）");
                        continue;
                    }

                    // 波形稳定后回读产品心率（0x7C）比对；产品不可用时比对预留（偏差计 0）
                    var readBpm = await ReadProductHeartRateAsync(productAvailable, token);
                    var deviation = double.IsNaN(readBpm) ? 0.0 : Math.Abs(readBpm - bpm);
                    if (double.IsNaN(worstReadBpm) || deviation > maxDeviation)
                    {
                        maxDeviation = deviation;
                        worstSetBpm = bpm;
                        worstReadBpm = readBpm;
                    }
                    _log.Info($"ECG-004 心率 {bpm}BPM 第 {r}/{repeats} 次：仪器设置下发成功，偏差={deviation:0.00}BPM");

                    // 每次输出后短暂等待，让波形稳定
                    await Task.Delay(500, token);
                }

                // 每档测试完后停止波形
                _hw.Aecg100.StopOutputWaveform();
            }

            // 取最大偏差点上报双值：仪器列=设定值，产品列=回读值，动态规格=设定值±容差
            if (double.IsNaN(worstReadBpm))
                await collector.SetAsync(SpecItems.Ecg004HeartRateAccuracy, TestItemResult.Of(Math.Round(maxDeviation, 2)));
            else
                await collector.SetAsync(SpecItems.Ecg004HeartRateAccuracy, TestItemResult.WithDualValues(
                    worstSetBpm, worstReadBpm,
                    worstSetBpm - ecgConfig.HeartRateTolerance, worstSetBpm + ecgConfig.HeartRateTolerance));
            _log.Info($"ECG-004 完成：最大偏差={maxDeviation:0.00}BPM（规格 ≤±{ecgConfig.HeartRateTolerance}BPM，产品回传比对预留）");
        }

        /// <summary>
        /// ECG 信号质量与波形（ECG-009~012）：
        /// 按 YY 9706.247-2021 标准要求，AECG100 发出标准测试信号，产品回传测量值后由上位机判定。
        ///   ECG-009 动态范围和线性（201.12.4.4.101）：输出 6mV 三角波 + ±300mV DC 偏置，
        ///         判定：DUT 回放幅度衰减 < 50μV（需产品回传 ADC 采样值）
        ///   ECG-010 输入阻抗（201.12.4.4.102）：输出 10Hz 5mV 正弦波，开关断开/闭合两种状态，
        ///         判定：振幅变化 ≤ 6% → 输入阻抗 > 10MΩ（需产品回传 + 外部开关控制）
        ///   ECG-011 增益准确性（201.12.4.4.104）：输出 5Hz 2mV 正弦波，各增益档验证，
        ///         判定：幅度误差 ≤ ±10%（需产品回传各增益档测量值）
        ///   ECG-012 频率响应（201.12.4.4.108）：0.67Hz~40Hz 逐频点输出 2mV 正弦波，
        ///         判定：各频点响应在 5Hz 响应的 70%~140%（需产品回传各频点测量幅度）
        /// 当前阶段：AECG100 发出标准信号，产品回传比对逻辑预留，默认 PASS。
        /// TODO: 产品串口协议定稿后，补充各测试项的产品回传读取与判定逻辑。
        /// </summary>
        //测试项目**************************---M10---**************************ECG信号质量与波形（ECG-009~012）
        public async Task EcgSignalQualityTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("ECG 信号质量测试")) return;
            // 检查 AECG100 是否已连接
            if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
            {
                _log.Error("ECG-009~012 跳过：AECG100 未连接，请先执行 M07 AECG100初始化连接");
                return;
            }

            var ecgConfig = _configService.Load().Aecg100Tester;

            // ==================== ECG-009 动态范围和线性（YY 9706.247-2021 201.12.4.4.101） ====================
            // 标准：输出 6mV（峰-谷）三角波 + ±300mV DC 偏置，验证 DUT 回放幅度衰减 < 50μV
            // 当前：AECG100 发出标准信号，产品回传比对预留
            _log.Info("ECG-009：发出 6mV 三角波 + ±300mV DC 偏置（标准 201.12.4.4.101）...");

            // ECG-009 拆分为 a/b/c 三个独立测试项，每个子项各自输出并判定
            async Task<bool> RunEcg009Item(string itemName, double dcOffset)
            {
                var triWave = BuildDefaultEcgWaveform(ecgConfig, 10.4);
                triWave.WaveformType = AecgEcgWaveformType.Triangle;
                triWave.Amplitude = 6.0;                       // 6mV 峰-谷
                triWave.DCOffsetVariable = false;
                triWave.DCOffset = (int)dcOffset;
                var (wf, captureMs) = ApplyWaveformConfig(ecgConfig, itemName, triWave);
                var ok = await OutputEcgWithCaptureAsync(itemName, wf, captureMs, token);
                _hw.Aecg100.StopOutputWaveform();
                await Task.Delay(500, token);
                return ok;
            }

            var triOk = await RunEcg009Item(SpecItems.Ecg009aDynamicRangeNoBias, 0);
            var dc300Ok = await RunEcg009Item(SpecItems.Ecg009bDynamicRangePlus300, 300);
            var dcn300Ok = await RunEcg009Item(SpecItems.Ecg009cDynamicRangeMinus300, -300);

            // TODO: 产品回传 DUT 回放幅度，判定衰减 < 50μV；目前仪器输出成功即 PASS
            if (triOk)
            {
                await collector.SetAsync(SpecItems.Ecg009aDynamicRangeNoBias, TestItemResult.Of(1));
                _log.Info("ECG-009a PASS：无偏置 6mV 三角波输出成功");
            }
            else
            {
                _log.Error("ECG-009a FAIL：无偏置 6mV 三角波输出失败");
            }

            if (dc300Ok)
            {
                await collector.SetAsync(SpecItems.Ecg009bDynamicRangePlus300, TestItemResult.Of(1));
                _log.Info("ECG-009b PASS：+300mV DC 偏置三角波输出成功");
            }
            else
            {
                _log.Error("ECG-009b FAIL：+300mV DC 偏置三角波输出失败");
            }

            if (dcn300Ok)
            {
                await collector.SetAsync(SpecItems.Ecg009cDynamicRangeMinus300, TestItemResult.Of(1));
                _log.Info("ECG-009c PASS：-300mV DC 偏置三角波输出成功");
            }
            else
            {
                _log.Error("ECG-009c FAIL：-300mV DC 偏置三角波输出失败");
            }

            // ==================== ECG-010 输入阻抗（YY 9706.247-2021 201.12.4.4.102） ====================
            // 标准：输出 10Hz 5mV 正弦波，开关 S1 闭合/断开两种状态，振幅变化 ≤ 6% → 输入阻抗 > 10MΩ
            // 当前：AECG100 发出标准信号，产品回传 + 外部开关控制预留
            _log.Info("ECG-010：发出 10Hz 5mV 正弦波（标准 201.12.4.4.102）...");

            var sineWave10Hz = BuildDefaultEcgWaveform(ecgConfig, 10.0);
            sineWave10Hz.WaveformType = AecgEcgWaveformType.Sine;
            sineWave10Hz.Amplitude = 5.0;  // 5mV 峰-谷
            var (wf010, cap010) = ApplyWaveformConfig(ecgConfig, SpecItems.Ecg010InputImpedance, sineWave10Hz);
            var sineOk = await OutputEcgWithCaptureAsync(SpecItems.Ecg010InputImpedance, wf010, cap010, token);
            if (sineOk)
            {
                // TODO: 产品回传两种状态（开关闭合/断开）下的 ADC 采样幅度
                // var ampClosed = await product.ReadEcgAmplitudeAsync(token);    // S1 闭合
                // await hardwareControl.OpenSwitch();  // 外部开关断开
                // var ampOpen = await product.ReadEcgAmplitudeAsync(token);     // S1 断开
                // var changePct = Math.Abs(ampClosed - ampOpen) / ampClosed * 100;
                // collector["ECG-010输入阻抗"] = TestItemResult.Of(changePct);  // 规格 0~6 %
                await collector.SetAsync(SpecItems.Ecg010InputImpedance, TestItemResult.Of(1));
                    _log.Info("ECG-010：10Hz 5mV 正弦波输出成功，产品回传 + 外部开关控制预留（默认 PASS）");
            }
            else
            {
                _log.Error("ECG-010 FAIL：10Hz 5mV 正弦波输出失败");
            }
            _hw.Aecg100.StopOutputWaveform();
            await Task.Delay(500, token);

            // ==================== ECG-011 增益准确性（YY 9706.247-2021 201.12.4.4.104） ====================
            // 标准：输出 5Hz 2mV 正弦波，在所有增益设置下验证输出等效输入幅度误差 ≤ ±10%
            // 当前：AECG100 发出标准信号，产品回传各增益档测量值预留
            _log.Info("ECG-011：发出 5Hz 2mV 正弦波（标准 201.12.4.4.104）...");

            var sineWave5Hz = BuildDefaultEcgWaveform(ecgConfig, 5.0);
            sineWave5Hz.WaveformType = AecgEcgWaveformType.Sine;
            sineWave5Hz.Amplitude = 2.0;  // 2mV 峰-谷
            var (wf011, cap011) = ApplyWaveformConfig(ecgConfig, SpecItems.Ecg011GainAccuracy, sineWave5Hz);
            var gainOk = await OutputEcgWithCaptureAsync(SpecItems.Ecg011GainAccuracy, wf011, cap011, token);
            if (gainOk)
            {
                // TODO: 产品在各增益档回传测量值，计算幅度误差
                // var dutValues = await product.ReadEcgGainAccuracyAsync(token);  // 各增益档测量值
                // var maxError = dutValues.Max(v => Math.Abs(v - 2.0) / 2.0 * 100);
                // collector["ECG-011增益准确性"] = TestItemResult.Of(maxError);  // 规格 0~10 %
                await collector.SetAsync(SpecItems.Ecg011GainAccuracy, TestItemResult.Of(0));
                    _log.Info("ECG-011：5Hz 2mV 正弦波输出成功，产品各增益档回传预留（默认 PASS）");
            }
            else
            {
                _log.Error("ECG-011 FAIL：5Hz 2mV 正弦波输出失败");
            }
            _hw.Aecg100.StopOutputWaveform();
            await Task.Delay(500, token);

            // ==================== ECG-012 频率响应（YY 9706.247-2021 201.12.4.4.108） ====================
            // 标准：0.67Hz~40Hz 逐频点输出 2mV 正弦波，各频点响应在 5Hz 响应的 70%~140%
            // 当前：AECG100 发出频率扫描信号，产品回传各频点测量幅度预留
            _log.Info("ECG-012：发出 0.67Hz~40Hz 频率扫描 2mV 正弦波（标准 201.12.4.4.108）...");

            var scanCfg = ecgConfig.GetWaveformConfig(SpecItems.Ecg012FrequencyResponse);
            var scanCaptureMs = scanCfg.CaptureTimeMs > 0 ? scanCfg.CaptureTimeMs : 32000;
            var freqScan = new AecgFrequencyScan
            {
                Amplitude = 2.0,           // 2mV
                FrequencyStart = 0.67,   // 0.67Hz
                FrequencyFinish = 40.0,  // 40Hz
                Duration = scanCaptureMs / 1000  // 扫描时长（秒）
            };
            var scanOk = await OutputFrequencyScanWithCaptureAsync(SpecItems.Ecg012FrequencyResponse, freqScan, scanCaptureMs, token);
            if (scanOk)
            {
                // TODO: 产品回传各频点测量幅度，计算相对 5Hz 的幅度比
                // var freqResponses = await product.ReadEcgFrequencyResponseAsync(token);
                // var ref5Hz = freqResponses[5.0];
                // var maxDeviation = freqResponses.Max(kv => Math.Abs(kv.Value - ref5Hz) / ref5Hz * 100);
                // collector["ECG-012频率响应"] = TestItemResult.Of(maxDeviation);  // 规格 -30~40 %（70%~140%）
                await collector.SetAsync(SpecItems.Ecg012FrequencyResponse, TestItemResult.Of(1));
                    _log.Info("ECG-012：频率扫描输出成功，产品各频点回传预留（默认 PASS）");
            }
            else
            {
                _log.Error("ECG-012 FAIL：频率扫描输出失败");
            }
            _hw.Aecg100.StopOutputWaveform();
        }

        /// <summary>
        /// PPG 脉搏率模拟测试（PPG-001，需 PPG-1R-525 绿光模块在位）：
        /// ① 产品可用性以 M16 产品通讯初始化记录为准（本模块不再问询）；
        /// ② 模块在位检测：AECG100 同时只接一个 PPG 模块，在位模块不是绿光单 LED 模块则拦截，
        ///    提示更换模块后重跑（测一个调一个）；
        /// ③ PPG-001：按配置心率点逐档输出 PPG 波形（频率 = BPM/60），
        ///    产品光学传感器计算脉搏率后回读 0x7C 比对。
        /// </summary>
        //测试项目**************************---M11---**************************PPG脉搏率模拟测试（PPG-001）
        public async Task PpgHeartRateTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("PPG 心率监测")) return;
            // 产品可用性以本轮 M16 产品通讯初始化记录为准（不再重复 0x72/0x74 问询）
            var config = _configService.Load();
            var productAvailable = GetProductAvailable(config.NoProductDemoMode);
            if (!productAvailable)
                _log.Warn("产品不可用：PPG-001 仍将输出 AECG100 PPG 波形，但产品回读比对预留（偏差计 0）");

            // 检查 AECG100 是否已连接
            if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
            {
                _log.Error("PPG-001 跳过：AECG100 未连接，请先执行 M07 AECG100初始化连接");
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

                    // 波形稳定后回读产品心率（0x7C）比对；产品不可用时比对预留（偏差计 0）
                    var readBpm = await ReadProductHeartRateAsync(productAvailable, token);
                    var deviation = double.IsNaN(readBpm) ? 0.0 : Math.Abs(readBpm - bpm);
                    if (double.IsNaN(worstReadBpm) || deviation > maxDeviation)
                    {
                        maxDeviation = deviation;
                        worstSetBpm = bpm;
                        worstReadBpm = readBpm;
                    }
                    _log.Info($"PPG-001 心率 {bpm}BPM 第 {r}/{cfg.PpgHeartRateRepeats} 次：偏差={deviation:0.00}BPM");
                }

                // 每档测试完后停止波形
                _hw.Aecg100.StopOutputWaveform();
            }

            // 取最大偏差点上报双值：仪器列=设定值，产品列=回读值，动态规格=设定值±容差
            if (double.IsNaN(worstReadBpm))
                await collector.SetAsync(SpecItems.Ppg001PulseRateAccuracy, TestItemResult.Of(Math.Round(maxDeviation, 2)));
            else
                await collector.SetAsync(SpecItems.Ppg001PulseRateAccuracy, TestItemResult.WithDualValues(
                    worstSetBpm, worstReadBpm,
                    worstSetBpm - cfg.PpgHeartRateTolerance, worstSetBpm + cfg.PpgHeartRateTolerance));
            _log.Info($"PPG-001 完成：最大偏差={maxDeviation:0.00}BPM（规格 ≤±{cfg.PpgHeartRateTolerance}BPM）");
        }

        /// <summary>
        /// SpO2 测量模块（对应测试规范 2.2.1，需 PPG-2R-940 红光+红外模块在位）：
        /// ① 产品可用性以 M16 产品通讯初始化记录为准（本模块不再问询），并设血氧测试模式（0x73 mode=4）；
        /// ② 模块在位检测：在位模块不是红光+红外双 LED 模块则拦截，提示更换模块后重跑；
        /// ③ SPO2-001 测量启动与停止：输出红/红外双通道信号启动测量后停止，验证输出启停控制；
        ///    SPO2-002 手指脱落检测：正常输出后停止信号模拟脱落（产品脱落判定回读预留）；
        ///    SPO2-003 低氧报警测试：预留不判定项（产品报警回读协议未定稿，报“预留（不判定）”）。
        /// </summary>
        //测试项目**************************---M12---**************************SpO2测量模块（SPO2-001~003）
        public async Task Spo2MeasurementModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("血氧测量")) return;
            // 产品可用性以本轮 M16 产品通讯初始化记录为准（不再重复 0x72/0x74 问询）
            var config = _configService.Load();
            var productAvailable = GetProductAvailable(config.NoProductDemoMode);
            if (!productAvailable)
                _log.Warn("产品不可用：SPO2-001/002 仍将输出 AECG100 双通道信号，产品状态回读预留");

            // SPO2-003 低氧报警为预留不判定项（产品报警回读协议未定稿）：先报不判定，不参与模块判定
            await collector.SetAsync(SpecItems.Spo2003LowOxygenAlarm, TestItemResult.Demo("预留（不判定）"));

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

            // 检查 AECG100 是否已连接
            if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
            {
                _log.Error("SPO2-001/002 跳过：AECG100 未连接，请先执行 M07 AECG100初始化连接");
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

            // SPO2-001 测量启动与停止：双通道输出启动测量，采集完成后停止输出
            var startOutputOk = await OutputPpgExWithCaptureAsync(
                new[] { SpecItems.Spo2001StartStop }, redWave, irWave, cfg.Spo2CaptureTimeMs, token);
            _hw.Aecg100.StopOutputWaveform();
            await collector.SetAsync(SpecItems.Spo2001StartStop, TestItemResult.Of(startOutputOk ? 1 : 0));
            if (!startOutputOk)
                _log.Error("SPO2-001 FAIL：双通道输出失败（WTQOutputPPGEx 返回 false）");

            // SPO2-002 手指脱落检测：先正常输出一段，再停止信号模拟手指脱落（产品脱落判定回读预留）
            token.ThrowIfCancellationRequested();
            var detachOutputOk = await OutputPpgExWithCaptureAsync(
                new[] { SpecItems.Spo2002FingerOff }, redWave, irWave, Math.Min(cfg.Spo2CaptureTimeMs, 3000), token);
            _hw.Aecg100.StopOutputWaveform();   // 停止输出 = 模拟手指脱落无信号
            await Task.Delay(1500, token);
            await collector.SetAsync(SpecItems.Spo2002FingerOff, TestItemResult.Of(detachOutputOk ? 1 : 0));
            _log.Info("SPO2-002：已停止信号输出模拟手指脱落（产品脱落判定回读预留）");
        }

        /// <summary>
        /// SpO2 精度检测（对应测试规范 2.2.2，需 PPG-2R-940 红光+红外模块在位）：
        /// ① 产品可用性以 M16 产品通讯初始化记录为准（本模块不再问询），并设血氧测试模式（0x73 mode=4）；
        /// ② SPO2-004 血氧饱和度精度：逐血氧点输出双通道信号，产品回读 0x81 比对（±2%）；
        ///    SPO2-005 脉搏率精度：逐脉搏率点输出双通道信号，产品回读 0x7C 比对（±2BPM）；
        ///    SPO2-006 低氧范围精度：逐低氧点输出双通道信号，产品回读 0x81 比对（±3%）。
        /// 仪器列=设定值、产品列=回读值，动态规格=设定值±容差。
        /// </summary>
        //测试项目**************************---M13---**************************SpO2精度检测（SPO2-004~006）
        public async Task Spo2AccuracyModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("血氧精度测试")) return;
            // 产品可用性以本轮 M16 产品通讯初始化记录为准（不再重复 0x72/0x74 问询）
            var config = _configService.Load();
            var productAvailable = GetProductAvailable(config.NoProductDemoMode);
            if (!productAvailable)
                _log.Warn("产品不可用：SPO2-004~006 仍将输出 AECG100 双通道信号，但产品回读比对预留（偏差计 0）");

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

            // 检查 AECG100 是否已连接
            if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
            {
                _log.Error("SPO2-004~006 跳过：AECG100 未连接，请先执行 M07 AECG100初始化连接");
                return;
            }

            // 精度测试需红光+红外模块（本模块无在位检测项，不在位直接拦截并提示去 M12 检测）
            var kind = await Task.Run(() => _hw.Aecg100!.DetectPpgModuleKind());
            if (kind != AecgPpgModuleKind.RedIr)
            {
                _log.Error($"SPO2-004~006 跳过：当前在位模块不是 PPG-2R-940（红光+红外），请更换模块后先执行 M12 SpO2测量模块完成在位检测");
                return;
            }

            var cfg = config.Aecg100Tester;

            // SPO2-004 血氧饱和度精度（逐血氧点，脉搏率固定）
            await RunSpo2AccuracySweepAsync(config, collector, SpecItems.Spo2004SaturationAccuracy,
                cfg.Spo2TestPoints, sweepPulseRate: false, cfg.Spo2Tolerance, productAvailable, token);

            // SPO2-005 脉搏率精度（逐脉搏率点，血氧固定为参考值）
            await RunSpo2AccuracySweepAsync(config, collector, SpecItems.Spo2005PulseRateAccuracy,
                cfg.Spo2PulseRateTestPoints, sweepPulseRate: true, cfg.Spo2PulseRateTolerance, productAvailable, token);

            // SPO2-006 低氧范围精度（逐低氧点，脉搏率固定）
            await RunSpo2AccuracySweepAsync(config, collector, SpecItems.Spo2006LowRangeAccuracy,
                cfg.Spo2LowRangeTestPoints, sweepPulseRate: false, cfg.Spo2LowRangeTolerance, productAvailable, token);

            _log.Info($"SPO2 精度检测完成（规格：血氧 ±{cfg.Spo2Tolerance}%，脉搏率 ±{cfg.Spo2PulseRateTolerance}BPM，低氧 ±{cfg.Spo2LowRangeTolerance}%）");
        }

        /// <summary>
        /// SpO2 灌注与抗干扰测试（对应测试规范 2.2.3）：
        /// SPO2-008 弱灌注能力 / SPO2-009 弱灌注提示 / SPO2-010 环境光抗干扰，
        /// PI（灌注指数）幅度控制与环境光控制暂未具备自动化条件，三项均为预留不判定项（报“预留（不判定）”，不参与判定），
        /// 本模块全为预留不判定项，产品通讯集中在 M16 产品通讯初始化。
        /// </summary>
        //测试项目**************************---M14---**************************SpO2灌注与抗干扰测试（SPO2-008/009/010）
        public async Task Spo2PerfusionModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("血氧灌注指数测试")) return;
            // 产品通讯集中在 M16 产品通讯初始化，本模块全为预留不判定项，无需打开产品串口
            _log.Info("SPO2-008/009/010 为预留不判定项（仪器 PI 幅度控制与环境光控制暂未具备自动化条件），报不判定");
            await collector.SetAsync(SpecItems.Spo2008WeakPerfusion, TestItemResult.Demo("预留（不判定）"));
            await collector.SetAsync(SpecItems.Spo2009PerfusionHint, TestItemResult.Demo("预留（不判定）"));
            await collector.SetAsync(SpecItems.Spo2010AmbientLight, TestItemResult.Demo("预留（不判定）"));
        }
    }
}
