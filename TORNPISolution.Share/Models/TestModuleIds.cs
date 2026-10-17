namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 测试模块固定 Id。
    /// 编号约定：A 前缀=整机工位模块（尾号沿用历史 M 编号，便于老日志对照），B 前缀=单板工位模块（单板自己连号）。
    /// 类别不由前缀决定，以 <see cref="TestModuleModel.Category"/> 为准（Spec 目录声明），前缀只是给人看的命名约定。
    /// 历史 M 编号已弃用，仅老方案文件里还留着，读入时按 M→A 映射一次（见 MainViewModel.LoadPlanWithName）。
    /// </summary>
    public static class TestModuleIds
    {
        /// <summary>初始化仪器（固高IO模块等硬件初始化）</summary>
        public const string InitInstruments = "A00";

        /// <summary>血压仪初始化连接（按配置选择康伟达/鲸扬 BPA700 并初始化）</summary>
        public const string BpDeviceInit = "A01";

        /// <summary>NFC功能测试（MAC信息写入与验证）</summary>
        public const string NfcFunctionTest = "A02";

        /// <summary>测试启动与停止功能（BP-001 短按启动键 / BP-002 短按停止键，均按设备端状态 0x74 判定）</summary>
        public const string BpAccuracyTest = "A03";

        /// <summary>压力控制功能测试（BP-006~008：匀速放气控制/快速放气控制/过压保护，均以产品串口回读压力判定）</summary>
        public const string PressureControlTest = "A04";

        /// <summary>血压静态压力精度测试（BP-003：多点静态压 vs 产品上报值，误差 ≤3mmHg）</summary>
        public const string BpStaticAccuracyTest = "A05";

        /// <summary>血压动态压力精度测试（BP-004 血压精度/重复性，多组压力 + 单一心率）</summary>
        public const string BpDynamicAccuracyTest = "A06";

        /// <summary>AECG100 多功能综合性能测试仪初始化连接</summary>
        public const string Aecg100Init = "A07";

        /// <summary>ECG 采集与启动（ECG-001 测量启动 / ECG-002 导联脱落检测）</summary>
        public const string EcgAcquisitionTest = "A08";

        /// <summary>ECG 心率监测（ECG-004 心率检测精度，多档心率设置与比对）</summary>
        public const string EcgHeartRateTest = "A09";

        /// <summary>ECG 动态范围和线性（仅 ECG-009；输入阻抗/增益准确性/频率响应 暂不实现）</summary>
        public const string EcgSignalQualityTest = "A10";

        /// <summary>PPG 脉搏率模拟测试（PPG-001，需 PPG-1R-525 绿光模块在位）</summary>
        public const string PpgHeartRateTest = "A11";

        /// <summary>SpO2 测量模块（SPO2-001 测量启动与停止 / SPO2-002 手指脱落检测 / SPO2-003 低氧报警，需 PPG-2R-940 红光+红外模块在位）</summary>
        public const string Spo2MeasurementTest = "A12";

        /// <summary>SpO2 精度检测（SPO2-004 血氧饱和度精度 / SPO2-005 脉搏率精度 / SPO2-006 低氧范围精度）</summary>
        public const string Spo2AccuracyTest = "A13";

        /// <summary>SpO2 灌注与抗干扰测试（SPO2-008 弱灌注能力 / SPO2-009 弱灌注提示 / SPO2-010 环境光抗干扰）</summary>
        public const string Spo2PerfusionTest = "A14";

        /// <summary>充电测试（充电状态判断 / 当前电池电量测量 / 电池百分比获取）</summary>
        public const string ChargingTest = "A15";

        /// <summary>产品通讯初始化（进入产测模式 + 0x72 产品信息获取（MAC/嵌软版本）+ 0x74 状态查询，全方案唯一执行产品通讯判定的模块）</summary>
        public const string ProductCommInit = "A16";

        /// <summary>产品静态压力校准（模拟仪逐点稳压 60/120/180/240mmHg 后发 0x66 校准，结束关闭压力传感器）</summary>
        public const string ProductStaticPressureCalibration = "A17";

        /// <summary>JJG692-2010 放气速率检测（听诊，康伟达 0x04/0x12，测试值=设备回传漏气率 mmHg/s）</summary>
        public const string Jjg692DeflationRateTest = "A19";

        /// <summary>JJG692-2010 气密性检测（康伟达 0x04/0x13，测试值=设备回传漏气率 mmHg/s）</summary>
        public const string Jjg692AirtightnessTest = "A20";

        /// <summary>血压心率精度及重复性测试（BP-005：一组压力 + 多档心率，一点一轮一项，偏差与极差按 max(5, 设定×5%) 判）</summary>
        public const string BpHeartRateAccuracyTest = "A21";

        // ==================== 单板工位模块（B 前缀，自己连号） ====================

        /// <summary>单板初始化仪器（内容同整机 A00：固高IO模块等硬件初始化；两工位各有独立治具与目录项，互不干扰）</summary>
        public const string BoardInitInstruments = "B01";

        /// <summary>单板产品条码获取（逐台读码器触发取码，一台收 SlotsPerReader 条）</summary>
        public const string BoardBarcodeRead = "B02";

        /// <summary>单板蓝牙功能测试（逐台网关先识别广播地址，再采信号强度取均值判定）</summary>
        public const string BoardBluetoothTest = "B03";

        /// <summary>单板综测仪上电（逐台综测仪逐通道缓输出到设定电压，回读通道电压与设定比对；跑完只断连接，输出保持）</summary>
        public const string BoardPowerOn = "B04";

        /// <summary>
        /// 老编号兼容：把已弃用的 M 前缀转成同尾号的整机 A 前缀（M06→A06），其余原样返回。
        /// 只为读旧方案文件服务：内存里一律走新 Id，下次保存自然升级。
        /// </summary>
        public static string NormalizeLegacyId(string? id)
            => id is { Length: 3 } && (id[0] == 'M' || id[0] == 'm') && char.IsDigit(id[1]) && char.IsDigit(id[2])
                ? "A" + id.Substring(1)
                : id!;
    }
}
