using TORNPISolution.Interface;
using TORNPISolution.Share.Enum;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// Spec 分部：整机工位（Category=WholeMachine）。
    /// 书写约定：#region 模块清单 里一个模块一段（//======Axx====== 开头），段与段之间不夹辅助方法；读配置与拼展示文本的辅助统一放文件末尾的 #region 取配置与展示辅助。
    /// </summary>
    public partial class Spec
    {
        /// <summary>整机工位目录：按书写顺序</summary>
        private IReadOnlyList<TestModuleModel> BuildWholeMachineModules() => new List<TestModuleModel>
        {
            BuildProductCommInit(),          // A16 产品通讯初始化
            BuildInitInstruments(),          // A00 初始化仪器
            BuildBpDeviceInit(),             // A01 血压仪初始化连接
            BuildNfcFunctionTest(),          // A02 NFC功能测试
            BuildBpAccuracyTest(),           // A03 测试启动与停止功能（BP-001/002）
            BuildPressureControlTest(),      // A04 压力控制功能测试（BP-006~009）
            BuildBpStaticAccuracyTest(),     // A05 血压静态压力精度测试（BP-003）
            BuildBpDynamicAccuracyTest(),    // A06 血压动态压力精度测试（BP-004）
            BuildBpHeartRateAccuracyTest(),  // A21 血压心率精度及重复性测试（BP-005）
            BuildAecg100Init(),              // A07 AECG100初始化连接
            BuildEcgAcquisitionTest(),       // A08 ECG采集与启动（ECG-001/002）
            BuildEcgHeartRateTest(),         // A09 ECG心率监测（ECG-004）
            BuildEcgSignalQualityTest(),     // A10 ECG动态范围和线性（ECG-009，幅度档×偏置档一项一点）
            BuildPpgHeartRateTest(),         // A11 PPG脉搏率模拟测试（PPG-001）
            BuildSpo2MeasurementTest(),      // A12 SpO2测量模块（SPO2-001~003）
            BuildSpo2AccuracyTest(),         // A13 SpO2精度检测（SPO2-004~006）
            BuildSpo2PerfusionTest(),        // A14 SpO2灌注与抗干扰测试（SPO2-008~010）
            BuildChargingTest(),             // A15 充电测试
            BuildProductStaticPressureCalibration(),   // A17 产品静态压力校准（60/120/180/240 逐点校准）
            BuildJjg692DeflationRateTest(),  // A19 JJG692 放气速率（听诊）（康伟达 0x04/0x12）
            BuildJjg692AirtightnessTest(),   // A20 JJG692 气密性（康伟达 0x04/0x13）
        };

        #region 模块清单（一个模块一段，按书写顺序）

        //======================================A00-----------初始化仪器
        private static TestModuleModel BuildInitInstruments()
        {
            var ti = AddTI(TestModuleIds.InitInstruments, ModuleCategory.WholeMachine, "初始化仪器",
                "初始化固高IO等硬件，全部成功为 PASS");

            AddResult(ti, name: SpecItems.GenIoInit,
                description: "启动 EtherCAT 总线",
                specMin: 1, specMax: 1, unit: "");

            return ti;
        }

        //======================================A16-----------产品通讯初始化
        // 进入产测模式（防休眠，产品断电前不再休眠）→ 0x72 → 0x74，全方案唯一执行产品通讯判定的模块；
        // 其余模块不再重复问询，产品可用性以本模块握手记录为准
        private static TestModuleModel BuildProductCommInit()
        {
            var ti = AddTI(TestModuleIds.ProductCommInit, ModuleCategory.WholeMachine, "产品通讯初始化",
                "进入产测模式（防休眠），读回 MAC、嵌软版本与当前状态");

            AddHandshakeItems(ti);
            return ti;
        }

        //======================================A01-----------血压仪初始化连接
        // 按仪器连接配置中选择的血压模拟仪初始化：选康伟达初始化康伟达，选鲸扬初始化 BPA700
        // BPA700 初始化时同时下发 Category/压力单位/袖带容量三项设置（默认 Adult/mmHg/500ml）
        private static TestModuleModel BuildBpDeviceInit()
        {
            var ti = AddTI(TestModuleIds.BpDeviceInit, ModuleCategory.WholeMachine, "血压仪初始化连接",
                "初始化配置里选中的血压模拟仪");

            AddResult(ti, name: SpecItems.BpSimulatorInit,
                description: "配置页面选择康伟达/鲸扬，未选择模拟仪则 FAIL",
                specMin: 1, specMax: 1, unit: "");

            return ti;
        }

        //======================================A02-----------NFC功能测试
        private static TestModuleModel BuildNfcFunctionTest()
        {
            var ti = AddTI(TestModuleIds.NfcFunctionTest, ModuleCategory.WholeMachine, "NFC功能测试",
                "读设备 NFC 标签，写入并验证 MAC 信息");

            AddResult(ti, name: SpecItems.NfcTagDetect,
                description: "NFC 读卡器读取到标签 UID",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.NfcMacWrite,
                description: "MAC 地址写入标签用户区",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.NfcMacVerify,
                description: "回读标签内容比对 MAC",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.NfcTagContentRead,
                description: "查看标签用户区内容",
                specMin: 1, specMax: 1, unit: "");

            return ti;
        }

        //======================================A03-----------测试启动与停止功能（BP-001/002）
        // 流程：弹提示让测试员短按产品[测量启动]/[测量停止]键 -> 等 5s -> 读设备端状态（0x74）
        //       -> 启动后应为 03 01（测量中）、停止后应为 03 00（待机）。整机自带气源，不驱动血压模拟仪。
        private static TestModuleModel BuildBpAccuracyTest()
        {
            var ti = AddTI(TestModuleIds.BpAccuracyTest, ModuleCategory.WholeMachine, "测试启动与停止功能",
                "短按启动键与停止键，按设备端状态判定");

            AddResult(ti, name: SpecItems.Bp001KeyStart,
                description: "提示后短按产品[测量启动]键，等 5s 读设备端状态（0x74），回读 03 01（测量中）为 PASS",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Bp002KeyStop,
                description: "提示后短按产品[测量停止]键，等 5s 读设备端状态（0x74），回读 03 00（待机）为 PASS",
                specMin: 1, specMax: 1, unit: "");

            return ti;
        }

        //======================================A04-----------压力控制功能测试（BP-006~009）
        // 四项均以产品串口回读压力判定产品放气/保护行为（充气由产品气泵完成，模拟仪只当手臂），未接产品判 FAIL
        private TestModuleModel BuildPressureControlTest()
        {
            var pc = GetPressureControlParams();
            var ti = AddTI(TestModuleIds.PressureControlTest, ModuleCategory.WholeMachine, "压力控制功能测试",
                "最高充气压力、匀速与快速放气、过压保护，按产品回读压力判定");

            AddResult(ti, name: SpecItems.Bp006MaxInflatePressure,
                description: "正常自动测量（0x79）全程回读压力的峰值，不应超过 300mmHg（与 BP-007 同一轮采集，含充放气压力曲线）",
                specMin: 0, specMax: 300, unit: "mmHg", waveform: WaveformPresentation.Inline,
                waveformUnit: "mmHg", waveformConfigSource: WaveformConfigSource.BloodPressureSimulator);
            AddResult(ti, name: SpecItems.Bp007UniformDeflation,
                description: "同一轮测量的匀速放气段平均速率 2~6mmHg/s（测试值=平均速率，分段速率只展示不判定）",
                specMin: 2, specMax: 6, unit: "mmHg/s", waveform: WaveformPresentation.Inline,
                waveformUnit: "mmHg", waveformConfigSource: WaveformConfigSource.BloodPressureSimulator);
            AddResult(ti, name: SpecItems.Bp008FastDeflation,
                description: $"回读达 {pc.BP008StopTriggerPressure:0}mmHg 时发 0x79 status=1 触发停止，从最高压降至 {pc.BP008EndPressure:0}mmHg 耗时 ≤10s（测试值=耗时；产品充不到触发压时从峰值回落也计时）",
                specMin: 0, specMax: 10, unit: "s", waveform: WaveformPresentation.Inline,
                waveformUnit: "mmHg", waveformConfigSource: WaveformConfigSource.BloodPressureSimulator);
            AddResult(ti, name: SpecItems.Bp009ResponseTime,
                description: $"0x75 充气，首次超 {pc.BP009ResponseStartPressure:0}mmHg 到 0x80 过压状态置位的间隔 ≤15s（0x80 全程未置位=保护未动作，按充气观察总时长判 FAIL；行详情只记录超压点/置位/回落/峰值）",
                specMin: 0, specMax: 15, unit: "s", waveform: WaveformPresentation.Inline,
                waveformUnit: "mmHg", waveformConfigSource: WaveformConfigSource.BloodPressureSimulator);
            AddResult(ti, name: SpecItems.Bp009ReliefDuration,
                description: "0x80 过压状态置位持续时长 ≤3s（压力骤降不能当证据：0x75 超时产品底层也会放气；未置位则不报告）",
                specMin: 0, specMax: 3, unit: "s", waveform: WaveformPresentation.None);

            return ti;
        }

        //======================================A17-----------产品静态压力校准
        // 流程：启动压力传感器（0x65）-> 逐点 60/120/180/240：模拟仪稳压 -> 发校准（0x66 + uint16 LE）-> 等 2s -> 下一点
        //       -> 全部测试项结束后关闭压力传感器（0x67）。逐点判定：|稳压值-校准点| ≤3mmHg 且校准有 ACK。
        private TestModuleModel BuildProductStaticPressureCalibration()
        {
            var ti = AddTI(TestModuleIds.ProductStaticPressureCalibration, ModuleCategory.WholeMachine, "产品静态压力校准",
                "模拟仪逐点稳压后下发校准，结束关闭压力传感器");

            AddResult(ti, name: SpecItems.PcalStartSensor,
                description: "启动压力传感器（0x07 0x65），有响应即通过",
                specMin: 1, specMax: 1, unit: "", hideSpecRange: true);

            foreach (var value in SpecItems.PcalPoints)
            {
                AddResult(ti, name: SpecItems.PcalPoint(value),
                    description: $"模拟仪稳压 {value:0.##}mmHg 后发校准（0x66），稳压误差 ≤3mmHg 且校准有 ACK（仪器列=稳压值）",
                    specMin: 0, specMax: 3, unit: "mmHg", valueSource: "Error");
            }

            AddResult(ti, name: SpecItems.PcalCloseSensor,
                description: "关闭压力传感器（0x07 0x67），有响应即通过",
                specMin: 1, specMax: 1, unit: "", hideSpecRange: true);

            return ti;
        }

        //======================================A19/A20-----------JJG692 放气速率（听诊）/气密性（康伟达 0x04 功能专属）

        private TestModuleModel BuildJjg692DeflationRateTest()
        {
            var dr = GetJjg692DeflationRateParams();
            var ti = AddTI(TestModuleIds.Jjg692DeflationRateTest, ModuleCategory.WholeMachine, "JJG692放气速率检测(听诊)",
                "设备匀速放气并回传漏气率判定（仅康伟达模拟仪）");

            AddResult(ti, name: SpecItems.Jjg692DeflationRate,
                description: $"测试值=设备回传漏气率（mmHg/s）；判定区间取配置（当前 {FormatRange(dr.MinRate, dr.MaxRate)}）（仪器列=漏气率/急释时间摘要）",
                specMin: dr.MinRate ?? 0, specMax: dr.MaxRate ?? 10, unit: "mmHg/s");

            return ti;
        }

        private TestModuleModel BuildJjg692AirtightnessTest()
        {
            var at = GetJjg692AirtightnessParams();
            var ti = AddTI(TestModuleIds.Jjg692AirtightnessTest, ModuleCategory.WholeMachine, "JJG692气密性检测",
                "设备稳压后回传漏气率判定（仅康伟达模拟仪）");

            AddResult(ti, name: SpecItems.Jjg692Airtightness,
                description: $"测试值=设备回传漏气率（mmHg/s）；合格上限取配置（当前 {FormatRange(null, at.MaxLeakRate)}）（仪器列=总时间/稳定压/漏气率摘要）",
                specMin: 0, specMax: at.MaxLeakRate ?? 10, unit: "mmHg/s");

            return ti;
        }

        //======================================A05-----------血压静态压力精度测试（BP-003）
        // 流程：逐点 BPARunStaticPressure（配置勾选的压力点） -> 稳压保持 ->
        //       读产品上报压力值（0x76） -> 产品值−标准值 落在 ±3mmHg 内（上报带符号，正=产品偏高）
        // 一点一项：项清单按当前配置勾选的静态压力点动态生成（项名见 SpecItems.Bp003Point），
        // 加载方案/打开模块库/点运行时 MainViewModel 会重建目录，改点无需重启。
        private TestModuleModel BuildBpStaticAccuracyTest()
        {
            var ti = AddTI(TestModuleIds.BpStaticAccuracyTest, ModuleCategory.WholeMachine, "血压静态压力精度测试",
                "逐点静态加压，一点一项，产品上报值与仪器稳压值的误差落在 ±3mmHg 内");

            foreach (var value in GetStaticPressurePoints())
            {
                AddResult(ti, name: SpecItems.Bp003Point(value),
                    description: $"静态加压 {value:0.##}mmHg，测试值=产品上报值−仪器稳压值（正=偏高），落在 ±3mmHg 判 PASS（仪器列=稳压值，产品列=上报值）",
                    specMin: -3, specMax: 3, unit: "mmHg", waveform: WaveformPresentation.Inline, valueSource: "Error",
                    waveformUnit: "mmHg", waveformConfigSource: WaveformConfigSource.BloodPressureSimulator);
            }

            return ti;
        }

        //======================================A06-----------血压动态压力精度测试（BP-004）
        // 流程：按配置压力组逐轮血压模拟（需储气瓶产生脉搏波）→ 产品 0x7B 回读收缩压/舒张压；
        // 项清单一个压力组一轮一项 + 每组一项重复性（项名 SpecItems.Bp004Round / Bp004Repeat，目录与执行端同源，改点位不重启）：
        //   轮次项  测试值 = max(|收缩压回读−设定|, |舒张压回读−设定|)；
        //   重复性项 测试值 = max(收缩压各轮极差, 舒张压各轮极差)，子表每轮一行 + 统计行；
        // 心率全模块只一个值（作模拟仪激励参数，不参与判定），不进项名也不分节。
        private TestModuleModel BuildBpDynamicAccuracyTest()
        {
            var cfg = GetBpAccuracyParams();
            // 与执行端同口径：只取勾选行、取整后去重（两行完全相同时项名会重名）
            var groups = cfg.Groups.Where(g => g.Enabled)
                .Select(g => ((int)Math.Round(g.Systolic), (int)Math.Round(g.Diastolic)))
                .Distinct().ToList();
            var repeats = Math.Max(3, cfg.Repeats);
            if (groups.Count == 0)
                _log.Warn("A06 目录构建：血压压力组未勾选任何行，BP-004 项清单为空（配置→动态血压→BP-004 压力组）");

            var ti = AddTI(TestModuleIds.BpDynamicAccuracyTest, ModuleCategory.WholeMachine, "血压动态压力精度测试",
                "多组压力各重复几轮，偏差与极差均 ≤2mmHg");

            foreach (var (sys, dia) in groups)
            {
                for (var r = 1; r <= repeats; r++)
                {
                    AddResult(ti, name: SpecItems.Bp004Round(sys, dia, r),
                        description: $"第 {r}/{repeats} 轮回读偏差",
                        specMin: 0, specMax: 2, unit: "mmHg", waveform: WaveformPresentation.Inline, valueSource: "Error",
                        waveformUnit: "mmHg", waveformConfigSource: WaveformConfigSource.BloodPressureSimulator);
                }

                AddResult(ti, name: SpecItems.Bp004Repeat(sys, dia),
                    description: $"{repeats} 轮极差",
                    specMin: 0, specMax: 2, unit: "mmHg");
            }

            return ti;
        }

        //======================================A21-----------血压心率精度及重复性测试（BP-005）
        // 压力只用一组（收缩/舒张），心率按配置勾选的档逐档测；一个心率点一轮一项 + 每点一项平均值统计（只展示不判定）；
        // 轮次项规格按该心率点现算 max(5, 设定×5%)（文档：±5 次/分 或 ±5% 取较大值）。
        private TestModuleModel BuildBpHeartRateAccuracyTest()
        {
            var cfg = GetBpHeartRateParams();
            var heartRates = cfg.HeartRates.Where(p => p.Enabled).Select(p => p.Value).Distinct().ToList();
            var repeats = Math.Max(3, cfg.Repeats);
            if (heartRates.Count == 0)
                _log.Warn("A21 目录构建：心率点未勾选任何行，BP-005 项清单为空（配置→动态血压→BP-005 心率点）");

            var ti = AddTI(TestModuleIds.BpHeartRateAccuracyTest, ModuleCategory.WholeMachine, "血压心率精度及重复性测试",
                "一组压力多档心率，逐轮判偏差；均值与极差只展示不判定");

            foreach (var hr in heartRates)
            {
                var tol = Math.Round(Math.Max(5, hr * 0.05), 1);
                for (var r = 1; r <= repeats; r++)
                {
                    AddResult(ti, name: SpecItems.Bp005Round(hr, r),
                        description: $"第 {r}/{repeats} 轮心率偏差",
                        specMin: 0, specMax: tol, unit: "BPM", waveform: WaveformPresentation.Inline, valueSource: "Error",
                        waveformUnit: "mmHg", waveformConfigSource: WaveformConfigSource.BloodPressureSimulator);
                }

                AddResult(ti, name: SpecItems.Bp005Average(hr),
                    description: $"{repeats} 轮平均",
                    specMin: 0, specMax: tol, unit: "BPM", specText: "仅统计");
            }

            return ti;
        }

        //======================================A07-----------AECG100 初始化连接
        private static TestModuleModel BuildAecg100Init()
        {
            var ti = AddTI(TestModuleIds.Aecg100Init, ModuleCategory.WholeMachine, "AECG100初始化连接",
                "初始化并连接 AECG100 综合性能测试仪");

            AddResult(ti, name: SpecItems.Aecg100InitConnection,
                description: "WTQInit + WTQConnect 连接成功",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Aecg100ModelName,
                description: "连接成功后读取设备型号（ProductName）",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Aecg100FirmwareVersion,
                description: "读取固件版本号（FW Major.Minor.Build）",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Aecg100SerialNumber,
                description: "读取设备序列号（SerialNumber）",
                specMin: 1, specMax: 1, unit: "");

            return ti;
        }

        //======================================A08-----------ECG 采集与启动（ECG-001/002）
        private static TestModuleModel BuildEcgAcquisitionTest()
        {
            var ti = AddTI(TestModuleIds.EcgAcquisitionTest, ModuleCategory.WholeMachine, "ECG采集与启动",
                "仪器输出 ECG 波形验证测量启动，读产品导联状态判脱落");

            AddResult(ti, name: SpecItems.Ppg1R525PresenceCheck,
                description: "检测绿光单 LED 模块在位（WTQGetPPGDeviceInformation），不在位则提示更换模块",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Ecg001Start,
                description: "AECG100 输出 ECG 波形（WTQOutputECG），SDK 返回成功即 PASS",
                specMin: 1, specMax: 1, unit: "", waveform: WaveformPresentation.Inline,
                waveformUnit: WaveformUnitMicroVolt, waveformConfigSource: WaveformConfigSource.Ecg100);
            AddResult(ti, name: SpecItems.Ecg002LeadOff,
                description: "输出波形后提示拔掉 LA 导联，产品上报导联脱落状态（0x88）即 PASS",
                specMin: 1, specMax: 1, unit: "", waveform: WaveformPresentation.Inline,
                waveformUnit: WaveformUnitMicroVolt, waveformConfigSource: WaveformConfigSource.Ecg100);

            return ti;
        }

        //======================================A09-----------ECG 心率监测（ECG-004）
        // ECG-004 心率检测精度：配置页设置心率点（默认 40/60/80/100/120/160 BPM），
        //   每档仪器输出对应频率 ECG 波形（BPM/60 Hz），重复 3 次取最大值，
        //   取最大偏差那一次报双值：仪器列=设定值、产品列=回读值，偏差上限取配置的 HeartRateTolerance；
        //   全程未取到一次有效回读时按 FAIL 报，不按“偏差 0”判定（否则没测也算过）
        private TestModuleModel BuildEcgHeartRateTest()
        {
            var hrTol = GetAecgParams().HeartRateTolerance;
            var ti = AddTI(TestModuleIds.EcgHeartRateTest, ModuleCategory.WholeMachine, "ECG心率监测",
                "多档心率逐档输出，产品回读心率与设定值比偏差");

            AddResult(ti, name: SpecItems.Ecg004HeartRateAccuracy,
                description: $"各心率点产品回读值应在 仪器设定值±{hrTol:0.#}BPM 内（容差取配置，仪器列=设定值，产品列=回读值）",
                specMin: 0, specMax: hrTol, unit: "BPM", waveform: WaveformPresentation.Inline, valueSource: "Both",
                waveformUnit: WaveformUnitMicroVolt, waveformConfigSource: WaveformConfigSource.Ecg100);

            return ti;
        }

        //======================================A10-----------ECG 动态范围和线性（仅 ECG-009）
        // 按 YY 9706.247-2021 201.12.4.4.101：AECG100 输出三角波（幅度=档值 波峰-波谷 mV，叠加 DC 偏置），产品回读曲线算全局波峰-波谷与设定幅度比对。
        // 一项一点，幅度档为外层、偏置档为内层（0.5mV 跑完三个偏置再跑 1mV）；项清单按配置动态生成（项名见 SpecItems.Ecg009Point），改档无需重启。
        // 允差分段口径统一由 Aecg100Settings.Ecg009ToleranceMv 实现（≤2mV→50μV，>2mV→10%）。
        private TestModuleModel BuildEcgSignalQualityTest()
        {
            var (cfg, amplitudes, offsets, frequency) = GetEcg009Params();
            if (amplitudes.Count == 0 || offsets.Count == 0)
                _log.Warn("A10 目录构建：ECG-009 幅度档或偏置档未填有效值，项清单为空（配置→AECG100→ECG-009 动态范围和线性）");

            var discard = cfg.Ecg009DiscardSeconds > 0 ? cfg.Ecg009DiscardSeconds : 20;
            var record = cfg.Ecg009RecordSeconds > 0 ? cfg.Ecg009RecordSeconds : 60;
            var ti = AddTI(TestModuleIds.EcgSignalQualityTest, ModuleCategory.WholeMachine, "ECG动态范围和线性",
                $"{amplitudes.Count} 幅度档 × {offsets.Count} 偏置档，一点一项比对回读波形幅度");

            ti.Overview = new WaveformOverviewProfile
            {
                Title = "ECG Signal",
                Unit = "mV",
                DefaultXWidthSeconds = 12,
                DefaultYMin = -5,
                DefaultYMax = 5,
                SeriesLabels = new[] { "AECG100 设定输出（三角波）", "产品回读（实测）" },
                StripDcByDefault = true,
                SegmentLabelAfter = "范围-",
            };

            foreach (var amplitude in amplitudes)
            {
                foreach (var offset in offsets)
                {
                    // 分段允差：小幅度档（≤2mV）用绝对 50μV，大幅度档用设定值 10%（与执行端调同一方法）
                    var tolerance = cfg.Ecg009ToleranceMv(amplitude);
                    AddResult(ti, name: SpecItems.Ecg009Point(amplitude, offset),
                        description: $"输出 {amplitude:0.##}mV 波峰-波谷三角波 + {offset:0.##}mV DC 偏置（{frequency:0.##}Hz）："
                            + $"丢弃前 {discard}s、取 {record}s 有效数据，全局波峰-波谷与设定幅度偏差 ≤{tolerance:0.###}mV"
                            + "（仪器列=设定幅度，产品列=回读波峰-波谷）",
                        specMin: 0, specMax: tolerance, unit: "mV", waveform: WaveformPresentation.Overview, valueSource: "Error",
                        waveformUnit: "mV", waveformConfigSource: WaveformConfigSource.Ecg100);
                }
            }

            return ti;
        }

        //======================================A11-----------PPG 脉搏率模拟测试（需 PPG-1R-525 绿光模块）
        private TestModuleModel BuildPpgHeartRateTest()
        {
            var hrTol = GetAecgParams().PpgHeartRateTolerance;
            var ti = AddTI(TestModuleIds.PpgHeartRateTest, ModuleCategory.WholeMachine, "PPG脉搏率模拟测试",
                "脉搏率精度，产品回读比对（需 PPG-1R-525 绿光模块在位）");

            AddResult(ti, name: SpecItems.Ppg1R525PresenceCheck,
                description: "检测绿光单 LED 模块在位（WTQGetPPGDeviceInformation），不在位则后续测试项跳过并提示更换模块",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Ppg001PulseRateAccuracy,
                description: $"逐档输出 PPG 波形（默认 60/80/100/120 BPM），产品回读值应在 设定值±{hrTol:0.#}BPM 内（容差取配置，仪器列=设定值，产品列=回读值）",
                specMin: 0, specMax: hrTol, unit: "BPM", waveform: WaveformPresentation.Inline, valueSource: "Both",
                waveformUnit: WaveformUnitMicroVolt, waveformConfigSource: WaveformConfigSource.Ecg100);

            return ti;
        }

        //======================================A12-----------SpO2 测量模块（需 PPG-2R-940 红光+红外模块）
        private static TestModuleModel BuildSpo2MeasurementTest()
        {
            var ti = AddTI(TestModuleIds.Spo2MeasurementTest, ModuleCategory.WholeMachine, "SpO2测量模块",
                "测量启停、手指脱落检测、低氧报警（需 PPG-2R-940 红光+红外模块在位）");

            AddResult(ti, name: SpecItems.Ppg2R940PresenceCheck,
                description: "检测红光+红外双 LED 模块在位（WTQGetPPGDeviceInformation），不在位则后续测试项跳过并提示更换模块",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Spo2001StartStop,
                description: "输出红光/红外双通道模拟信号启动测量后停止输出，仪器输出启停控制正常即 PASS（产品状态回读预留）",
                specMin: 1, specMax: 1, unit: "", waveform: WaveformPresentation.Inline,
                waveformUnit: WaveformUnitMicroVolt, waveformConfigSource: WaveformConfigSource.Ecg100);
            AddResult(ti, name: SpecItems.Spo2002FingerOff,
                description: "正常输出后停止信号模拟手指脱落，仪器输出控制正常即 PASS（产品脱落判定回读预留）",
                specMin: 1, specMax: 1, unit: "", waveform: WaveformPresentation.Inline,
                waveformUnit: WaveformUnitMicroVolt, waveformConfigSource: WaveformConfigSource.Ecg100);
            AddResult(ti, name: SpecItems.Spo2003LowOxygenAlarm,
                description: "[预留] 输出低于 SpO2 报警阈值的信号，验证产品触发低氧报警（产品报警回读协议未定稿，结果不判定）",
                specMin: 1, specMax: 1, unit: "");

            return ti;
        }

        //======================================A13-----------SpO2 精度检测
        // 对应测试规范 2.2.2：SPO2-004 血氧饱和度精度 / SPO2-005 脉搏率精度 / SPO2-006 低氧范围精度。
        // 复用 AECG100 红/红外双通道输出（WTQOutputPPGEx）+ 产品回读 0x82/0x7C 比对，
        // 仪器列=设定值、产品列=回读值，动态规格=设定值±容差。
        private TestModuleModel BuildSpo2AccuracyTest()
        {
            var cfg = GetAecgParams();
            var ti = AddTI(TestModuleIds.Spo2AccuracyTest, ModuleCategory.WholeMachine, "SpO2精度检测",
                "血氧饱和度、脉搏率、低氧范围三项精度（需 PPG-2R-940 红光+红外模块在位）");

            AddResult(ti, name: SpecItems.Spo2004SaturationAccuracy,
                description: $"逐点（默认 75/80/90/95/100%）输出红光/红外双通道信号，产品回读值应在 设定值±{cfg.Spo2Tolerance:0.#}% 内（容差取配置，仪器列=设定值，产品列=回读值）",
                specMin: 0, specMax: cfg.Spo2Tolerance, unit: "%", waveform: WaveformPresentation.Inline, valueSource: "Both",
                waveformUnit: WaveformUnitMicroVolt, waveformConfigSource: WaveformConfigSource.Ecg100);
            AddResult(ti, name: SpecItems.Spo2005PulseRateAccuracy,
                description: $"逐脉搏率点（默认 40/60/80/100/120/160BPM）输出双通道信号，产品回读脉搏率应在 设定值±{cfg.Spo2PulseRateTolerance:0.#}BPM 内（容差取配置，仪器列=设定值，产品列=回读值）",
                specMin: 0, specMax: cfg.Spo2PulseRateTolerance, unit: "BPM", waveform: WaveformPresentation.Inline, valueSource: "Both",
                waveformUnit: WaveformUnitMicroVolt, waveformConfigSource: WaveformConfigSource.Ecg100);
            AddResult(ti, name: SpecItems.Spo2006LowRangeAccuracy,
                description: $"逐点（默认 50/60/70%）输出低氧范围双通道信号，产品回读值应在 设定值±{cfg.Spo2LowRangeTolerance:0.#}% 内（容差取配置，仪器列=设定值，产品列=回读值）",
                specMin: 0, specMax: cfg.Spo2LowRangeTolerance, unit: "%", waveform: WaveformPresentation.Inline, valueSource: "Both",
                waveformUnit: WaveformUnitMicroVolt, waveformConfigSource: WaveformConfigSource.Ecg100);

            return ti;
        }

        //======================================A14-----------SpO2 灌注与抗干扰测试
        // PI（灌注指数）幅度控制与环境光控制暂未具备自动化条件，三项为预留不判定项（模块执行时报“预留（不判定）”）。
        private static TestModuleModel BuildSpo2PerfusionTest()
        {
            var ti = AddTI(TestModuleIds.Spo2PerfusionTest, ModuleCategory.WholeMachine, "SpO2灌注与抗干扰测试",
                "弱灌注能力与提示、环境光抗干扰（三项暂不判定，执行时报预留）");

            AddResult(ti, name: SpecItems.Spo2008WeakPerfusion,
                description: "[预留] 逐灌注指数点（PI=0.3%/0.7%/1%）输出双通道信号，验证产品可正常测量（仪器 PI 幅度控制预留，结果不判定）",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Spo2009PerfusionHint,
                description: "[预留] 输出 PI<0.3% 信号，验证产品给出弱灌注提示（仪器 PI 幅度控制 + 产品提示回读预留，结果不判定）",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Spo2010AmbientLight,
                description: "[预留] 强环境光下测量（环境光控制与产品回读均预留，结果不判定）",
                specMin: 1, specMax: 1, unit: "", specText: "不判定");

            return ti;
        }

        //======================================A15-----------充电测试
        private static TestModuleModel BuildChargingTest()
        {
            var ti = AddTI(TestModuleIds.ChargingTest, ModuleCategory.WholeMachine, "充电测试",
                "读充电状态、当前电量与电池百分比");

            AddResult(ti, name: SpecItems.ChargingStatus,
                description: "读取产品充电状态（0x6D），1=充电中 0=未充电",
                specMin: 1, specMax: 1, unit: "", valueSource: "Product");
            AddResult(ti, name: SpecItems.BatteryVoltage,
                description: "读取产品电池电压（0x6C，uint32 LE，单位 mV）",
                specMin: 3000, specMax: 4500, unit: "mV", valueSource: "Product");
            AddResult(ti, name: SpecItems.BatteryPercent,
                description: "读取产品电池百分比（0x6C，第 5 字节，单位 %）",
                specMin: 0, specMax: 100, unit: "%", valueSource: "Product");

            return ti;
        }

        #endregion

        #region 取配置与展示辅助（上面各 Build 方法共用，不定义测试项）

        /// <summary>配置页的 BP-004 压力组（A06 项清单来源；读不到配置时回退默认）</summary>
        private BpaBpAccuracyParams GetBpAccuracyParams()
        {
            try { return _configService.Load().ActiveBpTestParams.BpAccuracy; }
            catch (Exception ex) { _log.Warn($"读取 BP-004 压力组配置失败，用默认参数：{ex.Message}"); return new BpaBpAccuracyParams(); }
        }

        /// <summary>配置页的 BP-005 心率点（A21 项清单来源；读不到配置时回退默认）</summary>
        private BpaHeartRateAccuracyParams GetBpHeartRateParams()
        {
            try { return _configService.Load().ActiveBpTestParams.BpHeartRate; }
            catch (Exception ex) { _log.Warn($"读取 BP-005 心率点配置失败，用默认参数：{ex.Message}"); return new BpaHeartRateAccuracyParams(); }
        }

        /// <summary>配置页的 BP-006~009 过程参数（A04 描述里的触发压力/终点压力/起算压力来源）：读不到配置时回退默认值</summary>
        private BpaPressureControlParams GetPressureControlParams()
        {
            try { return _configService.Load().ActiveBpTestParams.PressureControl; }
            catch (Exception ex) { _log.Warn($"读取压力控制过程参数配置失败，用默认参数：{ex.Message}"); return new BpaPressureControlParams(); }
        }

        /// <summary>当前配置勾选的静态压力点：口径在 SpecItems.StaticPressurePoints（与执行端同一份算法），这里只管读不到配置时兜底不抛</summary>
        private List<double> GetStaticPressurePoints()
        {
            try
            {
                return SpecItems.StaticPressurePoints(_configService.Load());
            }
            catch (Exception ex)
            {
                _log.Warn($"读取静态压力点配置失败（A05 项清单为空）：{ex.Message}");
                return new List<double>();
            }
        }

        /// <summary>配置页的 JJG692 放气速率参数组（读不到配置时回退默认值，保证目录构建不抛）</summary>
        private Jjg692DeflationRateParams GetJjg692DeflationRateParams()
        {
            try { return _configService.Load().ActiveBpTestParams.Jjg692.DeflationRate; }
            catch (Exception ex) { _log.Warn($"读取 JJG692 放气速率配置失败，用默认参数：{ex.Message}"); return new Jjg692DeflationRateParams(); }
        }

        /// <summary>配置页的 JJG692 气密性参数组（读不到配置时回退默认值）</summary>
        private Jjg692AirtightnessParams GetJjg692AirtightnessParams()
        {
            try { return _configService.Load().ActiveBpTestParams.Jjg692.Airtightness; }
            catch (Exception ex) { _log.Warn($"读取 JJG692 气密性配置失败，用默认参数：{ex.Message}"); return new Jjg692AirtightnessParams(); }
        }

        /// <summary>AECG100 测试参数（A09/A11/A13 那三项容差的唯一来源）：读不到配置时回退默认对象，保证目录构建不抛</summary>
        private Aecg100Settings GetAecgParams()
        {
            try { return _configService.Load().Aecg100Tester; }
            catch (Exception ex) { _log.Warn($"读取 AECG100 测试参数配置失败，用默认参数：{ex.Message}"); return new Aecg100Settings(); }
        }

        /// <summary>ECG-009 目录参数（配置对象 + 幅度档/偏置档/频率）：读不到配置时回退默认对象，保证目录构建不抛</summary>
        private (Share.Models.Aecg100Settings Cfg, List<double> Amplitudes, List<int> Offsets, double Frequency) GetEcg009Params()
        {
            try
            {
                var cfg = _configService.Load().Aecg100Tester;
                return (cfg, CleanEcg009Amplitudes(cfg.Ecg009Amplitudes), cfg.Ecg009DcOffsets ?? new List<int>(),
                    cfg.Ecg009Frequency > 0 ? cfg.Ecg009Frequency : 6.25);
            }
            catch (Exception ex)
            {
                _log.Warn($"读取 ECG-009 配置失败，用默认参数：{ex.Message}");
                var fallback = new Share.Models.Aecg100Settings();
                return (fallback, CleanEcg009Amplitudes(fallback.Ecg009Amplitudes),
                    new List<int> { 0, 300, -300 }, 6.25);
            }
        }

        /// <summary>幅度档清洗：去 ≤0 的无效档、去重（与执行端同一口径，两边项清单才能一致）</summary>
        private static List<double> CleanEcg009Amplitudes(List<double>? amplitudes)
            => amplitudes?.Where(a => a > 0).Distinct().ToList() ?? new List<double>();

        /// <summary>判定区间展示文本：null 端显示“不限”（提醒用户规格值还需在配置页填写）</summary>
        private static string FormatRange(double? min, double? max)
            => min == null && max == null
                ? "未填写，仅报实测值不判定"
                : $"{min?.ToString("0.###") ?? "不限"} ~ {max?.ToString("0.###") ?? "不限"} mmHg/s";

        #endregion
    }
}
