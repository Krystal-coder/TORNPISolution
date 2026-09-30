namespace TORNPISolution.Service
{
    /// <summary>
    /// 所有测试项名称的唯一来源：Spec 定义、TestItem/TestSteps/NfcFunctionTest 上报结果与上抛波形均引用此处，改一处两边同步。
    /// 常量值 = 历史项名字符串，不得修改（UI 波形配置 WaveformDisplay.ItemConfigs 以项名为键）。
    /// 按模块分区，一测试项一常量，命名与模块编号（Spec 注释的 ---Mxx---）对应。
    /// </summary>
    public static class SpecItems
    {
        // -------- 通用通讯项（归属 M16 产品通讯初始化模块，全方案仅判定一次） --------
        public const string ProductMacAddress = "产品信息获取(MAC地址)";
        public const string ProductFirmwareVersion = "产品信息获取(嵌软版本)";
        public const string ProductStatusQuery = "产品当前状态查询";

        // -------- M00 初始化仪器 --------
        public const string GenIoInit = "固高IO模块初始化";

        // -------- M01 血压仪初始化连接 --------
        public const string BpSimulatorInit = "血压模拟仪初始化连接";

        // -------- M02 NFC功能测试 --------
        public const string NfcTagDetect = "标签识别";
        public const string NfcMacWrite = "MAC 信息写入";
        public const string NfcMacVerify = "MAC 信息验证";
        public const string NfcTagContentRead = "标签内容读取";

        // -------- M03 产品充放气测试（BP-001/002） --------
        public const string Bp001KeyStart = "BP-001物理按键启动测量";
        public const string Bp002ManualStop = "BP-002测量中手动停止";

        // -------- M05 血压静态压力精度测试（BP-003，一点一项：按勾选压力点动态生成） --------
        /// <summary>
        /// BP-003 压力点项名（按压力值命名）：目录构建（Spec）与执行端上报（TestItem）同用本函数，
        /// 保证项名一致；配置改点后项名自动跟随。
        /// </summary>
        public static string Bp003Point(double value) => $"BP-003静态压力-{value:0.##}mmHg";

        // -------- M17 产品静态压力校准 --------
        /// <summary>校准压力点（固定四点，Spec 目录与执行端同用）</summary>
        public static readonly double[] PcalPoints = { 60, 120, 180, 240 };
        public const string PcalStartSensor = "启动压力传感器";
        public const string PcalCloseSensor = "关闭压力传感器";
        /// <summary>校准点项名（按压力值命名）：目录构建（Spec）与执行端上报（TestItem）同用本函数</summary>
        public static string PcalPoint(double value) => $"设置{value:0.##}mmHg压力校准";

        // -------- M06 血压动态压力精度测试（BP-004/005） --------
        public const string Bp004SystolicAccuracy = "BP-004收缩压精度测试";
        public const string Bp004SystolicRepeat = "BP-004收缩压重复性测试";
        public const string Bp004DiastolicAccuracy = "BP-004舒张压精度测试";
        public const string Bp004DiastolicRepeat = "BP-004舒张压重复性测试";
        public const string Bp005HeartRateAccuracy = "BP-005心率精度测试";
        public const string Bp005HeartRateRepeat = "BP-005心率重复性测试";

        // -------- M04 压力控制功能测试（BP-006~008，产品串口回读判定） --------
        public const string Bp006UniformDeflation = "BP-006匀速放气控制";
        public const string Bp007FastDeflation = "BP-007快速放气控制";
        public const string Bp008Overpressure = "BP-008过压保护";

        // -------- M07 AECG100初始化连接 --------
        public const string Aecg100InitConnection = "AECG100初始化连接";
        public const string Aecg100ModelName = "AECG100设备型号";
        public const string Aecg100FirmwareVersion = "AECG100固件版本";
        public const string Aecg100SerialNumber = "AECG100序列号";

        // -------- M08 ECG采集与启动（ECG-001/002） --------
        /// <summary>M08 与 M11 各有一个同名在位检测项（分模块判定，互不影响）</summary>
        public const string Ppg1R525PresenceCheck = "PPG-1R-525模块在位检测";
        public const string Ecg001Start = "ECG-001测量启动";
        public const string Ecg002LeadOff = "ECG-002导联脱落检测";

        // -------- M09 ECG心率监测（ECG-004） --------
        public const string Ecg004HeartRateAccuracy = "ECG-004心率检测精度";

        // -------- M10 ECG信号质量与波形（ECG-009~012） --------
        public const string Ecg009aDynamicRangeNoBias = "ECG-009a动态范围(无偏置)";
        public const string Ecg009bDynamicRangePlus300 = "ECG-009b动态范围(+300mV)";
        public const string Ecg009cDynamicRangeMinus300 = "ECG-009c动态范围(-300mV)";
        public const string Ecg010InputImpedance = "ECG-010输入阻抗";
        public const string Ecg011GainAccuracy = "ECG-011增益准确性";
        public const string Ecg012FrequencyResponse = "ECG-012频率响应";

        // -------- M11 PPG脉搏率模拟测试（PPG-001） --------
        public const string Ppg001PulseRateAccuracy = "PPG-001脉搏率精度";

        // -------- M12 SpO2测量模块（SPO2-001~003） --------
        public const string Ppg2R940PresenceCheck = "PPG-2R-940模块在位检测";
        public const string Spo2001StartStop = "SPO2-001测量启动与停止";
        public const string Spo2002FingerOff = "SPO2-002手指脱落检测";
        public const string Spo2003LowOxygenAlarm = "SPO2-003低氧报警测试";

        // -------- M13 SpO2精度检测（SPO2-004~006） --------
        public const string Spo2004SaturationAccuracy = "SPO2-004血氧饱和度精度";
        public const string Spo2005PulseRateAccuracy = "SPO2-005脉搏率精度";
        public const string Spo2006LowRangeAccuracy = "SPO2-006低氧范围精度";

        // -------- M14 SpO2灌注与抗干扰测试（SPO2-008~010，均预留不判定） --------
        public const string Spo2008WeakPerfusion = "SPO2-008弱灌注能力";
        public const string Spo2009PerfusionHint = "SPO2-009弱灌注提示";
        public const string Spo2010AmbientLight = "SPO2-010环境光抗干扰";

        // -------- M15 充电测试 --------
        public const string ChargingStatus = "充电状态判断";
        public const string BatteryVoltage = "当前电池电量测量";
        public const string BatteryPercent = "电池百分比获取";
    }
}
