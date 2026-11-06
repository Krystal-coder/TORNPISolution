using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 所有测试项名称的唯一来源：Spec 定义、TestItem/TestSteps/NfcFunctionTest 上报结果与上抛波形均引用此处，改一处两边同步
    /// </summary>
    public static class SpecItems
    {
        // -------- 通用通讯项（归属 A16 产品通讯初始化模块，全方案仅判定一次） --------
        public const string ProductMacAddress = "产品信息获取(MAC地址)";
        public const string ProductFirmwareVersion = "产品信息获取(嵌软版本)";
        public const string ProductStatusQuery = "产品当前状态查询";

        // -------- A00 初始化仪器 --------
        public const string GenIoInit = "固高IO模块初始化";

        // -------- A01 血压仪初始化连接 --------
        public const string BpSimulatorInit = "血压模拟仪初始化连接";

        // -------- A02 NFC功能测试 --------
        public const string NfcTagDetect = "标签识别";
        public const string NfcMacWrite = "MAC 信息写入";
        public const string NfcMacVerify = "MAC 信息验证";
        public const string NfcTagContentRead = "标签内容读取";

        // -------- A03 测试启动与停止功能（BP-001/002） --------
        public const string Bp001KeyStart = "BP-001物理按键启动测量";
        public const string Bp002KeyStop = "BP-002物理按键停止测量";

        // -------- A05 血压静态压力精度测试（BP-003，一点一项：按勾选压力点动态生成） --------
        /// <summary>BP-003 压力点项名（按压力值命名）：目录构建（Spec）与执行端上报（TestItem）同用本函数保证项名一致，配置改点后项名自动跟随。</summary>
        public static string Bp003Point(double value) => $"BP-003静态压力-{value:0.##}mmHg";

        // -------- A17 产品静态压力校准 --------
        /// <summary>校准压力点（固定三点，Spec 目录与执行端同用）</summary>
        public static readonly double[] PcalPoints = { 50, 150, 250 };
        public const string PcalStartSensor = "启动压力传感器";
        public const string PcalCloseSensor = "关闭压力传感器";

        /// <summary>压力校准 K/B 回读项（0x07 0xC3）：校准动作完成后读回，两个值都不等于出厂默认即合格</summary>
        public const string PcalReadKb = "读取压力校准KB值";

        /// <summary>K/B 出厂默认值（产品未校准或恢复出厂设置时就是这一对；实机未校准读回约 -34.1 / -7.0）</summary>
        public const double PcalKbDefaultK = -30.9006;
        public const double PcalKbDefaultB = -46.6286;
        /// <summary>校准点项名（按压力值命名）：目录构建（Spec）与执行端上报（TestItem）同用本函数</summary>
        public static string PcalPoint(double value) => $"设置{value:0.##}mmHg压力校准";

        // -------- A18 放气速率测试（产品关泄气阀封闭气路，仪器充气/缓降两段各一项：测试值=该段时长，双值列=该点采样压力；BPA700 过压测试 / 康伟达压力源+慢释阀）--------
        /// <summary>充气目标（mmHg）与缓降终点（mmHg）：Spec 说明与执行端流程同用</summary>
        public const double DeflateInflatePressure = 230;
        public const double DeflateEndPressure = 40;
        public const string Deflate007InflateTime = "BP-007加压时间t1";
        public const string Deflate007DeflateTime = "BP-007放气时间t2";

        // -------- A22 气路泄露测试（封堵毛细气孔后高低压各读起止两点，差值即泄气量；BPA700 静态压力序列 / 康伟达压力源直切；读取用指令与校准/BP-003 同款）--------
        /// <summary>高压段与低压段压力点（mmHg）：Spec 说明与执行端流程同用</summary>
        public const double LeakHighPressure = 250;
        public const double LeakLowPressure = 50;
        public const string Leak010HighStart = "BP-010高压起点P_H_start";
        public const string Leak010HighEnd = "BP-010高压终点P_H_end";
        public const string Leak010HighLoss = "BP-010高压泄气值ΔP_H";
        public const string Leak010LowStart = "BP-010低压起点P_L_start";
        public const string Leak010LowEnd = "BP-010低压终点P_L_end";
        public const string Leak010LowLoss = "BP-010低压泄气值ΔP_L";

        // -------- A06 血压动态压力精度测试（BP-004，一组压力一轮一项：按配置压力组与轮数动态生成，同 A05 机制）--------
        /// <summary>
        /// BP-004 轮次项名（一个压力组一轮一项）：目录构建（Spec）与执行端上报（TestItem）同用本函数，配置改点位/轮数后项名自动跟随（不重启，由 MainViewModel 重建目录）。
        /// 心率全模块只一个值、不写进项名：改心率不会让已存方案的结果项名错位。
        /// </summary>
        public static string Bp004Round(int systolic, int diastolic, int round)
            => $"BP-004精度测试 {systolic}/{diastolic} 第{round}轮";

        /// <summary>BP-004 压力组重复性项名（同组各轮的极差与 平均值/与设定偏差 统计；单轮配置不建本项）</summary>
        public static string Bp004Repeat(int systolic, int diastolic)
            => $"BP-004重复性测试 {systolic}/{diastolic}";

        /// <summary>BP-004/BP-005 生效轮数（Spec 建项与执行端轮次同用一份，各算一份会对不上项数）：最少 1 轮，配置几轮就跑几轮。</summary>
        public static int EffectiveRepeats(int configured) => Math.Max(1, configured);

        // -------- A21 血压心率精度及重复性测试（BP-005，一个心率点一轮一项：按配置心率点与轮数动态生成）--------
        /// <summary>BP-005 心率轮次项名（一个心率点一轮一项，测试值=本轮回读心率与设定的偏差）</summary>
        public static string Bp005Round(int heartRate, int round)
            => $"BP-005心率精度测试 {heartRate} 第{round}轮";

        /// <summary>BP-005 心率点平均值项名（同一点各轮心率的 均值/与设定偏差 统计，只展示不判定；单轮时均值即该轮，极大/极小/极差不出值）</summary>
        public static string Bp005Average(int heartRate)
            => $"BP-005心率精度测试 {heartRate} 平均值";

        // -------- A04 压力控制功能测试（BP-006~009，产品串口回读判定） --------
        public const string Bp006MaxInflatePressure = "BP-006最高充气压力";
        public const string Bp007UniformDeflation = "BP-007匀速放气控制";
        public const string Bp008FastDeflation = "BP-008快速放气功能";
        public const string Bp009ResponseTime = "BP-009过压保护响应时限";
        public const string Bp009ReliefDuration = "BP-009过压保护泄压时长";

        // -------- A07 AECG100初始化连接 --------
        public const string Aecg100InitConnection = "AECG100初始化连接";
        public const string Aecg100ModelName = "AECG100设备型号";
        public const string Aecg100FirmwareVersion = "AECG100固件版本";
        public const string Aecg100SerialNumber = "AECG100序列号";

        // -------- A08 ECG采集与启动（ECG-001/002） --------
        /// <summary>A08 与 A11 各有一个同名在位检测项（分模块判定，互不影响）</summary>
        public const string Ppg1R525PresenceCheck = "PPG-1R-525模块在位检测";
        public const string Ecg001Start = "ECG-001测量启动";
        public const string Ecg002LeadOff = "ECG-002导联脱落检测";

        // -------- A09 ECG心率监测（ECG-004） --------
        public const string Ecg004HeartRateAccuracy = "ECG-004心率检测精度";

        // -------- A10 ECG动态范围和线性（ECG-009，一幅度档×一偏置档一项：按配置动态生成，同 A05/A06 机制） --------
        /// <summary>ECG-009 点项名（幅度档 × DC 偏置档）：目录构建（Spec）与执行端上报（TestItem）同用本函数保证项名一致，配置改档后项名自动跟随（不重启，由 MainViewModel 重建目录）。</summary>
        public static string Ecg009Point(double amplitudeMv, double dcOffsetMv)
            => $"ECG-009动态范围-{amplitudeMv:0.##}mV/{dcOffsetMv:0.##}mV偏置";

        // -------- A11 PPG脉搏率模拟测试（PPG-001） --------
        public const string Ppg001PulseRateAccuracy = "PPG-001脉搏率精度";

        // -------- A12 SpO2测量模块（SPO2-001~003） --------
        public const string Ppg2R940PresenceCheck = "PPG-2R-940模块在位检测";
        public const string Spo2001StartStop = "SPO2-001测量启动与停止";
        public const string Spo2002FingerOff = "SPO2-002手指脱落检测";
        public const string Spo2003LowOxygenAlarm = "SPO2-003低氧报警测试";

        // -------- A13 SpO2精度检测（SPO2-004~006） --------
        public const string Spo2004SaturationAccuracy = "SPO2-004血氧饱和度精度";
        public const string Spo2005PulseRateAccuracy = "SPO2-005脉搏率精度";
        public const string Spo2006LowRangeAccuracy = "SPO2-006低氧范围精度";

        // -------- A14 SpO2灌注与抗干扰测试（SPO2-008~010，均预留不判定） --------
        public const string Spo2008WeakPerfusion = "SPO2-008弱灌注能力";
        public const string Spo2009PerfusionHint = "SPO2-009弱灌注提示";
        public const string Spo2010AmbientLight = "SPO2-010环境光抗干扰";

        // -------- A15 充电测试 --------
        public const string ChargingStatus = "充电状态判断";
        public const string BatteryVoltage = "当前电池电量测量";
        public const string BatteryPercent = "电池百分比获取";

        // -------- B02 产品条码获取（一台读码器收 SlotsPerReader 条，槽位号见 BoardBarcodeSlots） --------
        /// <summary>槽位条码项名（槽位号从 1 起）：目录构建与执行端上报同用本函数</summary>
        public static string BarcodeSlot(int slot) => $"产品条码-{slot}";


        // -------- B03 蓝牙功能测试（一台网关一个槽位，两项：广播地址与信号强度） --------
        /// <summary>槽位广播地址项名（槽位号从 1 起）</summary>
        public static string BluetoothMac(int slot) => $"蓝牙MAC地址-{slot}";

        /// <summary>槽位信号强度项名（测试值 = 本轮采到的均值 dBm）</summary>
        public static string BluetoothRssi(int slot) => $"蓝牙信号强度-{slot}";

        // -------- B04 综测仪上电（一台已启用综测仪占四个通道，一项一通道回读输出电压） --------
        /// <summary>通道供电电压项名（槽位号从 1 起，测试值 = 仪器回读的实测电压 mV）</summary>
        public static string PowerVoltage(int slot) => $"供电电压-{slot}";

        /// <summary>B02 槽位分配：只数已启用的读码器，按清单顺序一台接着占 SlotsPerReader 个槽位（槽位号从 1 起连号）；返回（槽位号, 读码器在清单里的下标, 是该读码器内的第几条），下标供执行端回查参数、不参与命名。</summary>
        public static IReadOnlyList<(int Slot, int ReaderIndex, int IndexInReader)> BoardBarcodeSlots(InstrumentConnectionConfig config)
        {
            var list = new List<(int Slot, int ReaderIndex, int IndexInReader)>();
            var slot = 0;
            for (var reader = 0; reader < config.BarcodeReaders.Count; reader++)
            {
                if (!config.BarcodeReaders[reader].Enabled) continue;
                var perReader = Math.Max(1, config.BarcodeReaders[reader].SlotsPerReader);
                for (var index = 0; index < perReader; index++)
                    list.Add((++slot, reader, index));
            }
            return list;
        }

        /// <summary>B03 槽位分配：一台已启用网关一个槽位，按清单顺序从 1 起连号（返回 槽位号与清单下标）</summary>
        public static IReadOnlyList<(int Slot, int GatewayIndex)> BoardBluetoothSlots(InstrumentConnectionConfig config)
        {
            var list = new List<(int Slot, int GatewayIndex)>();
            var slot = 0;
            for (var gateway = 0; gateway < config.BluetoothGateways.Count; gateway++)
            {
                if (!config.BluetoothGateways[gateway].Enabled) continue;
                list.Add((++slot, gateway));
            }
            return list;
        }

        /// <summary>B04 槽位分配：只数已启用的综测仪，每台按硬件固定的四个通道连着占四个槽位（槽位号从 1 起连号）；返回（槽位号, 综测仪在清单里的下标, 通道号 1~4），后两者供执行端回查参数与下发通道、不参与命名。</summary>
        public static IReadOnlyList<(int Slot, int TesterIndex, int Channel)> BoardPowerSlots(InstrumentConnectionConfig config)
        {
            var list = new List<(int Slot, int TesterIndex, int Channel)>();
            var slot = 0;
            for (var tester = 0; tester < config.Cry5242Testers.Count; tester++)
            {
                if (!config.Cry5242Testers[tester].Enabled) continue;
                for (var channel = 1; channel <= Cry5242Settings.ChannelCount; channel++)
                    list.Add((++slot, tester, channel));
            }
            return list;
        }

        // -------- A05 血压静态压力精度（点位清单：目录建项与逐点加压的单一来源）--------
        /// <summary>当前配置勾选的静态压力点（保持配置顺序、去重）：目录建项与逐点加压的唯一口径，项名按点位现算，两边各算一份会对不上名。</summary>
        public static List<double> StaticPressurePoints(InstrumentConnectionConfig config)
            => config.ActiveBpTestParams.StaticPressure.Points
                .Where(p => p.Enabled).Select(p => p.Value).Distinct().ToList();
    }
}
