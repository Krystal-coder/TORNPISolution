using System.Runtime.InteropServices;
using TORNPISolution.Interface;

namespace TORNPISolution.Service.Devices
{
    // ==================== AECG100 枚举定义（与官方 AECG100.h 一一对应） ====================

    /// <summary>PPG 输出通道：WAP2001-2004 ch1=R/ch2=IR；WAP2005-2008 ch3=G</summary>
    public enum AecgPpgChannel
    {
        Channel1 = 1,
        Channel2 = 2,
        Channel3 = 3
    }

    /// <summary>工作模式类型（ECG / PWV / SPO2 / PPG 1~3 通道组合）</summary>
    public enum AecgModeType
    {
        Ecg = 1,
        Pwv = 2,
        Spo2 = 3,
        Ppg1 = 4,
        Ppg2 = 5,
        Pwv2 = 6,
        Pwv3 = 7,
        Spo23 = 8,
        Ppg3 = 9
    }

    /// <summary>独立模式槽位（Main / Mode-A / Mode-B / Mode-C）</summary>
    public enum AecgStandAloneMode
    {
        Main = 0,
        ModeA = 1,
        ModeB = 2,
        ModeC = 3
    }

    /// <summary>ECG 波形类型</summary>
    public enum AecgEcgWaveformType
    {
        Sine = 0,
        Triangle = 1,
        Square = 2,
        RectanglePulse = 3,
        TrianglePulse = 4,
        Exponential = 5,
        Ecg = 6
    }

    /// <summary>PPG 波形类型</summary>
    public enum AecgPpgWaveformType
    {
        Sine = 0,
        Triangle = 1,
        Square = 2,
        Ppg = 3
    }

    /// <summary>PPG 模块类型（由 WTQGetPPGDeviceInformation 的 LED 类型判定）</summary>
    public enum AecgPpgModuleKind
    {
        /// <summary>未检测到 PPG 模块</summary>
        None = 0,

        /// <summary>单绿光模块（PPG-1R-525，用于 PPG 心率模拟）</summary>
        Green = 1,

        /// <summary>红光+红外双 LED 模块（PPG-2R-880/PPG-2R-940/PPG-2TF-660，用于 SpO2 模拟）</summary>
        RedIr = 2,

        /// <summary>已检测到模块但 LED 组合无法归类</summary>
        Unknown = 3
    }

    /// <summary>输出电极：RA / LA</summary>
    public enum AecgElectrode
    {
        RightArm = 0,
        LeftArm = 0xff
    }

    /// <summary>ECG 阻抗：关 / 开（620K）</summary>
    public enum AecgEcgImpedance
    {
        Off = 0,
        On = 0xff
    }

    /// <summary>起搏脉冲：关 / 开</summary>
    public enum AecgEcgPacing
    {
        Off = 0,
        On = 0xff
    }

    /// <summary>呼吸模式（可组合，Resistance 仅 ECG 支持且优先）</summary>
    [Flags]
    public enum AecgRespirationMode
    {
        Off = 0x00,
        BaselineModulation = 0x01,
        AmplitudeModulation = 0x02,
        FrequencyModulation = 0x04,
        Resistance = 0x80
    }

    /// <summary>ECG 呼吸阻抗基线（ohm）</summary>
    public enum AecgEcgRespirationBaseline
    {
        Baseline1000 = 1000,
        Baseline1500 = 1500,
        Baseline2000 = 2000
    }

    /// <summary>ECG 噪声频率</summary>
    public enum AecgEcgNoiseFrequency
    {
        Off = 0,
        F50Hz = 1,
        F60Hz = 2,
        F100Hz = 3,
        F120Hz = 4
    }

    /// <summary>PPG 噪声频率（枚举值顺序与官方头文件一致）</summary>
    public enum AecgPpgNoiseFrequency
    {
        Off = 0,
        F50Hz = 1,
        F60Hz = 2,
        F1KHz = 3,
        F5KHz = 4,
        F100Hz = 5,
        F120Hz = 6,
        WhiteNoise = 7,
        F0_5Hz = 8,
        F1Hz = 9,
        F2Hz = 10,
        F3Hz = 11,
        F5Hz = 12,
        F10Hz = 13,
        F25Hz = 14
    }

    /// <summary>PPG 通道 LED 类型</summary>
    public enum AecgLedType
    {
        Green = 0,
        Red = 1,
        Ir = 2,
        None = 3
    }

    /// <summary>LED 脉冲（位组合：Green/Red/IR）</summary>
    [Flags]
    public enum AecgLedPulse
    {
        None = 0x00,
        Green = 0x01,
        Red = 0x02,
        Infrared = 0x04
    }

    /// <summary>AECG100 LED 与 DUT LED 同步方式</summary>
    public enum AecgSyncPulse
    {
        LedOff = 0,
        SyncOn = 1,
        SyncOff = 2
    }

    /// <summary>PPG 波形是否反相输出</summary>
    public enum AecgPpgInverted
    {
        Off = 0,
        On = 1
    }

    /// <summary>PPG 采样通道选择</summary>
    public enum AecgPpgSampling
    {
        Channel1Pd = 0,
        Channel2Pd = 1,
        Channel1Switch = 2,
        Channel2Switch = 3
    }

    /// <summary>PPG 环境光模式（仅 WAP2012 有效）</summary>
    public enum AecgPpgAmbientLightMode
    {
        Off = 0,
        F50Hz = 1,
        F60Hz = 2,
        F1KHz = 3,
        F2KHz = 4,
        F3KHz = 5,
        F4KHz = 6,
        F5KHz = 7,
        F6KHz = 8,
        F7KHz = 9,
        F8KHz = 10,
        F9KHz = 11,
        F10KHz = 12,
        DirectCurrent = 13,
        SunLight = 14
    }

    // ==================== 托管侧波形参数模型 ====================

    /// <summary>
    /// ECG 波形参数（对应官方 ECG_WAVEFORM，单位见各字段注释）。
    /// </summary>
    public class AecgEcgWaveform
    {
        /// <summary>波形类型</summary>
        public AecgEcgWaveformType WaveformType { get; set; } = AecgEcgWaveformType.Ecg;

        /// <summary>频率 Hz（ECG 模式即心率对应频率，如 60BPM = 1Hz）</summary>
        public double Frequency { get; set; } = 1.0;

        /// <summary>幅度 mV</summary>
        public double Amplitude { get; set; } = 1.0;

        /// <summary>T 波 mV</summary>
        public double TWave { get; set; }

        /// <summary>P 波 mV</summary>
        public double PWave { get; set; }

        /// <summary>ST 段 mV</summary>
        public double STSegment { get; set; }

        /// <summary>DC 偏置是否可变（true：-500~500；false：只能 300/0/-300）</summary>
        public bool DCOffsetVariable { get; set; }

        /// <summary>DC 偏置 mV（受 DCOffsetVariable 约束）</summary>
        public int DCOffset { get; set; }

        /// <summary>波形周期 ms</summary>
        public int TimePeriod { get; set; } = 1000;

        /// <summary>PR 间期 ms</summary>
        public int PRInterval { get; set; }

        /// <summary>QRS 波群宽度 ms</summary>
        public int QRSDuration { get; set; }

        /// <summary>T 波宽度 ms</summary>
        public int TDuration { get; set; }

        /// <summary>QT 间期 ms</summary>
        public int QTInterval { get; set; }

        /// <summary>阻抗设置</summary>
        public AecgEcgImpedance Impedance { get; set; } = AecgEcgImpedance.Off;

        /// <summary>输出电极</summary>
        public AecgElectrode Electrode { get; set; } = AecgElectrode.RightArm;

        /// <summary>脉冲宽度 ms（仅 RectanglePulse/TrianglePulse/Exponential 有效）</summary>
        public int PulseWidth { get; set; }

        /// <summary>噪声幅度 mV（最大 2.00）</summary>
        public double NoiseAmplitude { get; set; }

        /// <summary>噪声频率</summary>
        public AecgEcgNoiseFrequency NoiseFrequency { get; set; } = AecgEcgNoiseFrequency.Off;

        /// <summary>是否启用起搏脉冲</summary>
        public bool PacingEnabled { get; set; }

        /// <summary>起搏幅度 mV（-1000~1000）</summary>
        public double PacingAmplitude { get; set; }

        /// <summary>起搏持续时间 ms（0~2，分辨率 0.1）</summary>
        public double PacingDuration { get; set; }

        /// <summary>起搏速率 BPM</summary>
        public int PacingRate { get; set; }

        /// <summary>呼吸模式（RespirationMode 组合）</summary>
        public AecgRespirationMode RespirationMode { get; set; } = AecgRespirationMode.Off;

        /// <summary>呼吸幅度 milli-ohm（电阻式：1000~5000，分辨率 0.1ohm）</summary>
        public int RespirationAmplitude { get; set; }

        /// <summary>呼吸速率 BrPM（4~200）</summary>
        public int RespirationRate { get; set; }

        /// <summary>呼吸比率：电阻式 1~5（1:1~1:5）；调制式 -16~16% 变化量</summary>
        public int RespirationRatio { get; set; } = 1;

        /// <summary>呼吸基线：电阻式取 ECGRespirationBaseline（默认 1000ohm）；调制式取吸呼比 1~5</summary>
        public int RespirationBaseline { get; set; } = 1000;

        /// <summary>呼吸暂停时长 s（0~60）</summary>
        public int RespirationApneaDuration { get; set; }

        /// <summary>呼吸暂停周期 min（1~10）</summary>
        public int RespirationApneaCycle { get; set; }
    }

    /// <summary>
    /// PPG 波形参数（对应官方 PPG_WAVEFORM，单位见各字段注释）。
    /// </summary>
    public class AecgPpgWaveform
    {
        /// <summary>波形类型</summary>
        public AecgPpgWaveformType WaveformType { get; set; } = AecgPpgWaveformType.Ppg;

        /// <summary>频率 Hz（PWV 模式须与 ECG 频率一致）</summary>
        public double Frequency { get; set; } = 1.0;

        /// <summary>直流分量 mV</summary>
        public double VolDC { get; set; }

        /// <summary>主波（收缩峰）幅度 mV（AC 与 ACOffset 之和 ≤ 30mV）</summary>
        public double VolSP { get; set; }

        /// <summary>重搏切迹幅度 mV</summary>
        public double VolDN { get; set; }

        /// <summary>重搏波幅度 mV</summary>
        public double VolDP { get; set; }

        /// <summary>AC 偏置 mV</summary>
        public double ACOffset { get; set; }

        /// <summary>主波时刻 ms</summary>
        public int TimeSP { get; set; }

        /// <summary>重搏切迹时刻 ms</summary>
        public int TimeDN { get; set; }

        /// <summary>重搏波时刻 ms</summary>
        public int TimeDP { get; set; }

        /// <summary>波形周期 ms</summary>
        public int TimePeriod { get; set; } = 1000;

        /// <summary>同步脉冲方式</summary>
        public AecgSyncPulse SyncPulse { get; set; } = AecgSyncPulse.SyncOff;

        /// <summary>是否反相输出</summary>
        public AecgPpgInverted Inverted { get; set; } = AecgPpgInverted.Off;

        /// <summary>噪声幅度 mV（最大 2.00）</summary>
        public double NoiseAmplitude { get; set; }

        /// <summary>噪声频率</summary>
        public AecgPpgNoiseFrequency NoiseFrequency { get; set; } = AecgPpgNoiseFrequency.Off;

        /// <summary>呼吸模式（调制式组合）</summary>
        public AecgRespirationMode RespirationMode { get; set; } = AecgRespirationMode.Off;

        /// <summary>呼吸速率 BrPM</summary>
        public int RespirationRate { get; set; }

        /// <summary>呼吸变化量 -16~16%</summary>
        public int RespirationVariation { get; set; }

        /// <summary>吸呼比 1~5（1:1~1:5）</summary>
        public int RespirationInExhaleRatio { get; set; } = 1;

        /// <summary>呼吸暂停时长 s（0~60）</summary>
        public int RespirationApneaDuration { get; set; }

        /// <summary>呼吸暂停周期 min（1~10）</summary>
        public int RespirationApneaCycle { get; set; }
    }

    /// <summary>频率扫描参数（ECG 用，对应 FREQUENCY_SCAN）</summary>
    public class AecgFrequencyScan
    {
        /// <summary>幅度 mV</summary>
        public double Amplitude { get; set; } = 1.0;

        /// <summary>起始频率 Hz</summary>
        public double FrequencyStart { get; set; } = 1.0;

        /// <summary>终止频率 Hz</summary>
        public double FrequencyFinish { get; set; } = 100.0;

        /// <summary>扫描时长 s</summary>
        public int Duration { get; set; } = 10;
    }

    /// <summary>频率扫描参数（PPG 用，对应 FREQUENCY_SCAN2）</summary>
    public class AecgFrequencyScanPpg
    {
        /// <summary>幅度 mV</summary>
        public double Amplitude { get; set; } = 1.0;

        /// <summary>直流分量 mV（PPG：0~3000）</summary>
        public double DC { get; set; }

        /// <summary>同步脉冲方式</summary>
        public AecgSyncPulse SyncPulse { get; set; } = AecgSyncPulse.SyncOff;

        /// <summary>起始频率 Hz</summary>
        public double FrequencyStart { get; set; } = 1.0;

        /// <summary>终止频率 Hz</summary>
        public double FrequencyFinish { get; set; } = 100.0;

        /// <summary>扫描时长 s</summary>
        public int Duration { get; set; } = 10;
    }

    /// <summary>设备型号信息（对应 MODEL_INFORMATION）</summary>
    public record AecgModelInformation(
        string ProductName,
        int GenerationNumber,
        int ModelNumber,
        int SerialNumber,
        int Year,
        AecgLedType LedType1,
        AecgLedType LedType2,
        AecgLedType LedType3);

    /// <summary>设备固件/硬件信息（对应 HW_INFORMATION）</summary>
    public record AecgHwInformation(
        int FWMajorVersion,
        int FWMinorVersion,
        int FWBuildVersion,
        int HWVersion,
        int PCBVersion,
        int FPGAMajorVersion,
        int FPGAMinorVersion);

    /// <summary>
    /// 鲸扬 AECG100 多功能综合性能测试仪（WhaleTeq）控制类。
    /// 通过 P/Invoke 调用 AECG100x64.dll 原生 SDK（与 BPA700 相同接入方式），不走串口协议，SDK 内部处理 USB VCP 通讯。
    /// SDK 关键 API 分组见下方 Native 区声明（连接生命周期 / 波形输出 / 频率扫描 / 回放 / PPG 采样 / RLD / 独立模式）。
    /// </summary>
    public class Aecg100Tester : IDisposable
    {
        #region 原生 SDK P/Invoke 声明（按官方 AECG100.h 1.0.5.4 逐条对齐）

        private const string DllName = "AECG100x64.dll";

        // ---------- 回调委托 ----------

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ConnectedCallbackDelegate([MarshalAs(UnmanagedType.I1)] bool connected);

        /// <summary>输出数据回调：time(s)、ac(uV)、dc(uV)；停止输出时最后一次回调 ac/dc 为 int 最小值</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void OutputSignalCallbackDelegate(double time, int ac, int dc);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SamplingCallbackDelegate(int data, int number);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DutSignalDetectionCallbackDelegate(int location);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SamplingErrorCallbackDelegate(int error);

        // ---------- 原生结构体 ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct MODEL_INFORMATION
        {
            public byte ProductName0;
            public byte ProductName1;
            public byte GenerationNumber;
            public byte ModelNumber;
            public int SerialNumber;
            public int Year;
            public int LEDType1;
            public int LEDType2;
            public int LEDType3;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HW_INFORMATION
        {
            public int FWMajorVersion;
            public int FWMinorVersion;
            public int FWBuildVersion;
            public int HWVersion;
            public int PCBVersion;
            public int FPGAMajorVersion;
            public int FPGAMinorVersion;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ECG_WAVEFORM
        {
            public int WaveformType;
            public double Frequency;
            public double Amplitude;
            public double TWave;
            public double PWave;
            public double STSegment;
            public int DCOffsetVariable;
            public int DCOffset;
            public int TimePeriod;
            public int PRInterval;
            public int QRSDuration;
            public int TDuration;
            public int QTInterval;
            public int Impedance;
            public int Electrode;
            public int PulseWidth;
            public double NoiseAmplitude;
            public int NoiseFrequency;
            public int PacingEnabled;
            public double PacingAmplitude;
            public double PacingDuration;
            public int PacingRate;
            public int RespirationMode;
            public int RespirationAmplitude;
            public int RespirationRate;
            public int RespirationRatio;
            public int RespirationBaseline;
            public int RespirationApneaDuration;
            public int RespirationApneaCycle;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PPG_WAVEFORM
        {
            public int WaveformType;
            public double Frequency;
            public double VolDC;
            public double VolSP;
            public double VolDN;
            public double VolDP;
            public double ACOffset;
            public int TimeSP;
            public int TimeDN;
            public int TimeDP;
            public int TimePeriod;
            public int SyncPulse;
            public int Inverted;
            public double NoiseAmplitude;
            public int NoiseFrequency;
            public int RespirationMode;
            public int RespirationRate;
            public int RespirationVariation;
            public int RespirationInExhaleRatio;
            public int RespirationApneaDuration;
            public int RespirationApneaCycle;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FREQUENCY_SCAN
        {
            public double Amplitude;
            public double FrequencyStart;
            public double FrequencyFinish;
            public int Duration;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FREQUENCY_SCAN2
        {
            public double Amplitude;
            public double DC;
            public int SyncPulse;
            public double FrequencyStart;
            public double FrequencyFinish;
            public int Duration;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PPG_LED_PULSE_GROUP_SETTING
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] PulseLEDTable;
            public ushort PulseGroupInterval; // us；0 = IR-first 模式（表首必须为 IR）
        }

        /// <summary>原始数据回放（PLAY_RAW_DATA）：AC/DC 为托管数组指针，回调内嵌</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PLAY_RAW_DATA
        {
            public double SampleRate;
            public int Size;
            public int SyncPulse;
            public IntPtr AC;
            public IntPtr DC;
            public OutputSignalCallbackDelegate cb;
        }

        // ---------- 初始化 / 连接 / 设备信息 ----------

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQInit(ConnectedCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQConnect(uint portNumber, uint millisecondsTimeout);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void WTQFree();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQGetDeviceInformation(out MODEL_INFORMATION modelInfo);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr WTQGetSerialNumber();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQGetPPGDeviceInformation(out MODEL_INFORMATION modelInfo);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr WTQGetPPGSerialNumber();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQGetHWInformation(out HW_INFORMATION hwInfo);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQGetPPGHWInformation(out HW_INFORMATION hwInfo);

        // ---------- ECG RLD 测试 ----------

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQEnableRLD([MarshalAs(UnmanagedType.I1)] bool enable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQReadRLD(out double n1, out double n2);

        // ---------- PPG 模块采样 ----------

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQEnableSampling(int mode, SamplingCallbackDelegate cbSamplingData);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQStartSampling(SamplingErrorCallbackDelegate cbError);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void WTQDisableSampling();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQEnableDUTSignalDetection([MarshalAs(UnmanagedType.I1)] bool enable, DutSignalDetectionCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void WTQEnableACAboveDC([MarshalAs(UnmanagedType.I1)] bool enable);

        // ---------- SpO2 自动功能（WAP2012/WAP2112） ----------

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQEnableDynamicLEDStrength([MarshalAs(UnmanagedType.I1)] bool enable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQConfigureTransmissionRate(int mode);

        // ---------- ECG 输出控制 ----------

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQDeviceEnableImpedance(int impedance);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQDeviceSetElectrode(int electrode);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQDeviceSetDCOffset(int dcOffset);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQDeviceEnablePacing(int pacing);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQDeviceEnableRespiration(int respiration);

        // ---------- PPG LED 配置 / 环境光 ----------

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQReadLEDPulseGroupSetting(ref PPG_LED_PULSE_GROUP_SETTING setting);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQWriteLEDPulseGroupSetting(ref PPG_LED_PULSE_GROUP_SETTING setting);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WTQGetLEDAmbientLightMode();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQSetLEDAmbientLightMode(int mode);

        // ---------- 单机模式波形输出 WTQOutput* ----------

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQOutputECG(ref ECG_WAVEFORM waveform, OutputSignalCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQOutputECGAndPPG(int timeDiffPTTPeak, ref ECG_WAVEFORM ecgWaveform, ref PPG_WAVEFORM ppgWaveform, OutputSignalCallbackDelegate cbECG, OutputSignalCallbackDelegate cbPPG);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQOutputECGAndPPGEx(int timeDiffPTTPeak, ref ECG_WAVEFORM ecgWaveform, ref PPG_WAVEFORM ppgChannel1, ref PPG_WAVEFORM ppgChannel2, OutputSignalCallbackDelegate cbECG, OutputSignalCallbackDelegate cbPPG1, OutputSignalCallbackDelegate cbPPG2);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQOutputECGAndPPG3(int timeDiffPTTPeak, ref ECG_WAVEFORM ecgWaveform, ref PPG_WAVEFORM ppgChannel1, ref PPG_WAVEFORM ppgChannel2, ref PPG_WAVEFORM ppgChannel3, OutputSignalCallbackDelegate cbECG, OutputSignalCallbackDelegate cbPPG1, OutputSignalCallbackDelegate cbPPG2, OutputSignalCallbackDelegate cbPPG3);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQOutputPPG(int channelNo, ref PPG_WAVEFORM waveform, OutputSignalCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQOutputPPGEx(ref PPG_WAVEFORM channel1, ref PPG_WAVEFORM channel2, OutputSignalCallbackDelegate cb1, OutputSignalCallbackDelegate cb2);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQOutputPPG3(ref PPG_WAVEFORM channel1, ref PPG_WAVEFORM channel2, ref PPG_WAVEFORM channel3, OutputSignalCallbackDelegate cb1, OutputSignalCallbackDelegate cb2, OutputSignalCallbackDelegate cb3);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQOutputFrequencyScan(ref FREQUENCY_SCAN scan, OutputSignalCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQOutputFrequencyScanPPG(int channelNo, ref FREQUENCY_SCAN2 scan, OutputSignalCallbackDelegate cb);

        // ---------- 原始数据回放 WTQWaveformPlayerOutput* ----------

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQWaveformPlayerOutputECG(ref PLAY_RAW_DATA data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQWaveformPlayerOutputECGAndPPG(ref PLAY_RAW_DATA ecgData, int channelNo, ref PLAY_RAW_DATA ppgData);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQWaveformPlayerOutputPPG(int channelNo, ref PLAY_RAW_DATA ppgData);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQWaveformPlayerOutputPPGEx(ref PLAY_RAW_DATA ppgChannel1, ref PLAY_RAW_DATA ppgChannel2);

        // ---------- play-raw 模式输出 WTQPlay*（支持调制式呼吸） ----------

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQPlayECG(ref ECG_WAVEFORM waveform, OutputSignalCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQPlayECGAndPPG(int timeDiffPTTPeak, ref ECG_WAVEFORM ecgWaveform, ref PPG_WAVEFORM ppgWaveform, OutputSignalCallbackDelegate cbECG, OutputSignalCallbackDelegate cbPPG);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQPlayPPG(int channelNo, ref PPG_WAVEFORM waveform, OutputSignalCallbackDelegate cb);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void WTQWaveformPlayerLoop([MarshalAs(UnmanagedType.I1)] bool loop);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void WTQStopOutputWaveform();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void WTQStopPlayRawData();

        // ---------- 独立模式读写 ----------

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WTQReadStandaloneModeType(int mode);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WTQReadECGModeFromStandaloneMode(int mode, ref ECG_WAVEFORM waveform);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WTQWriteECGModeToStandaloneMode(int mode, ref ECG_WAVEFORM waveform);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WTQReadPWVModeFromStandaloneMode(int mode, out int timeDiffPTTPeak, ref ECG_WAVEFORM ecgWaveform, ref PPG_WAVEFORM ppgWaveform);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WTQWritePWVModeToStandaloneMode(int mode, int timeDiffPTTPeak, ref ECG_WAVEFORM ecgWaveform, ref PPG_WAVEFORM ppgWaveform);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WTQReadSPO2ModeFromStandaloneMode(int mode, ref PPG_WAVEFORM channel1, ref PPG_WAVEFORM channel2);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WTQWriteSPO2ModeToStandaloneMode(int mode, ref PPG_WAVEFORM channel1, ref PPG_WAVEFORM channel2);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WTQReadPPGModeFromStandaloneMode(int mode, ref PPG_WAVEFORM ppgWaveform);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WTQWritePPGModeToStandaloneMode(int mode, ref PPG_WAVEFORM ppgWaveform);

        // ---------- 校准 / 同步 / 版本 ----------

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQRestoreFactorySetting();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQReadPPGLedCalibrationSetting(int channelNo, out ushort level);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQWritePPGLedCalibrationSetting(int channelNo, ushort level);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQReadPPGTriggerLevelCalibrationSetting(int channelNo, out ushort level);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool WTQWritePPGTriggerLevelCalibrationSetting(int channelNo, ushort level);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr WTQCreateSynchronizationSignal();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void WTQSetSynchronizationSignal(IntPtr syncObject);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void WTQSynchronizationSignalSetEvent(IntPtr syncObject);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void WTQSynchronizationSignalResetEvent(IntPtr syncObject);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint WTQGetVersion();

        #endregion

        #region 连接生命周期

        private readonly ILogService _log;
        private readonly TORNPISolution.Share.Models.Aecg100Settings _settings;

        // 回调委托必须以字段持有，防止被 GC 回收（原生回调发生在 SDK 内部线程）
        private ConnectedCallbackDelegate? _connectedCallback;
        private SamplingCallbackDelegate? _samplingCallback;
        private SamplingErrorCallbackDelegate? _samplingErrorCallback;
        private DutSignalDetectionCallbackDelegate? _dutDetectionCallback;
        private readonly List<OutputSignalCallbackDelegate> _outputCallbacks = new();
        private readonly List<GCHandle> _rawDataHandles = new();

        /// <summary>设备连接/断开事件（true=已连接）</summary>
        public event Action<bool>? ConnectedChanged;

        /// <summary>当前是否已连接</summary>
        public bool IsConnected { get; private set; }

        public Aecg100Tester(TORNPISolution.Share.Models.Aecg100Settings settings, ILogService? log = null)
        {
            _settings = settings;
            _log = log ?? new LogService(nameof(Aecg100Tester));
        }

        /// <summary>
        /// 连接设备：注册连接事件监听（WTQInit）后按配置 COM 口发起连接（WTQConnect）。
        /// COM 口留空时传 0xFFFFFFFF 由 SDK 自动选择。
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            _log.Info($"{DateTime.Now} Start calling the Aecg100Tester.ConnectAsync() method . {Environment.NewLine}");
            try
            {
                _connectedCallback = OnConnected;
                if (!WTQInit(_connectedCallback))
                {
                    _log.Error("AECG100 WTQInit 失败，无法注册连接事件");
                    return false;
                }

                var port = ParsePortNumber(_settings.PortName);
                var timeout = _settings.ConnectTimeoutMs < 0 ? uint.MaxValue : (uint)_settings.ConnectTimeoutMs;
                var ok = await Task.Run(() => WTQConnect(port, timeout));
                IsConnected = ok;
                _log.Info(ok
                    ? $"AECG100 连接成功（Port={_settings.PortName}）"
                    : $"AECG100 连接超时（Port={_settings.PortName}, Timeout={_settings.ConnectTimeoutMs}ms）");
                return ok;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in Aecg100Tester.class ConnectAsync method, message: {ex}", ex);
                return false;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the Aecg100Tester.ConnectAsync() method . {Environment.NewLine}");
            }
        }

        private void OnConnected(bool connected)
        {
            IsConnected = connected;
            _log.Info($"AECG100 连接状态变化：{(connected ? "已连接" : "已断开")}");
            ConnectedChanged?.Invoke(connected);
        }

        /// <summary>"COM5"/"5" → 5；空或 Auto → 0xFFFFFFFF（SDK 自动选择）</summary>
        private static uint ParsePortNumber(string? portName)
        {
            if (string.IsNullOrWhiteSpace(portName) || portName.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase))
                return 0xFFFFFFFF;

            var digits = new string(portName.Where(char.IsDigit).ToArray());
            return uint.TryParse(digits, out var n) ? n : 0xFFFFFFFF;
        }

        /// <summary>断开连接并释放 SDK 资源（WTQFree）</summary>
        public void Disconnect()
        {
            WTQFree();
            IsConnected = false;
            _outputCallbacks.Clear();
            FreeRawDataHandles();
        }

        public void Dispose() => Disconnect();

        #endregion

        #region 设备信息

        /// <summary>主模块型号信息（序列号格式 W%c%c%02X%02X-%02d%04d）</summary>
        public AecgModelInformation? GetDeviceInformation()
            => WTQGetDeviceInformation(out var m) ? FromNative(m) : null;

        /// <summary>PPG 模块型号信息</summary>
        public AecgModelInformation? GetPpgDeviceInformation()
            => WTQGetPPGDeviceInformation(out var m) ? FromNative(m) : null;

        /// <summary>主模块固件/硬件信息</summary>
        public AecgHwInformation? GetHwInformation()
            => WTQGetHWInformation(out var h)
                ? new AecgHwInformation(h.FWMajorVersion, h.FWMinorVersion, h.FWBuildVersion, h.HWVersion, h.PCBVersion, h.FPGAMajorVersion, h.FPGAMinorVersion)
                : null;

        /// <summary>PPG 模块固件/硬件信息</summary>
        public AecgHwInformation? GetPpgHwInformation()
            => WTQGetPPGHWInformation(out var h)
                ? new AecgHwInformation(h.FWMajorVersion, h.FWMinorVersion, h.FWBuildVersion, h.HWVersion, h.PCBVersion, h.FPGAMajorVersion, h.FPGAMinorVersion)
                : null;

        /// <summary>主模块序列号（SDK 返回的字符串不得释放）</summary>
        public string? GetSerialNumber() => Marshal.PtrToStringAnsi(WTQGetSerialNumber());

        /// <summary>PPG 模块序列号</summary>
        public string? GetPpgSerialNumber() => Marshal.PtrToStringAnsi(WTQGetPPGSerialNumber());

        /// <summary>
        /// 检测当前连接的 PPG 模块类型。
        /// 返回值：null 表示未检测到 PPG 模块；否则返回模块类型描述。
        /// 判断依据：WTQGetPPGDeviceInformation 成功且 LED 类型不为空。
        /// </summary>
        public string? DetectPpgModule()
        {
            var kind = DetectPpgModuleKind();
            return kind switch
            {
                AecgPpgModuleKind.None => null,
                AecgPpgModuleKind.Green => "PPG-1R-525 (Green)",
                AecgPpgModuleKind.RedIr => "PPG-2R-880/PPG-2R-940/PPG-2TF-660 (Red+IR)",
                _ => $"Unknown PPG module (LEDs: {DescribeLeds()})"
            };
        }

        /// <summary>
        /// 结构化检测当前在位的 PPG 模块类型（供测试模块在位校验用）。
        /// AECG100 主机同一时刻只接一个 PPG 模块：绿光模块做 PPG 心率，红+红外模块做 SpO2，
        /// 测试前调用本方法确认在位模块与测试需求匹配，不匹配时提示更换模块。
        /// </summary>
        public AecgPpgModuleKind DetectPpgModuleKind()
        {
            if (!IsConnected)
                return AecgPpgModuleKind.None;

            if (!WTQGetPPGDeviceInformation(out var m))
                return AecgPpgModuleKind.None;

            var info = FromNative(m);
            var leds = new[] { info.LedType1, info.LedType2, info.LedType3 }
                .Where(t => t != AecgLedType.None)
                .ToList();

            if (leds.Count == 0)
                return AecgPpgModuleKind.None;

            if (leds.Contains(AecgLedType.Green))
                return AecgPpgModuleKind.Green;

            if (leds.Contains(AecgLedType.Red) && leds.Contains(AecgLedType.Ir))
                return AecgPpgModuleKind.RedIr;

            return AecgPpgModuleKind.Unknown;
        }

        /// <summary>当前模块 LED 组合描述（日志用）</summary>
        private string DescribeLeds()
        {
            if (!WTQGetPPGDeviceInformation(out var m)) return "N/A";
            var info = FromNative(m);
            return string.Join(",", new[] { info.LedType1, info.LedType2, info.LedType3 }
                .Where(t => t != AecgLedType.None));
        }

        private static AecgModelInformation FromNative(MODEL_INFORMATION m)
            => new(
                $"{(char)m.ProductName0}{(char)m.ProductName1}",
                m.GenerationNumber, m.ModelNumber, m.SerialNumber, m.Year,
                (AecgLedType)m.LEDType1, (AecgLedType)m.LEDType2, (AecgLedType)m.LEDType3);

        #endregion

        #region 单机模式波形输出（WTQOutput*）

        /// <summary>输出 ECG 波形；onData(time s, ac uV, dc uV) 逐点回调</summary>
        public bool OutputEcg(AecgEcgWaveform waveform, Action<double, int, int>? onData = null)
        {
            var wf = ToNative(waveform);
            return WTQOutputECG(ref wf, Wrap(onData));
        }

        /// <summary>输出 ECG + PPG（PWV 模式）；timeDiffPTTPeak 为 ECG/PPG 峰-峰时差 ms，两者频率必须相同</summary>
        public bool OutputEcgAndPpg(int timeDiffPTTPeak, AecgEcgWaveform ecg, AecgPpgWaveform ppg,
            Action<double, int, int>? onEcg = null, Action<double, int, int>? onPpg = null)
        {
            var e = ToNative(ecg);
            var p = ToNative(ppg);
            return WTQOutputECGAndPPG(timeDiffPTTPeak, ref e, ref p, Wrap(onEcg), Wrap(onPpg));
        }

        /// <summary>输出 ECG + 双通道 PPG（PWV2）</summary>
        public bool OutputEcgAndPpgEx(int timeDiffPTTPeak, AecgEcgWaveform ecg, AecgPpgWaveform ppg1, AecgPpgWaveform ppg2,
            Action<double, int, int>? onEcg = null, Action<double, int, int>? onPpg1 = null, Action<double, int, int>? onPpg2 = null)
        {
            var e = ToNative(ecg);
            var p1 = ToNative(ppg1);
            var p2 = ToNative(ppg2);
            return WTQOutputECGAndPPGEx(timeDiffPTTPeak, ref e, ref p1, ref p2, Wrap(onEcg), Wrap(onPpg1), Wrap(onPpg2));
        }

        /// <summary>输出 ECG + 三通道 PPG（PWV3；单 LED 模块须用本接口）</summary>
        public bool OutputEcgAndPpg3(int timeDiffPTTPeak, AecgEcgWaveform ecg, AecgPpgWaveform ppg1, AecgPpgWaveform ppg2, AecgPpgWaveform ppg3,
            Action<double, int, int>? onEcg = null, Action<double, int, int>? onPpg1 = null, Action<double, int, int>? onPpg2 = null, Action<double, int, int>? onPpg3 = null)
        {
            var e = ToNative(ecg);
            var p1 = ToNative(ppg1);
            var p2 = ToNative(ppg2);
            var p3 = ToNative(ppg3);
            return WTQOutputECGAndPPG3(timeDiffPTTPeak, ref e, ref p1, ref p2, ref p3, Wrap(onEcg), Wrap(onPpg1), Wrap(onPpg2), Wrap(onPpg3));
        }

        /// <summary>输出单通道 PPG（SPO2/PPG 模式）</summary>
        public bool OutputPpg(AecgPpgChannel channel, AecgPpgWaveform waveform, Action<double, int, int>? onData = null)
        {
            var wf = ToNative(waveform);
            return WTQOutputPPG((int)channel, ref wf, Wrap(onData));
        }

        /// <summary>输出双通道 PPG</summary>
        public bool OutputPpgEx(AecgPpgWaveform ch1, AecgPpgWaveform ch2,
            Action<double, int, int>? onData1 = null, Action<double, int, int>? onData2 = null)
        {
            var w1 = ToNative(ch1);
            var w2 = ToNative(ch2);
            return WTQOutputPPGEx(ref w1, ref w2, Wrap(onData1), Wrap(onData2));
        }

        /// <summary>输出三通道 PPG（单 LED 模块须用本接口）</summary>
        public bool OutputPpg3(AecgPpgWaveform ch1, AecgPpgWaveform ch2, AecgPpgWaveform ch3,
            Action<double, int, int>? onData1 = null, Action<double, int, int>? onData2 = null, Action<double, int, int>? onData3 = null)
        {
            var w1 = ToNative(ch1);
            var w2 = ToNative(ch2);
            var w3 = ToNative(ch3);
            return WTQOutputPPG3(ref w1, ref w2, ref w3, Wrap(onData1), Wrap(onData2), Wrap(onData3));
        }

        /// <summary>ECG 频率扫描（变频正弦波）</summary>
        public bool OutputFrequencyScan(AecgFrequencyScan scan, Action<double, int, int>? onData = null)
        {
            var s = new FREQUENCY_SCAN
            {
                Amplitude = scan.Amplitude,
                FrequencyStart = scan.FrequencyStart,
                FrequencyFinish = scan.FrequencyFinish,
                Duration = scan.Duration
            };
            return WTQOutputFrequencyScan(ref s, Wrap(onData));
        }

        /// <summary>PPG 频率扫描（变频正弦波）</summary>
        public bool OutputFrequencyScanPpg(AecgPpgChannel channel, AecgFrequencyScanPpg scan, Action<double, int, int>? onData = null)
        {
            var s = new FREQUENCY_SCAN2
            {
                Amplitude = scan.Amplitude,
                DC = scan.DC,
                SyncPulse = (int)scan.SyncPulse,
                FrequencyStart = scan.FrequencyStart,
                FrequencyFinish = scan.FrequencyFinish,
                Duration = scan.Duration
            };
            return WTQOutputFrequencyScanPPG((int)channel, ref s, Wrap(onData));
        }

        /// <summary>停止波形输出</summary>
        public void StopOutputWaveform()
        {
            WTQStopOutputWaveform();
            _outputCallbacks.Clear();
        }

        /// <summary>设置波形播放器是否循环</summary>
        public void SetWaveformPlayerLoop(bool loop) => WTQWaveformPlayerLoop(loop);

        #endregion

        #region play-raw 模式输出（WTQPlay*，需要调制式呼吸时用）

        /// <summary>play-raw 输出 ECG（支持调制式呼吸；无呼吸需求建议用 OutputEcg）</summary>
        public bool PlayEcg(AecgEcgWaveform waveform, Action<double, int, int>? onData = null)
        {
            var wf = ToNative(waveform);
            return WTQPlayECG(ref wf, Wrap(onData));
        }

        /// <summary>play-raw 输出 ECG + PPG（支持调制式呼吸）</summary>
        public bool PlayEcgAndPpg(int timeDiffPTTPeak, AecgEcgWaveform ecg, AecgPpgWaveform ppg,
            Action<double, int, int>? onEcg = null, Action<double, int, int>? onPpg = null)
        {
            var e = ToNative(ecg);
            var p = ToNative(ppg);
            return WTQPlayECGAndPPG(timeDiffPTTPeak, ref e, ref p, Wrap(onEcg), Wrap(onPpg));
        }

        /// <summary>play-raw 输出单通道 PPG（支持调制式呼吸）</summary>
        public bool PlayPpg(AecgPpgChannel channel, AecgPpgWaveform waveform, Action<double, int, int>? onData = null)
        {
            var wf = ToNative(waveform);
            return WTQPlayPPG((int)channel, ref wf, Wrap(onData));
        }

        #endregion

        #region 原始数据回放（WTQWaveformPlayerOutput*）

        /// <summary>
        /// 回放原始数据到 ECG 通道。ac/dc 数组在播放期间由本类固定（pin），
        /// 调用 StopPlayRawData 后释放；停止输出时回调会收到 int 最小值哨兵。
        /// </summary>
        public bool PlayRawDataEcg(double sampleRate, double[] ac, double[] dc, AecgSyncPulse sync,
            Action<double, int, int>? onData = null)
        {
            var data = BuildRawData(sampleRate, ac, dc, sync, onData);
            return WTQWaveformPlayerOutputECG(ref data);
        }

        /// <summary>回放原始数据到 ECG + 单 PPG 通道</summary>
        public bool PlayRawDataEcgAndPpg(double ecgSampleRate, double[] ecgAc, double[] ecgDc,
            AecgPpgChannel channel, double ppgSampleRate, double[] ppgAc, double[] ppgDc,
            AecgSyncPulse sync, Action<double, int, int>? onEcg = null, Action<double, int, int>? onPpg = null)
        {
            var ecg = BuildRawData(ecgSampleRate, ecgAc, ecgDc, sync, onEcg);
            var ppg = BuildRawData(ppgSampleRate, ppgAc, ppgDc, sync, onPpg);
            return WTQWaveformPlayerOutputECGAndPPG(ref ecg, (int)channel, ref ppg);
        }

        /// <summary>回放原始数据到单 PPG 通道</summary>
        public bool PlayRawDataPpg(AecgPpgChannel channel, double sampleRate, double[] ac, double[] dc,
            AecgSyncPulse sync, Action<double, int, int>? onData = null)
        {
            var data = BuildRawData(sampleRate, ac, dc, sync, onData);
            return WTQWaveformPlayerOutputPPG((int)channel, ref data);
        }

        /// <summary>回放原始数据到双通道 PPG（SpO2 红光/红外）</summary>
        public bool PlayRawDataPpgEx(
            double ch1SampleRate, double[] ch1Ac, double[] ch1Dc,
            double ch2SampleRate, double[] ch2Ac, double[] ch2Dc,
            AecgSyncPulse sync,
            Action<double, int, int>? onCh1 = null, Action<double, int, int>? onCh2 = null)
        {
            var d1 = BuildRawData(ch1SampleRate, ch1Ac, ch1Dc, sync, onCh1);
            var d2 = BuildRawData(ch2SampleRate, ch2Ac, ch2Dc, sync, onCh2);
            return WTQWaveformPlayerOutputPPGEx(ref d1, ref d2);
        }

        private PLAY_RAW_DATA BuildRawData(double sampleRate, double[] ac, double[] dc, AecgSyncPulse sync, Action<double, int, int>? onData)
        {
            if (ac.Length != dc.Length)
                throw new ArgumentException("AC/DC 数组长度必须一致");

            var acHandle = GCHandle.Alloc(ac, GCHandleType.Pinned);
            var dcHandle = GCHandle.Alloc(dc, GCHandleType.Pinned);
            _rawDataHandles.Add(acHandle);
            _rawDataHandles.Add(dcHandle);

            return new PLAY_RAW_DATA
            {
                SampleRate = sampleRate,
                Size = ac.Length,
                SyncPulse = (int)sync,
                AC = acHandle.AddrOfPinnedObject(),
                DC = dcHandle.AddrOfPinnedObject(),
                cb = Wrap(onData)
            };
        }

        /// <summary>停止原始数据回放并释放固定的数据数组</summary>
        public void StopPlayRawData()
        {
            WTQStopPlayRawData();
            FreeRawDataHandles();
        }

        private void FreeRawDataHandles()
        {
            foreach (var h in _rawDataHandles)
            {
                if (h.IsAllocated) h.Free();
            }
            _rawDataHandles.Clear();
        }

        #endregion

        #region ECG RLD 测试

        /// <summary>启用/禁用 RLD；量测完成后必须禁用</summary>
        public bool EnableRld(bool enable) => WTQEnableRLD(enable);

        /// <summary>读回 RLD N1/N2 电压；启用后首次读取前需延时约 1s</summary>
        public (double N1, double N2)? ReadRld()
            => WTQReadRLD(out var n1, out var n2) ? (n1, n2) : null;

        #endregion

        #region PPG 采样与信号检测

        /// <summary>使能采样模式；onData(data, number)，PD 采样数据范围 [0,1023]</summary>
        public bool EnableSampling(AecgPpgSampling mode, Action<int, int> onData)
        {
            _samplingCallback = (data, number) => onData(data, number);
            return WTQEnableSampling((int)mode, _samplingCallback);
        }

        /// <summary>开始采样；onError 回调采样错误码（0x10~0x13 丢包）</summary>
        public bool StartSampling(Action<int>? onError = null)
        {
            _samplingErrorCallback = error => onError?.Invoke(error);
            return WTQStartSampling(_samplingErrorCallback);
        }

        /// <summary>停止采样并复位；再次采样需重新 EnableSampling</summary>
        public void DisableSampling() => WTQDisableSampling();

        /// <summary>DUT 信号强度检测（仅手指模块）；location 50=最佳，&lt;0 未检测到</summary>
        public bool EnableDutSignalDetection(bool enable, Action<int> onLocation)
        {
            _dutDetectionCallback = location => onLocation(location);
            return WTQEnableDUTSignalDetection(enable, _dutDetectionCallback);
        }

        /// <summary>设置 PPG AC 在 DC 之上（默认 AC 在 DC 之下）</summary>
        public void EnableAcAboveDC(bool enable) => WTQEnableACAboveDC(enable);

        /// <summary>启用动态 LED 强度（WAP2012/WAP2112）</summary>
        public bool EnableDynamicLedStrength(bool enable) => WTQEnableDynamicLEDStrength(enable);

        /// <summary>透射率模式（仅 WAP2112，须在信号输出后调用）：0 新生儿足部 ~ 5 浅色细手指</summary>
        public bool ConfigureTransmissionRate(int mode) => WTQConfigureTransmissionRate(mode);

        #endregion

        #region ECG 输出控制 / PPG LED 配置

        /// <summary>使能 620K 阻抗</summary>
        public bool SetImpedance(AecgEcgImpedance impedance) => WTQDeviceEnableImpedance((int)impedance);

        /// <summary>设置输出电极（RA/LA）</summary>
        public bool SetElectrode(AecgElectrode electrode) => WTQDeviceSetElectrode((int)electrode);

        /// <summary>设置固定 DC 偏置（仅允许 300/0/-300）</summary>
        public bool SetDcOffset(int dcOffset) => WTQDeviceSetDCOffset(dcOffset);

        /// <summary>使能起搏脉冲输出</summary>
        public bool EnablePacing(AecgEcgPacing pacing) => WTQDeviceEnablePacing((int)pacing);

        /// <summary>使能 ECG 呼吸（仅 Off / Resistance 有效）</summary>
        public bool EnableRespiration(AecgRespirationMode respiration) => WTQDeviceEnableRespiration((int)respiration);

        /// <summary>读回 LED 脉冲组配置（单/双 LED 模拟三通道模块）</summary>
        public bool ReadLedPulseGroupSetting(out byte[] pulseLedTable, out ushort pulseGroupIntervalUs)
        {
            var setting = new PPG_LED_PULSE_GROUP_SETTING { PulseLEDTable = new byte[8] };
            var ok = WTQReadLEDPulseGroupSetting(ref setting);
            pulseLedTable = setting.PulseLEDTable;
            pulseGroupIntervalUs = setting.PulseGroupInterval;
            return ok;
        }

        /// <summary>写入 LED 脉冲组配置；intervalUs=0 为 IR-first 模式（表首必须为 IR）</summary>
        public bool WriteLedPulseGroupSetting(byte[] pulseLedTable, ushort pulseGroupIntervalUs)
        {
            if (pulseLedTable.Length != 8)
                throw new ArgumentException("PulseLEDTable 必须为 8 字节");

            var setting = new PPG_LED_PULSE_GROUP_SETTING { PulseLEDTable = pulseLedTable, PulseGroupInterval = pulseGroupIntervalUs };
            return WTQWriteLEDPulseGroupSetting(ref setting);
        }

        /// <summary>读回环境光模式（仅 WAP2012）</summary>
        public AecgPpgAmbientLightMode GetAmbientLightMode() => (AecgPpgAmbientLightMode)WTQGetLEDAmbientLightMode();

        /// <summary>设置环境光模式（仅 WAP2012）</summary>
        public bool SetAmbientLightMode(AecgPpgAmbientLightMode mode) => WTQSetLEDAmbientLightMode((int)mode);

        #endregion

        #region 独立模式波形读写

        /// <summary>读取独立模式槽位当前的工作模式类型</summary>
        public AecgModeType ReadStandaloneModeType(AecgStandAloneMode mode) => (AecgModeType)WTQReadStandaloneModeType((int)mode);

        /// <summary>从独立模式读取 ECG 波形参数（返回 0=成功）</summary>
        public AecgEcgWaveform? ReadEcgFromStandalone(AecgStandAloneMode mode)
        {
            var wf = EmptyEcgNative();
            return WTQReadECGModeFromStandaloneMode((int)mode, ref wf) == 0 ? FromNative(wf) : null;
        }

        /// <summary>向独立模式写入 ECG 波形参数</summary>
        public bool WriteEcgToStandalone(AecgStandAloneMode mode, AecgEcgWaveform waveform)
        {
            var wf = ToNative(waveform);
            return WTQWriteECGModeToStandaloneMode((int)mode, ref wf) == 0;
        }

        /// <summary>从独立模式读取 PWV（ECG+PPG）波形参数</summary>
        public (int TimeDiffPTTPeak, AecgEcgWaveform Ecg, AecgPpgWaveform Ppg)? ReadPwvFromStandalone(AecgStandAloneMode mode)
        {
            var e = EmptyEcgNative();
            var p = EmptyPpgNative();
            return WTQReadPWVModeFromStandaloneMode((int)mode, out var diff, ref e, ref p) == 0
                ? (diff, FromNative(e), FromNative(p))
                : null;
        }

        /// <summary>向独立模式写入 PWV（ECG+PPG）波形参数</summary>
        public bool WritePwvToStandalone(AecgStandAloneMode mode, int timeDiffPTTPeak, AecgEcgWaveform ecg, AecgPpgWaveform ppg)
        {
            var e = ToNative(ecg);
            var p = ToNative(ppg);
            return WTQWritePWVModeToStandaloneMode((int)mode, timeDiffPTTPeak, ref e, ref p) == 0;
        }

        /// <summary>从独立模式读取 SPO2（双通道 PPG）波形参数</summary>
        public (AecgPpgWaveform Channel1, AecgPpgWaveform Channel2)? ReadSpo2FromStandalone(AecgStandAloneMode mode)
        {
            var w1 = EmptyPpgNative();
            var w2 = EmptyPpgNative();
            return WTQReadSPO2ModeFromStandaloneMode((int)mode, ref w1, ref w2) == 0
                ? (FromNative(w1), FromNative(w2))
                : null;
        }

        /// <summary>向独立模式写入 SPO2（双通道 PPG）波形参数</summary>
        public bool WriteSpo2ToStandalone(AecgStandAloneMode mode, AecgPpgWaveform ch1, AecgPpgWaveform ch2)
        {
            var w1 = ToNative(ch1);
            var w2 = ToNative(ch2);
            return WTQWriteSPO2ModeToStandaloneMode((int)mode, ref w1, ref w2) == 0;
        }

        /// <summary>从独立模式读取 PPG（单 LED）波形参数</summary>
        public AecgPpgWaveform? ReadPpgFromStandalone(AecgStandAloneMode mode)
        {
            var wf = EmptyPpgNative();
            return WTQReadPPGModeFromStandaloneMode((int)mode, ref wf) == 0 ? FromNative(wf) : null;
        }

        /// <summary>向独立模式写入 PPG（单 LED）波形参数</summary>
        public bool WritePpgToStandalone(AecgStandAloneMode mode, AecgPpgWaveform waveform)
        {
            var wf = ToNative(waveform);
            return WTQWritePPGModeToStandaloneMode((int)mode, ref wf) == 0;
        }

        #endregion

        #region 校准 / 多机同步 / 版本

        /// <summary>恢复 PPG 模块 LED/触发电平出厂设置</summary>
        public bool RestoreFactorySetting() => WTQRestoreFactorySetting();

        /// <summary>读回 PPG LED 电平（0~255）</summary>
        public ushort? ReadPpgLedLevel(AecgPpgChannel channel)
            => WTQReadPPGLedCalibrationSetting((int)channel, out var level) ? level : null;

        /// <summary>写入 PPG LED 电平（0~255）</summary>
        public bool WritePpgLedLevel(AecgPpgChannel channel, ushort level)
            => WTQWritePPGLedCalibrationSetting((int)channel, level);

        /// <summary>读回 PPG 触发电平（0~255）</summary>
        public ushort? ReadPpgTriggerLevel(AecgPpgChannel channel)
            => WTQReadPPGTriggerLevelCalibrationSetting((int)channel, out var level) ? level : null;

        /// <summary>写入 PPG 触发电平（0~255）</summary>
        public bool WritePpgTriggerLevel(AecgPpgChannel channel, ushort level)
            => WTQWritePPGTriggerLevelCalibrationSetting((int)channel, level);

        /// <summary>创建多机同步信号（手动复位事件），多设备同时输出前各设备 Set 后统一置位</summary>
        public IntPtr CreateSynchronizationSignal() => WTQCreateSynchronizationSignal();

        /// <summary>为本设备绑定同步事件（后续输出将等待该事件置位）</summary>
        public void SetSynchronizationSignal(IntPtr syncObject) => WTQSetSynchronizationSignal(syncObject);

        /// <summary>置位同步事件，所有设备同时开始输出</summary>
        public void SignalSyncEvent(IntPtr syncObject) => WTQSynchronizationSignalSetEvent(syncObject);

        /// <summary>复位同步事件</summary>
        public void ResetSyncEvent(IntPtr syncObject) => WTQSynchronizationSignalResetEvent(syncObject);

        /// <summary>DLL 版本（四段式，如 1.0.5.4）</summary>
        public Version GetDllVersion()
        {
            var v = WTQGetVersion();
            return new Version((int)(v >> 24 & 0xFF), (int)(v >> 16 & 0xFF), (int)(v >> 8 & 0xFF), (int)(v & 0xFF));
        }

        #endregion

        #region 托管 ↔ 原生 转换

        private OutputSignalCallbackDelegate Wrap(Action<double, int, int>? onData)
        {
            OutputSignalCallbackDelegate del = onData == null
                ? (_, _, _) => { }
                : new OutputSignalCallbackDelegate((t, ac, dc) => onData(t, ac, dc));
            _outputCallbacks.Add(del);
            return del;
        }

        private static ECG_WAVEFORM ToNative(AecgEcgWaveform w)
        {
            var n = EmptyEcgNative();
            n.WaveformType = (int)w.WaveformType;
            n.Frequency = w.Frequency;
            n.Amplitude = w.Amplitude;
            n.TWave = w.TWave;
            n.PWave = w.PWave;
            n.STSegment = w.STSegment;
            n.DCOffsetVariable = w.DCOffsetVariable ? 1 : 0;
            n.DCOffset = w.DCOffset;
            n.TimePeriod = w.TimePeriod;
            n.PRInterval = w.PRInterval;
            n.QRSDuration = w.QRSDuration;
            n.TDuration = w.TDuration;
            n.QTInterval = w.QTInterval;
            n.Impedance = (int)w.Impedance;
            n.Electrode = (int)w.Electrode;
            n.PulseWidth = w.PulseWidth;
            n.NoiseAmplitude = w.NoiseAmplitude;
            n.NoiseFrequency = (int)w.NoiseFrequency;
            n.PacingEnabled = w.PacingEnabled ? 1 : 0;
            n.PacingAmplitude = w.PacingAmplitude;
            n.PacingDuration = w.PacingDuration;
            n.PacingRate = w.PacingRate;
            n.RespirationMode = (int)w.RespirationMode;
            n.RespirationAmplitude = w.RespirationAmplitude;
            n.RespirationRate = w.RespirationRate;
            n.RespirationRatio = w.RespirationRatio;
            n.RespirationBaseline = w.RespirationBaseline;
            n.RespirationApneaDuration = w.RespirationApneaDuration;
            n.RespirationApneaCycle = w.RespirationApneaCycle;
            return n;
        }

        private static AecgEcgWaveform FromNative(ECG_WAVEFORM n)
            => new()
            {
                WaveformType = (AecgEcgWaveformType)n.WaveformType,
                Frequency = n.Frequency,
                Amplitude = n.Amplitude,
                TWave = n.TWave,
                PWave = n.PWave,
                STSegment = n.STSegment,
                DCOffsetVariable = n.DCOffsetVariable != 0,
                DCOffset = n.DCOffset,
                TimePeriod = n.TimePeriod,
                PRInterval = n.PRInterval,
                QRSDuration = n.QRSDuration,
                TDuration = n.TDuration,
                QTInterval = n.QTInterval,
                Impedance = (AecgEcgImpedance)n.Impedance,
                Electrode = (AecgElectrode)n.Electrode,
                PulseWidth = n.PulseWidth,
                NoiseAmplitude = n.NoiseAmplitude,
                NoiseFrequency = (AecgEcgNoiseFrequency)n.NoiseFrequency,
                PacingEnabled = n.PacingEnabled != 0,
                PacingAmplitude = n.PacingAmplitude,
                PacingDuration = n.PacingDuration,
                PacingRate = n.PacingRate,
                RespirationMode = (AecgRespirationMode)n.RespirationMode,
                RespirationAmplitude = n.RespirationAmplitude,
                RespirationRate = n.RespirationRate,
                RespirationRatio = n.RespirationRatio,
                RespirationBaseline = n.RespirationBaseline,
                RespirationApneaDuration = n.RespirationApneaDuration,
                RespirationApneaCycle = n.RespirationApneaCycle
            };

        private static PPG_WAVEFORM ToNative(AecgPpgWaveform w)
        {
            var n = EmptyPpgNative();
            n.WaveformType = (int)w.WaveformType;
            n.Frequency = w.Frequency;
            n.VolDC = w.VolDC;
            n.VolSP = w.VolSP;
            n.VolDN = w.VolDN;
            n.VolDP = w.VolDP;
            n.ACOffset = w.ACOffset;
            n.TimeSP = w.TimeSP;
            n.TimeDN = w.TimeDN;
            n.TimeDP = w.TimeDP;
            n.TimePeriod = w.TimePeriod;
            n.SyncPulse = (int)w.SyncPulse;
            n.Inverted = (int)w.Inverted;
            n.NoiseAmplitude = w.NoiseAmplitude;
            n.NoiseFrequency = (int)w.NoiseFrequency;
            n.RespirationMode = (int)w.RespirationMode;
            n.RespirationRate = w.RespirationRate;
            n.RespirationVariation = w.RespirationVariation;
            n.RespirationInExhaleRatio = w.RespirationInExhaleRatio;
            n.RespirationApneaDuration = w.RespirationApneaDuration;
            n.RespirationApneaCycle = w.RespirationApneaCycle;
            return n;
        }

        private static AecgPpgWaveform FromNative(PPG_WAVEFORM n)
            => new()
            {
                WaveformType = (AecgPpgWaveformType)n.WaveformType,
                Frequency = n.Frequency,
                VolDC = n.VolDC,
                VolSP = n.VolSP,
                VolDN = n.VolDN,
                VolDP = n.VolDP,
                ACOffset = n.ACOffset,
                TimeSP = n.TimeSP,
                TimeDN = n.TimeDN,
                TimeDP = n.TimeDP,
                TimePeriod = n.TimePeriod,
                SyncPulse = (AecgSyncPulse)n.SyncPulse,
                Inverted = (AecgPpgInverted)n.Inverted,
                NoiseAmplitude = n.NoiseAmplitude,
                NoiseFrequency = (AecgPpgNoiseFrequency)n.NoiseFrequency,
                RespirationMode = (AecgRespirationMode)n.RespirationMode,
                RespirationRate = n.RespirationRate,
                RespirationVariation = n.RespirationVariation,
                RespirationInExhaleRatio = n.RespirationInExhaleRatio,
                RespirationApneaDuration = n.RespirationApneaDuration,
                RespirationApneaCycle = n.RespirationApneaCycle
            };

        private static ECG_WAVEFORM EmptyEcgNative() => new() { Reserved = new byte[12] };

        private static PPG_WAVEFORM EmptyPpgNative() => new() { Reserved = new byte[12] };

        #endregion
    }
}
