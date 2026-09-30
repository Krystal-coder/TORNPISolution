using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 规格类：每模块一个 Build 方法，GetAllModules 是模块目录。
    /// 测试项名称统一引用 <see cref="SpecItems"/> 常量（项名唯一来源，TestItem 上报同引用）。
    /// 动态规格怎么定义：这里写的 SpecMin/SpecMax 只是静态规格（默认/兜底）；需按仪器读回值现算规格的项（如血压：仪器值±容差）不改这里，由 TestItem 运行时把动态规格随 TestItemResult 带回，UI 判定优先用动态规格，未测到才用这里的静态值。
    /// </summary>
    public class Spec : ITestModuleService
    {
        private readonly ILogService _log = new LogService(nameof(Spec));
        private readonly IInstrumentConfigService _configService;

        /// <param name="configService">仪器配置服务：M05 项清单按当前勾选的静态压力点动态生成（一点一项）</param>
        public Spec(IInstrumentConfigService configService) => _configService = configService;

        /// <summary>
        /// 嵌软版本管控规格（M16“产品信息获取(嵌软版本)”项的规格与判定唯一来源）：
        /// 产品读回版本与它不一致判 FAIL（防烧录老版本）；版本升级时只改这一处，规格列显示与判定同步生效。
        /// </summary>
        public const string ExpectedFirmwareVersion = "V0.1.POR264002";

        public const string FirmwareStatus = "03-00";
        /// <summary>
        /// 模块目录：按书写顺序返回全部测试模块定义（顺序即新建方案时的默认顺序，可拖拽调整）。
        /// 每个模块的项定义见对应的 Build 方法。
        /// </summary>
        public IReadOnlyList<TestModuleModel> GetAllModules()
        {
            _log.Info($"{DateTime.Now} Start calling the Spec.GetAllModules() method . {Environment.NewLine}");
            try
            {
                return new List<TestModuleModel>
                {
                    BuildProductCommInit(),          // M16 产品通讯初始化（进产测模式+0x72/0x74，全方案仅一次，建议放方案开头）
                    BuildInitInstruments(),          // M00 初始化仪器
                    BuildBpDeviceInit(),             // M01 血压仪初始化连接
                    BuildNfcFunctionTest(),          // M02 NFC功能测试
                    BuildBpAccuracyTest(),           // M03 产品充放气测试（BP-001/002）
                    BuildBpStaticAccuracyTest(),     // M05 血压静态压力精度测试（BP-003）
                    BuildBpDynamicAccuracyTest(),    // M06 血压动态压力精度测试（BP-004/005）
                    BuildPressureControlTest(),      // M04 压力控制功能测试（BP-006~009）
                    BuildAecg100Init(),              // M07 AECG100初始化连接
                    BuildEcgAcquisitionTest(),       // M08 ECG采集与启动（ECG-001/002）
                    BuildEcgHeartRateTest(),         // M09 ECG心率监测（ECG-004）
                    BuildEcgSignalQualityTest(),     // M10 ECG信号质量与波形（ECG-009~012）
                    BuildPpgHeartRateTest(),         // M11 PPG脉搏率模拟测试（PPG-001）
                    BuildSpo2MeasurementTest(),      // M12 SpO2测量模块（SPO2-001~003）
                    BuildSpo2AccuracyTest(),         // M13 SpO2精度检测（SPO2-004~006）
                    BuildSpo2PerfusionTest(),        // M14 SpO2灌注与抗干扰测试（SPO2-008~010）
                    BuildChargingTest(),           // M15 充电测试
                    BuildProductStaticPressureCalibration(), // M17 产品静态压力校准（60/120/180/240 逐点校准）
                };
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in Spec.class GetAllModules method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the Spec.GetAllModules() method . {Environment.NewLine}");
            }
        }

        //======================================M00-----------初始化仪器
        private static TestModuleModel BuildInitInstruments()
        {
            var ti = AddTI(TestModuleIds.InitInstruments, "初始化仪器", "\uE90F",
                "初始化固高IO模块等硬件，全部初始化成功为 PASS");

            AddResult(ti, name: SpecItems.GenIoInit,          // ResultName
                description: "启动 EtherCAT 总线",
                specMin: 1, specMax: 1, unit: "");          // SpecMin / SpecMax（1=成功 0=失败，成功才 PASS）

            return ti;
        }

        //======================================M16-----------产品通讯初始化
        // 进入产测模式（防休眠，产品断电前不再休眠）→ 0x72 → 0x74，全方案唯一执行产品通讯判定的模块；
        // 其余模块不再重复问询，产品可用性以本模块握手记录为准
        private static TestModuleModel BuildProductCommInit()
        {
            var ti = AddTI(TestModuleIds.ProductCommInit, "产品通讯初始化", "\uE95E",
                "进入产测模式（防休眠）+ 0x72 产品信息获取（MAC/嵌软版本）+ 0x74 状态查询");

            AddHandshakeItems(ti);
            return ti;
        }

        //======================================M01-----------血压仪初始化连接
        // 按仪器连接配置中选择的血压模拟仪初始化：选康伟达初始化康伟达，选鲸扬初始化 BPA700
        // BPA700 初始化时同时下发 Category/压力单位/袖带容量三项设置（默认 Adult/mmHg/500ml）
        private static TestModuleModel BuildBpDeviceInit()
        {
            var ti = AddTI(TestModuleIds.BpDeviceInit, "血压仪初始化连接", "\uE95E",
                "按仪器连接配置初始化选中的血压模拟仪（康伟达/鲸扬 BPA700）");

            AddResult(ti, name: SpecItems.BpSimulatorInit,          // ResultName
                description: "配置页面选择康伟达/鲸扬，未选择模拟仪则 FAIL",
                specMin: 1, specMax: 1, unit: "");          // SpecMin / SpecMax（1=成功 0=失败，成功才 PASS）

            return ti;
        }

        //======================================M02-----------NFC功能测试
        private static TestModuleModel BuildNfcFunctionTest()
        {
            var ti = AddTI(TestModuleIds.NfcFunctionTest, "NFC功能测试", "\uE779",
                "使用 NFC 读卡器读取设备 NFC 标签，写入并验证 MAC 信息");

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

        //======================================M03-----------产品充放气测试（BP-001/002）
        // 流程：BPARunBPTest 进入待测 -> 测试员按产品物理键（开始/停止） ->
        //       通过 BPA700 回调 status 序列与压力曲线验证结果
        private static TestModuleModel BuildBpAccuracyTest()
        {
            var ti = AddTI(TestModuleIds.BpAccuracyTest, "产品充放气测试", "\uE95E",
                "BP-001 物理按键启动测量 / BP-002 测量过程中手动停止（需连接储气瓶）");

            AddResult(ti, name: SpecItems.Bp001KeyStart,
                description: "按产品[开始]键充气测量，BPA700 status 依次 Busy→Stable→Finish 为 PASS",
                specMin: 1, specMax: 1, unit: "", showWaveform: true);
            AddResult(ti, name: SpecItems.Bp002ManualStop,
                description: "充气中按产品[停止]键，立即停止充气并快速放气，放气时间 ≤配置上限（血压仪参数配置→充放气规格）",
                specMin: 0, specMax: 10, unit: "s", showWaveform: true);

            return ti;
        }

        //======================================M17-----------产品静态压力校准
        // 流程：启动压力传感器（0x65）-> 逐点 60/120/180/240：模拟仪稳压 -> 发校准（0x66 + uint16 LE）-> 等 2s -> 下一点
        //       -> 全部测试项结束后关闭压力传感器（0x67）。逐点判定：|稳压值-校准点| ≤3mmHg 且校准有 ACK。
        private TestModuleModel BuildProductStaticPressureCalibration()
        {
            var ti = AddTI(TestModuleIds.ProductStaticPressureCalibration, "产品静态压力校准", "\uE95E",
                "产品压力传感器校准：模拟仪逐点稳压 60/120/180/240mmHg 后发 0x66 校准（每点间隔 2s），结束关闭传感器");

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

        //======================================M05-----------血压静态压力精度测试（BP-003）
        // 流程：逐点 BPARunStaticPressure（配置勾选的压力点） -> 稳压保持 ->
        //       读产品上报压力值（0x76） -> |产品值-标准值|≤3mmHg
        // 一点一项：项清单按当前配置勾选的静态压力点动态生成（项名见 SpecItems.Bp003Point），
        // 加载方案/打开模块库/点运行时 MainViewModel 会重建目录，改点无需重启。
        private TestModuleModel BuildBpStaticAccuracyTest()
        {
            var ti = AddTI(TestModuleIds.BpStaticAccuracyTest, "血压静态压力精度测试", "\uE95E",
                "BP-003：逐点静态加压（压力点取配置，一点一项），产品上报值与标准值误差 ≤3mmHg（含压力曲线）");

            foreach (var value in GetStaticPressurePoints())
            {
                AddResult(ti, name: SpecItems.Bp003Point(value),
                    description: $"静态加压 {value:0.##}mmHg，产品上报值与仪器稳压值误差 ≤3mmHg（仪器列=稳压值，产品列=上报值）",
                    specMin: -3, specMax: 3, unit: "mmHg", showWaveform: true, valueSource: "Error");
            }

            return ti;
        }

        /// <summary>当前配置勾选的静态压力点（去重、保持配置顺序；与执行端 BpStaticAccuracyModule 同一口径）</summary>
        private List<double> GetStaticPressurePoints()
        {
            try
            {
                return _configService.Load().ActiveBpTestParams.StaticPressure.Points
                    .Where(p => p.Enabled).Select(p => p.Value).Distinct().ToList();
            }
            catch (Exception ex)
            {
                _log.Warn($"读取静态压力点配置失败（M05 项清单为空）：{ex.Message}");
                return new List<double>();
            }
        }

        //======================================M06-----------血压动态压力精度测试（BP-004/BP-005）
        // 流程：BPARunBPTest 多组血压/心率参数 × 每组3次（需储气瓶产生脉搏波） ->
        //       产品上报收缩压/舒张压/心率（0x7B 推送） -> 极差与系统偏差判定
        private static TestModuleModel BuildBpDynamicAccuracyTest()
        {
            var ti = AddTI(TestModuleIds.BpDynamicAccuracyTest, "血压动态压力精度测试", "\uE95E",
                "BP-004 血压精度/重复性（极差≤2 偏差≤3） + BP-005 心率精度/重复性（需连接储气瓶）");

            AddResult(ti, name: SpecItems.Bp004SystolicAccuracy,
                description: "4组血压×3次，|收缩压均值-设定值| 系统偏差 ≤3mmHg（测试值=最大偏差；分组设定值 vs 产品回读均值见行内子表，含充放气压力曲线）",
                specMin: 0, specMax: 3, unit: "mmHg", showWaveform: true);
            AddResult(ti, name: SpecItems.Bp004SystolicRepeat,
                description: "4组血压×3次，收缩压极差 |最大-最小| ≤2mmHg（测试值=最大极差；分组最大/最小回读见行内子表）",
                specMin: 0, specMax: 2, unit: "mmHg");
            AddResult(ti, name: SpecItems.Bp004DiastolicAccuracy,
                description: "4组血压×3次，|舒张压均值-设定值| 系统偏差 ≤3mmHg（测试值=最大偏差；分组设定值 vs 产品回读均值见行内子表）",
                specMin: 0, specMax: 3, unit: "mmHg");
            AddResult(ti, name: SpecItems.Bp004DiastolicRepeat,
                description: "4组血压×3次，舒张压极差 |最大-最小| ≤2mmHg（测试值=最大极差；分组最大/最小回读见行内子表）",
                specMin: 0, specMax: 2, unit: "mmHg");
            AddResult(ti, name: SpecItems.Bp005HeartRateAccuracy,
                description: "4组心率×3次，|心率均值-设定值| ≤ max(5, 设定值×5%)（测试值=最大偏差；分组判定见行内子表）",
                specMin: 0, specMax: 6, unit: "BPM");
            AddResult(ti, name: SpecItems.Bp005HeartRateRepeat,
                description: "4组心率×3次，心率极差 |最大-最小| ≤ max(5, 设定值×5%)（测试值=最大极差；分组判定见行内子表）",
                specMin: 0, specMax: 6, unit: "BPM");

            return ti;
        }

        //======================================M04-----------压力控制功能测试（BP-006~008）
        // BP-006~008 三项均以产品串口回读压力判定产品放气/保护行为，未接产品判 FAIL
        private static TestModuleModel BuildPressureControlTest()
        {
            var ti = AddTI(TestModuleIds.PressureControlTest, "压力控制功能测试", "\uE713",
                "BP-006~008：匀速放气控制/快速放气控制/过压保护（三项均以产品串口回读压力判定，未接产品判 FAIL）");

            AddResult(ti, name: SpecItems.Bp006UniformDeflation,
                description: "产品测量周期匀速放气段，串口回读压力计算平均放气速率 2~6mmHg/s（测试值=平均速率，含充放气压力曲线）",
                specMin: 2, specMax: 6, unit: "mmHg/s", showWaveform: true);
            AddResult(ti, name: SpecItems.Bp007FastDeflation,
                description: "产品充气到触发压力后发停止（0x7D）快速放气，串口回读压力计时归零 ≤10s（测试值=归零耗时）",
                specMin: 0, specMax: 10, unit: "s", showWaveform: true);
            AddResult(ti, name: SpecItems.Bp008Overpressure,
                description: "充气过 300 冲至 320，产品应触发过压保护直接快速放气（串口回读骤降点=触发压力 300~320mmHg）",
                specMin: 300, specMax: 320, unit: "mmHg", showWaveform: true);

            return ti;
        }

        //======================================M07-----------AECG100 初始化连接
        // 通过原生 SDK（AECG100x64.dll）初始化并连接 AECG100 多功能综合性能测试仪
        private static TestModuleModel BuildAecg100Init()
        {
            var ti = AddTI(TestModuleIds.Aecg100Init, "AECG100初始化连接", "\uE90F",
                "初始化并连接鲸扬 AECG100 多功能综合性能测试仪（USB VCP + 原生 SDK）");

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

        //======================================M08-----------ECG 采集与启动（ECG-001/002）
        // 产品通讯集中在 M16，本模块不再问询；
        // ECG-001 测量启动：仪器输出 ECG 波形，验证 SDK 返回成功；
        // ECG-002 导联脱落检测：读取产品导联状态（0x88）判定脱落。
        private static TestModuleModel BuildEcgAcquisitionTest()
        {
            var ti = AddTI(TestModuleIds.EcgAcquisitionTest, "ECG采集与启动", "\uE95E",
                "ECG-001 测量启动 / ECG-002 导联脱落检测（产品串口通讯预留，先控制仪器）");

            AddResult(ti, name: SpecItems.Ppg1R525PresenceCheck,
                description: "检测绿光单 LED 模块在位（WTQGetPPGDeviceInformation），不在位则提示更换模块",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Ecg001Start,
                description: "AECG100 输出 ECG 波形（WTQOutputECG），SDK 返回成功即 PASS",
                specMin: 1, specMax: 1, unit: "", showWaveform: true);
            AddResult(ti, name: SpecItems.Ecg002LeadOff,
                description: "仪器侧判断导联是否脱落（预留，脱落不采集数据判 FAIL）",
                specMin: 1, specMax: 1, unit: "", showWaveform: true);

            return ti;
        }

        //======================================M09-----------ECG 心率监测（ECG-004）
        // 产品通讯集中在 M16，本模块不再问询；
        // ECG-004 心率检测精度：配置页设置心率点（默认 40/60/80/100/120/160 BPM），
        //   每档仪器输出对应频率 ECG 波形（BPM/60 Hz），重复 3 次取最大值，
        //   仅验证仪器设置下发成功（产品回传比对预留）
        private static TestModuleModel BuildEcgHeartRateTest()
        {
            var ti = AddTI(TestModuleIds.EcgHeartRateTest, "ECG心率监测", "\uE95E",
                "ECG-004 心率检测精度（多档心率设置，误差 ≤±2BPM，产品回传预留）");

            AddResult(ti, name: SpecItems.Ecg004HeartRateAccuracy,
                description: "各心率点产品回读值应在 仪器设定值±2BPM 内（仪器列=设定值，产品列=回读值）",
                specMin: 0, specMax: 2, unit: "BPM", showWaveform: true, valueSource: "Both");

            return ti;
        }

        //======================================M10-----------ECG 信号质量与波形（ECG-009~012）
        // 按 YY 9706.247-2021 标准条款，AECG100 发出标准测试信号，产品回传测量值后由上位机判定。
        // 当前阶段：产品回传比对逻辑预留，默认 PASS。
        // TODO: 产品串口协议定稿后，补充各测试项的产品回传读取与判定逻辑。
        // 注意：本模块无产品通讯项（历史上就没定义，保持不变）
        private static TestModuleModel BuildEcgSignalQualityTest()
        {
            var ti = AddTI(TestModuleIds.EcgSignalQualityTest, "ECG信号质量与波形", "\uE95E",
                "ECG-009~012：按 YY 9706.247-2021 标准，AECG100 发出标准信号，产品回传判定（协议预留）");

            AddResult(ti, name: SpecItems.Ecg009aDynamicRangeNoBias,
                description: "标准 201.12.4.4.101：输出 6mV 三角波，无 DC 偏置，DUT 回放幅度衰减 < 50μV（产品回传预留）",
                specMin: 0, specMax: 50, unit: "μV", showWaveform: true);
            AddResult(ti, name: SpecItems.Ecg009bDynamicRangePlus300,
                description: "标准 201.12.4.4.101：输出 6mV 三角波 + +300mV DC 偏置，DUT 回放幅度衰减 < 50μV（产品回传预留）",
                specMin: 0, specMax: 50, unit: "μV", showWaveform: true);
            AddResult(ti, name: SpecItems.Ecg009cDynamicRangeMinus300,
                description: "标准 201.12.4.4.101：输出 6mV 三角波 + -300mV DC 偏置，DUT 回放幅度衰减 < 50μV（产品回传预留）",
                specMin: 0, specMax: 50, unit: "μV", showWaveform: true);
            AddResult(ti, name: SpecItems.Ecg010InputImpedance,
                description: "标准 201.12.4.4.102：输出 10Hz 5mV 正弦波，开关断开/闭合振幅变化 ≤ 6% → 输入阻抗 > 10MΩ（产品回传 + 外部开关控制预留）",
                specMin: 0, specMax: 6, unit: "%", showWaveform: true);
            AddResult(ti, name: SpecItems.Ecg011GainAccuracy,
                description: "标准 201.12.4.4.104：输出 5Hz 2mV 正弦波，各增益档幅度误差 ≤ ±10%（产品回传预留）",
                specMin: 0, specMax: 10, unit: "%", showWaveform: true);
            AddResult(ti, name: SpecItems.Ecg012FrequencyResponse,
                description: "标准 201.12.4.4.108：0.67Hz~40Hz 逐频点输出 2mV 正弦波，各频点响应在 5Hz 响应的 70%~140%（产品回传预留）",
                specMin: 70, specMax: 140, unit: "%", showWaveform: true);

            return ti;
        }

        //======================================M11-----------PPG 脉搏率模拟测试（需 PPG-1R-525 绿光模块）
        // 测前模块在位校验：检测不在位或模块不对直接拦截，提示更换为 PPG-1R-525。
        // PPG-001：仪器经绿光 LED 输出 PPG 波形（频率 = BPM/60），产品光学传感器计算脉搏率后比对。
        private static TestModuleModel BuildPpgHeartRateTest()
        {
            var ti = AddTI(TestModuleIds.PpgHeartRateTest, "PPG脉搏率模拟测试", "\uE95E",
                "PPG-001 脉搏率精度（需 PPG-1R-525 绿光模块在位，产品回读 0x7C 比对）");

            AddResult(ti, name: SpecItems.Ppg1R525PresenceCheck,
                description: "检测绿光单 LED 模块在位（WTQGetPPGDeviceInformation），不在位则后续测试项跳过并提示更换模块",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Ppg001PulseRateAccuracy,
                description: "逐档输出 PPG 波形（默认 60/80/100/120 BPM），产品回读值应在 设定值±2BPM 内（仪器列=设定值，产品列=回读值）",
                specMin: 0, specMax: 2, unit: "BPM", showWaveform: true, valueSource: "Both");

            return ti;
        }

        //======================================M12-----------SpO2 测量模块（需 PPG-2R-940 红光+红外模块）
        // 测前模块在位校验：检测不在位或模块不对直接拦截，提示更换为 PPG-2R-940。
        // 对应测试规范 2.2.1：SPO2-001 测量启动与停止 / SPO2-002 手指脱落检测 / SPO2-003 低氧报警。
        private static TestModuleModel BuildSpo2MeasurementTest()
        {
            var ti = AddTI(TestModuleIds.Spo2MeasurementTest, "SpO2测量模块", "\uE95E",
                "SPO2-001 测量启动与停止 / SPO2-002 手指脱落检测 / SPO2-003 低氧报警（需 PPG-2R-940 红光+红外模块在位）");

            AddResult(ti, name: SpecItems.Ppg2R940PresenceCheck,
                description: "检测红光+红外双 LED 模块在位（WTQGetPPGDeviceInformation），不在位则后续测试项跳过并提示更换模块",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Spo2001StartStop,
                description: "输出红光/红外双通道模拟信号启动测量后停止输出，仪器输出启停控制正常即 PASS（产品状态回读预留）",
                specMin: 1, specMax: 1, unit: "", showWaveform: true);
            AddResult(ti, name: SpecItems.Spo2002FingerOff,
                description: "正常输出后停止信号模拟手指脱落，仪器输出控制正常即 PASS（产品脱落判定回读预留）",
                specMin: 1, specMax: 1, unit: "", showWaveform: true);
            AddResult(ti, name: SpecItems.Spo2003LowOxygenAlarm,
                description: "[预留] 输出低于 SpO2 报警阈值的信号，验证产品触发低氧报警（产品报警回读协议未定稿，结果不判定）",
                specMin: 1, specMax: 1, unit: "");

            return ti;
        }

        //======================================M13-----------SpO2 精度检测
        // 对应测试规范 2.2.2：SPO2-004 血氧饱和度精度 / SPO2-005 脉搏率精度 / SPO2-006 低氧范围精度。
        // 复用 AECG100 红/红外双通道输出（WTQOutputPPGEx）+ 产品回读 0x81/0x7C 比对，
        // 仪器列=设定值、产品列=回读值，动态规格=设定值±容差。
        private static TestModuleModel BuildSpo2AccuracyTest()
        {
            var ti = AddTI(TestModuleIds.Spo2AccuracyTest, "SpO2精度检测", "\uE95E",
                "SPO2-004 血氧饱和度精度 / SPO2-005 脉搏率精度 / SPO2-006 低氧范围精度（需 PPG-2R-940 红光+红外模块在位）");

            AddResult(ti, name: SpecItems.Spo2004SaturationAccuracy,
                description: "逐点（默认 75/80/90/95/100%）输出红光/红外双通道信号，产品回读值应在 设定值±2% 内（仪器列=设定值，产品列=回读值）",
                specMin: 0, specMax: 2, unit: "%", showWaveform: true, valueSource: "Both");
            AddResult(ti, name: SpecItems.Spo2005PulseRateAccuracy,
                description: "逐脉搏率点（默认 40/60/80/100/120/160BPM）输出双通道信号，产品回读脉搏率应在 设定值±2BPM 内（仪器列=设定值，产品列=回读值）",
                specMin: 0, specMax: 2, unit: "BPM", showWaveform: true, valueSource: "Both");
            AddResult(ti, name: SpecItems.Spo2006LowRangeAccuracy,
                description: "逐点（默认 50/60/70%）输出低氧范围双通道信号，产品回读值应在 设定值±3% 内（仪器列=设定值，产品列=回读值）",
                specMin: 0, specMax: 3, unit: "%", showWaveform: true, valueSource: "Both");

            return ti;
        }

        //======================================M14-----------SpO2 灌注与抗干扰测试
        // 对应测试规范 2.2.3：SPO2-008 弱灌注能力 / SPO2-009 弱灌注提示 / SPO2-010 环境光抗干扰。
        // PI（灌注指数）幅度控制与环境光控制暂未具备自动化条件，三项为预留不判定项（模块执行时报“预留（不判定）”）。
        private static TestModuleModel BuildSpo2PerfusionTest()
        {
            var ti = AddTI(TestModuleIds.Spo2PerfusionTest, "SpO2灌注与抗干扰测试", "\uE95E",
                "SPO2-008 弱灌注能力 / SPO2-009 弱灌注提示 / SPO2-010 环境光抗干扰（PI 控制与环境光控制预留）");

            AddResult(ti, name: SpecItems.Spo2008WeakPerfusion,
                description: "[预留] 逐灌注指数点（PI=0.3%/0.7%/1%）输出双通道信号，验证产品可正常测量（仪器 PI 幅度控制预留，结果不判定）",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Spo2009PerfusionHint,
                description: "[预留] 输出 PI<0.3% 信号，验证产品给出弱灌注提示（仪器 PI 幅度控制 + 产品提示回读预留，结果不判定）",
                specMin: 1, specMax: 1, unit: "");
            AddResult(ti, name: SpecItems.Spo2010AmbientLight,
                description: "[预留] 强环境光下测量，误差应 ≤±2%（环境光控制预留，结果不判定）",
                specMin: 0, specMax: 2, unit: "%");

            return ti;
        }

        //======================================M15-----------充电测试
        // 通过产品串口协议读取充电状态（0x6D）、电池电压+百分比（0x6C）
        private static TestModuleModel BuildChargingTest()
        {
            var ti = AddTI(TestModuleIds.ChargingTest, "充电测试", "\uE945",
                "充电状态判断 / 当前电池电量测量 / 电池百分比获取（产品串口协议 0x6C/0x6D）");

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

        #region 书写辅助（对应模板的 TIName[j]=... 与 ResultName[k]/SpecMin[k]/SpecMax[k] 赋值）

        /// <summary>新建一个测试模块（TI），返回模块对象供追加测试项；模块进入目录的顺序由 GetAllModules 决定。对应模板 TIName[j]/TIComments[j] 一行</summary>
        private static TestModuleModel AddTI(string id, string name, string icon, string description)
            => new TestModuleModel { Id = id, Name = name, Icon = icon, Description = description };

        /// <summary>
        /// 追加 M16 产品通讯初始化的三个通用通讯项（仅本模块拥有，全方案只判定一次；其余模块不重复问询）：
        /// MAC 地址（读回不为空即合格）+ 嵌软版本（与 <see cref="ExpectedFirmwareVersion"/> 比对）+ 当前状态查询。
        /// </summary>
        private static void AddHandshakeItems(TestModuleModel ti)
        {
            AddResult(ti, name: SpecItems.ProductMacAddress,
                description: " 0x72 读 MAC（回包前 6 字节）",
                specMin: 1, specMax: 1, unit: "", valueSource: "Product", hideSpecRange: true);
            AddResult(ti, name: SpecItems.ProductFirmwareVersion,
                description: " 0x72 读嵌软版本，与管控规格比对",
                specMin: 1, specMax: 1, unit: "", valueSource: "Product", specText: ExpectedFirmwareVersion);
            AddResult(ti, name: SpecItems.ProductStatusQuery,
                description: " 0x74 读测试模式和状态",
                specMin: 1, specMax: 1, unit: "", valueSource: "Product", specText: FirmwareStatus);
        }

        /// <summary>
        /// 给模块追加一条测试项。对应模板 ResultName[k]/SpecMin[k]/SpecMax[k] 三行。
        /// showWaveform=true = 该项执行时上抛实时采样点，UI 行内显示波形折叠按钮。
        /// valueSource = 测试值来源：Instrument（默认，仪器值）/ Product（产品上报值）/ Both（双列比对）/ Error（误差口径：主值=误差按静态规格判，双列=仪器/产品值）。
        /// hideSpecRange=true = 通讯检查项：无数值规格，规格列显示“有响应即通过”。
        /// specText 非空 = 字符串规格项（如嵌软版本管控）：规格列直接显示该文本。
        /// 暂不具备自动化条件的预留项不用跳过标记：模块执行时报“预留（不判定）”结果即可。
        /// </summary>
        private static void AddResult(TestModuleModel ti, string name, string description, double specMin, double specMax, string unit, bool showWaveform = false, string valueSource = "Instrument", bool hideSpecRange = false, string specText = "")
        {
            ti.Items.Add(new TestItemModel
            {
                Name = name,
                Description = description,
                LowerLimit = specMin,
                UpperLimit = specMax,
                Unit = unit,
                ShowWaveform = showWaveform,
                ValueSource = valueSource,
                HideSpecRange = hideSpecRange,
                SpecText = specText
            });
        }

        #endregion
    }
}
