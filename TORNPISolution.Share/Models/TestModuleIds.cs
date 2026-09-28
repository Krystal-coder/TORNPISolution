namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 测试模块固定 Id（模块定义与界面执行分派共用，避免魔法字符串）
    /// </summary>
    public static class TestModuleIds
    {
        /// <summary>初始化仪器（固高IO模块等硬件初始化）</summary>
        public const string InitInstruments = "M00";

        /// <summary>血压仪初始化连接（按配置选择康伟达/鲸扬 BPA700 并初始化）</summary>
        public const string BpDeviceInit = "M01";

        /// <summary>NFC功能测试（MAC信息写入与验证）</summary>
        public const string NfcFunctionTest = "M02";

        /// <summary>产品充放气测试（BP-001 物理按键启动测量 / BP-002 测量中手动停止）</summary>
        public const string BpAccuracyTest = "M03";

        /// <summary>压力控制功能测试（BP-006~008：匀速放气控制/快速放气控制/过压保护，均以产品串口回读压力判定）</summary>
        public const string PressureControlTest = "M04";

        /// <summary>血压静态压力精度测试（BP-003：多点静态压 vs 产品上报值，误差 ≤2mmHg）</summary>
        public const string BpStaticAccuracyTest = "M05";

        /// <summary>血压动态压力精度测试（BP-004 血压精度/重复性 + BP-005 心率精度/重复性）</summary>
        public const string BpDynamicAccuracyTest = "M06";

        /// <summary>AECG100 多功能综合性能测试仪初始化连接</summary>
        public const string Aecg100Init = "M07";

        /// <summary>ECG 采集与启动（ECG-001 测量启动 / ECG-002 导联脱落检测）</summary>
        public const string EcgAcquisitionTest = "M08";

        /// <summary>ECG 心率监测（ECG-004 心率检测精度，多档心率设置与比对）</summary>
        public const string EcgHeartRateTest = "M09";

        /// <summary>ECG 信号质量与波形（ECG-009~012：动态范围/输入阻抗/增益准确性/频率响应）</summary>
        public const string EcgSignalQualityTest = "M10";

        /// <summary>PPG 脉搏率模拟测试（PPG-001，需 PPG-1R-525 绿光模块在位）</summary>
        public const string PpgHeartRateTest = "M11";

        /// <summary>SpO2 测量模块（SPO2-001 测量启动与停止 / SPO2-002 手指脱落检测 / SPO2-003 低氧报警，需 PPG-2R-940 红光+红外模块在位）</summary>
        public const string Spo2MeasurementTest = "M12";

        /// <summary>SpO2 精度检测（SPO2-004 血氧饱和度精度 / SPO2-005 脉搏率精度 / SPO2-006 低氧范围精度）</summary>
        public const string Spo2AccuracyTest = "M13";

        /// <summary>SpO2 灌注与抗干扰测试（SPO2-008 弱灌注能力 / SPO2-009 弱灌注提示 / SPO2-010 环境光抗干扰）</summary>
        public const string Spo2PerfusionTest = "M14";

        /// <summary>充电测试（充电状态判断 / 当前电池电量测量 / 电池百分比获取）</summary>
        public const string ChargingTest = "M15";

        /// <summary>产品通讯初始化（进入产测模式 + 0x72 产品信息获取（MAC/嵌软版本）+ 0x74 状态查询，全方案唯一执行产品通讯判定的模块）</summary>
        public const string ProductCommInit = "M16";
    }
}
