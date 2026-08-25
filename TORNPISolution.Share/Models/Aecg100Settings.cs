namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 鲸扬 AECG100 多功能综合性能测试仪（WhaleTeq）连接与测试配置。
    /// 设备通过 USB 虚拟串口（VCP）连接，通讯由官方原生 SDK（AECG100x64.dll）处理，
    /// 上位机只需指定 COM 口与连接超时；串口号留空表示自动选择第一台 AECG100。
    /// </summary>
    public class Aecg100Settings
    {
        // ==================== 连接参数 ====================

        /// <summary>是否启用该仪器</summary>
        public bool Enabled { get; set; }

        /// <summary>串口号（如 COM5）；留空表示自动选择</summary>
        public string PortName { get; set; } = string.Empty;

        /// <summary>连接超时（毫秒），-1 表示无限等待（默认 5000ms）</summary>
        public int ConnectTimeoutMs { get; set; } = 5000;

        // ==================== ECG 默认波形参数 ====================

        /// <summary>ECG 默认波形类型（0=Sine, 1=Triangle, 2=Square, 3=RectanglePulse, 4=TrianglePulse, 5=Exponential, 6=ECG）</summary>
        public int DefaultWaveformType { get; set; } = 6;

        /// <summary>ECG 默认幅度 mV（默认 1.0）</summary>
        public double DefaultAmplitude { get; set; } = 1.0;

        /// <summary>ECG 默认 T 波幅度 mV（默认 0.2）</summary>
        public double DefaultTWave { get; set; } = 0.2;

        /// <summary>ECG 默认 P 波幅度 mV（默认 0.2）</summary>
        public double DefaultPWave { get; set; } = 0.2;

        /// <summary>ECG 默认 ST 段 mV（默认 0.0）</summary>
        public double DefaultSTSegment { get; set; } = 0.0;

        /// <summary>ECG 默认波形周期 ms（默认 1000，对应 60BPM）</summary>
        public int DefaultTimePeriod { get; set; } = 1000;

        /// <summary>ECG 默认 PR 间期 ms（默认 160）</summary>
        public int DefaultPRInterval { get; set; } = 160;

        /// <summary>ECG 默认 QRS 波群宽度 ms（默认 100）</summary>
        public int DefaultQRSDuration { get; set; } = 100;

        /// <summary>ECG 默认 T 波宽度 ms（默认 180）</summary>
        public int DefaultTDuration { get; set; } = 180;

        /// <summary>ECG 默认 QT 间期 ms（默认 350）</summary>
        public int DefaultQTInterval { get; set; } = 350;

        /// <summary>ECG 默认脉冲宽度 ms（仅 RectanglePulse/TrianglePulse/Exponential 有效，默认 100）</summary>
        public int DefaultPulseWidth { get; set; } = 100;

        /// <summary>输出电极（0=RA 右臂，255=LA 左臂）</summary>
        public int DefaultElectrode { get; set; } = 0;

        // ==================== ECG-004 心率测试配置 ====================

        /// <summary>
        /// ECG-004 心率检测精度测试点（BPM）。
        /// 每个心率点仪器输出对应频率的 ECG 波形（频率 = BPM/60 Hz），
        /// 每组重复 N 次取最大值与设定值比对，误差 ≤ 容差 为 PASS。
        /// 默认 6 档：40/60/80/100/120/160。
        /// </summary>
        public List<int> HeartRateTestPoints { get; set; } = new() { 40, 60, 80, 100, 120, 160 };

        /// <summary>ECG-004 每组心率重复测试次数（默认 3，取最大值）</summary>
        public int HeartRateRepeats { get; set; } = 3;

        /// <summary>ECG-004 心率容差（BPM，默认 ±2）</summary>
        public double HeartRateTolerance { get; set; } = 2.0;

        // ==================== ECG 噪声 / 干扰配置 ====================

        /// <summary>ECG 噪声幅度 mV（最大 2.00，默认 0 无噪声）</summary>
        public double NoiseAmplitude { get; set; } = 0.0;

        /// <summary>ECG 噪声频率（0=Off, 1=50Hz, 2=60Hz, 3=100Hz, 4=120Hz）</summary>
        public int NoiseFrequency { get; set; } = 0;

        // ==================== ECG 阻抗 / 起搏器配置 ====================

        /// <summary>是否启用 620K 阻抗（ECG-010 输入阻抗测试用）</summary>
        public bool ImpedanceEnabled { get; set; }

        /// <summary>是否启用起搏脉冲输出</summary>
        public bool PacingEnabled { get; set; }

        /// <summary>起搏幅度 mV（-1000~1000，默认 2）</summary>
        public double PacingAmplitude { get; set; } = 2.0;

        /// <summary>起搏持续时间 ms（0~2，分辨率 0.1，默认 2）</summary>
        public double PacingDuration { get; set; } = 2.0;

        /// <summary>起搏速率 BPM（默认 60）</summary>
        public int PacingRate { get; set; } = 60;

        // ==================== ECG-009/012 频率扫描配置 ====================

        /// <summary>频率扫描幅度 mV（默认 1.0）</summary>
        public double FrequencyScanAmplitude { get; set; } = 1.0;

        /// <summary>频率扫描起始频率 Hz（默认 0.5）</summary>
        public double FrequencyScanStart { get; set; } = 0.5;

        /// <summary>频率扫描终止频率 Hz（默认 150）</summary>
        public double FrequencyScanFinish { get; set; } = 150.0;

        /// <summary>频率扫描时长 s（默认 30）</summary>
        public int FrequencyScanDuration { get; set; } = 30;

        // ==================== ECG DC 偏置配置 ====================

        /// <summary>DC 偏置模式（0=固定 300/0/-300，1=可变 -500~500）</summary>
        public bool DCOffsetVariable { get; set; }

        /// <summary>DC 偏置值 mV（固定模式：300/0/-300；可变模式：-500~500）</summary>
        public int DCOffset { get; set; }
    }
}
