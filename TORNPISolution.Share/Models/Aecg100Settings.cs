namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 鲸扬 AECG100 多功能综合性能测试仪（WhaleTeq）连接与测试配置。
    /// 设备通过 USB 虚拟串口（VCP）连接，通讯由官方SDK（AECG100x64.dll）处理，
    /// 上位机只需指定 COM 口与连接超时；串口号留空表示自动选择第一台 AECG100。
    /// </summary>
    public class Aecg100Settings
    {
        // ==================== 连接参数 ====================

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
        /// ECG-004 心率检测精度测试点（BPM），仪器按 BPM/60 Hz 输出 ECG 波形；每组重复取最大值与设定值比对，误差 ≤ 容差为 PASS。
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

        // ==================== ECG-009/012 频率扫描配置（ECG-012 暂未实现，保留字段） ====================

        /// <summary>频率扫描幅度 mV（默认 1.0）</summary>
        public double FrequencyScanAmplitude { get; set; } = 1.0;

        /// <summary>频率扫描起始频率 Hz（默认 0.5）</summary>
        public double FrequencyScanStart { get; set; } = 0.5;

        /// <summary>频率扫描终止频率 Hz（默认 150）</summary>
        public double FrequencyScanFinish { get; set; } = 150.0;

        /// <summary>频率扫描时长 s（默认 30）</summary>
        public int FrequencyScanDuration { get; set; } = 30;

        // ==================== ECG-009 动态范围和线性配置（A10 唯一实现项） ====================
        // 一项一点，幅度档为外层、DC 偏置档为内层（给定推进顺序）：默认 4 幅度 × 3 偏置 = 12 项。
        // 项名见 SpecItems.Ecg009Point，Spec 目录与执行端同源生成。

        /// <summary>ECG-009 三角波幅度档 mV 波峰-波谷（法规值 0.5/1/2/10；≤0 的档在目录构建与执行端均被过滤）</summary>
        public List<double> Ecg009Amplitudes { get; set; } = new() { 0.5, 1, 2, 10 };

        /// <summary>ECG-009 DC 偏置档 mV（仪器固定偏置模式支持 300/0/-300，即法规三档）</summary>
        public List<int> Ecg009DcOffsets { get; set; } = new() { 0, 300, -300 };

        /// <summary>ECG-009 波形频率 Hz（法规 6.25，即 160ms 一周期）</summary>
        public double Ecg009Frequency { get; set; } = 6.25;

        /// <summary>ECG-009 波形类型（0=Sine 1=Triangle 2=Square 3=RectanglePulse 4=TrianglePulse 5=Exponential 6=ECG；法规要 Triangle=1）</summary>
        public int Ecg009WaveformType { get; set; } = 1;

        /// <summary>ECG-009 输出电极（0=RA/R，255=LA/L，界面二选一）</summary>
        public int Ecg009Electrode { get; set; } = 0;

        /// <summary>ECG-009 噪声发生器频率（0=Off 1=50Hz 2=60Hz 3=100Hz 4=120Hz；法规要 Off）</summary>
        public int Ecg009NoiseFrequency { get; set; }

        /// <summary>ECG-009 是否串入 620kΩ 输入阻抗（默认 false＝短路；勾选后本档波形经 620kΩ 输出）</summary>
        public bool Ecg009Impedance { get; set; }

        /// <summary>ECG-009 串口采集丢弃时长 s（开始输出后先丢弃 0x8D 数据这么多秒，避开产品进入心电测量的起始瞬态；默认 20）。</summary>
        public int Ecg009DiscardSeconds { get; set; } = 20;

        /// <summary>ECG-009 串口有效采集时长 s（丢弃后连续记录判定，默认 60）；单项耗时 = 丢弃 + 有效 + 换档等待，到点即停输出进下一档。</summary>
        public int Ecg009RecordSeconds { get; set; } = 60;

        /// <summary>ECG-009 换档等待 ms（停输出→下一档开始输出之间，默认 500）</summary>
        public int Ecg009StepDelayMs { get; set; } = 500;

        /// <summary>ECG-009 允差分段分界幅度 mV（默认 2：≤本值用绝对允差，>本值用百分比允差）</summary>
        public double Ecg009ToleranceSplitMv { get; set; } = 2;

        /// <summary>ECG-009 幅值允差 %（默认 10；仅用于 >分界档 的幅度档，如 10mV 档 → ±1mV）</summary>
        public double Ecg009TolerancePercent { get; set; } = 10;

        /// <summary>ECG-009 幅值绝对允差 μV（默认 50；用于 ≤分界档 的小幅度档，即 0.5/1/2mV 三档）</summary>
        public double Ecg009ToleranceFloorUv { get; set; } = 50;

        /// <summary>
        /// ECG-009 单档允差（mV），法规分段口径的唯一实现处（Spec 目录与执行端都调本方法，避免两边算法漂开）：
        /// 幅度 ≤ 分界档（默认 2mV）→ 绝对允差 50μV；幅度 > 分界档（默认 10mV）→ 设定幅度 × 10%。
        /// </summary>
        public double Ecg009ToleranceMv(double amplitude)
            => amplitude <= (Ecg009ToleranceSplitMv > 0 ? Ecg009ToleranceSplitMv : 2)
                ? (Ecg009ToleranceFloorUv >= 0 ? Ecg009ToleranceFloorUv : 50) / 1000.0
                : amplitude * (Ecg009TolerancePercent > 0 ? Ecg009TolerancePercent : 10) / 100.0;

        // ==================== ECG DC 偏置配置 ====================

        /// <summary>DC 偏置模式（0=固定 300/0/-300，1=可变 -500~500）</summary>
        public bool DCOffsetVariable { get; set; }

        /// <summary>DC 偏置值 mV（固定模式：300/0/-300；可变模式：-500~500）</summary>
        public int DCOffset { get; set; }

        // ==================== ECG 呼吸调制配置（ECG 模式的 Respiration 参数） ====================

        /// <summary>呼吸模式（AecgRespirationMode 位组合：0=Off, 1=基线, 2=幅度, 4=频率, 128=阻抗式）</summary>
        public int DefaultRespirationMode { get; set; }

        /// <summary>呼吸速率 BrPM（4~200，默认 15）</summary>
        public int DefaultRespirationRate { get; set; } = 15;

        /// <summary>呼吸幅度 milli-ohm（阻抗式 1000~5000，默认 2000 = 2.0ohm）</summary>
        public int DefaultRespirationAmplitude { get; set; } = 2000;

        /// <summary>呼吸比率：阻抗式 1~5（1:1~1:5）；调制式 -16~16% 变化量</summary>
        public int DefaultRespirationRatio { get; set; } = 1;

        // ==================== PPG 心率测试配置（需 PPG-1R-525 绿光模块在位） ====================
        // PPG-001 脉搏率精度：仪器经绿光 LED 输出 PPG 波形（频率 = BPM/60），
        // 产品光学传感器接收后计算心率，与设定值比对。

        /// <summary>PPG 心率测试点（BPM，默认 60/80/100/120）</summary>
        public List<int> PpgHeartRateTestPoints { get; set; } = new() { 60, 80, 100, 120 };

        /// <summary>PPG 心率每档重复次数（默认 3）</summary>
        public int PpgHeartRateRepeats { get; set; } = 3;

        /// <summary>PPG 心率容差（BPM，默认 ±2）</summary>
        public double PpgHeartRateTolerance { get; set; } = 2.0;

        /// <summary>PPG 波形直流分量 mV（0~3000，默认 1000）</summary>
        public double PpgVolDC { get; set; } = 1000;

        /// <summary>PPG 主波（收缩峰）幅度 mV（AC+ACOffset 之和 ≤ 30）</summary>
        public double PpgVolSP { get; set; } = 10;

        /// <summary>PPG 重搏切迹幅度 mV</summary>
        public double PpgVolDN { get; set; } = 6;

        /// <summary>PPG 重搏波幅度 mV</summary>
        public double PpgVolDP { get; set; } = 7;

        /// <summary>PPG 主波时刻 ms</summary>
        public int PpgTimeSP { get; set; } = 150;

        /// <summary>PPG 重搏切迹时刻 ms</summary>
        public int PpgTimeDN { get; set; } = 350;

        /// <summary>PPG 重搏波时刻 ms</summary>
        public int PpgTimeDP { get; set; } = 450;

        /// <summary>PPG 心率每档采集时长 ms（默认 5000）</summary>
        public int PpgCaptureTimeMs { get; set; } = 5000;

        // ==================== SpO2 血氧模拟测试配置（需 PPG-2R-940 红光+红外模块在位） ====================
        // SpO2 原理：产品按 R=(AC红/DC红)/(AC红外/DC红外) 经自身标定曲线换算 SpO2，
        // 模拟指定 SpO2% 即按产品标定曲线反解出 R，再换算红光通道 AC 幅度（红外 AC 固定）。

        /// <summary>SPO2-004 血氧饱和度精度测试点（%，默认 75/80/90/95/100）</summary>
        public List<int> Spo2TestPoints { get; set; } = new() { 75, 80, 90, 95, 100 };

        /// <summary>SPO2-005 脉搏率精度测试点（BPM，默认 40/60/80/100/120/160）</summary>
        public List<int> Spo2PulseRateTestPoints { get; set; } = new() { 40, 60, 80, 100, 120, 160 };

        /// <summary>SPO2-005 脉搏率测试时输出的参考血氧值（%，默认 98）</summary>
        public int Spo2PulseRateRefSpo2 { get; set; } = 98;

        /// <summary>SPO2-006 低氧范围精度测试点（%，默认 50/60/70）</summary>
        public List<int> Spo2LowRangeTestPoints { get; set; } = new() { 50, 60, 70 };

        /// <summary>SPO2-006 低氧范围容差（%，默认 ±3）</summary>
        public double Spo2LowRangeTolerance { get; set; } = 3.0;

        /// <summary>SpO2 模拟脉搏率（BPM，默认 75）</summary>
        public int Spo2PulseRate { get; set; } = 75;

        /// <summary>SpO2 容差（%，默认 ±2，参照 ISO 80601-2-61 精度要求）</summary>
        public double Spo2Tolerance { get; set; } = 2.0;

        /// <summary>SpO2 模式脉搏率容差（BPM，默认 ±2）</summary>
        public double Spo2PulseRateTolerance { get; set; } = 2.0;

        /// <summary>产品标定曲线系数 A：SpO2 = A - B×R（默认 110）</summary>
        public double Spo2CalibrationA { get; set; } = 110;

        /// <summary>产品标定曲线系数 B：SpO2 = A - B×R（默认 25）</summary>
        public double Spo2CalibrationB { get; set; } = 25;

        /// <summary>红外通道 AC 幅度 mV（红光 AC 按 R 比例计算，默认 10）</summary>
        public double Spo2IrAcAmplitude { get; set; } = 10;

        /// <summary>SpO2 双通道直流分量 mV（红/红外一致，默认 1000）</summary>
        public double Spo2VolDC { get; set; } = 1000;

        /// <summary>SpO2 每点采集时长 ms（默认 8000）</summary>
        public int Spo2CaptureTimeMs { get; set; } = 8000;

        /// <summary>
        /// 按测试项名存放 ECG/PPG/SpO2 固定项的波形配置。
        /// 界面读 TimeScale 画横轴，执行端仅 ECG-001/002/004 读 Frequency/CaptureTimeMs；未配置项用内置默认，ECG-009 按档位动态生成不走本字典。
        /// </summary>
        public Dictionary<string, EcgWaveformItemConfig> WaveformItemConfigs { get; set; } = new();

        /// <summary>获取指定测试项的波形配置；未配置时返回默认值对象</summary>
        public EcgWaveformItemConfig GetWaveformConfig(string itemName)
        {
            if (WaveformItemConfigs.TryGetValue(itemName, out var cfg)) return cfg;
            return new EcgWaveformItemConfig();
        }

        /// <summary>PPG LED 校准参数（红光/红外/绿光 × LED Level + Trigger Level）</summary>
        public PpgLedCalibrationConfig LedCalibration { get; set; } = new();
    }

    /// <summary>
    /// 单个 ECG 测试项的波形与显示配置。
    /// 键值放在 <see cref="Aecg100Settings.WaveformItemConfigs"/> 中，按测试项名（如 ECG-002...）查找。
    /// 任何字段 ≤0 时使用默认值。
    /// </summary>
    public class EcgWaveformItemConfig
    {
        /// <summary>输出频率 Hz（ECG 波形频率或频率扫描起始频率）；≤0 表示使用代码默认</summary>
        public double Frequency { get; set; } = -1;

        /// <summary>波形采集时长（毫秒）；≤0 表示使用代码默认（一般 3000ms）</summary>
        public int CaptureTimeMs { get; set; } = -1;

        /// <summary>波形显示时间像素（像素/秒）；≤0 表示使用默认 24 px/s</summary>
        public double TimeScale { get; set; } = -1;
    }

    /// <summary>PPG LED 校准参数：三通道（红光/红外/绿光）各自的 LED Level 和 Trigger Level（0~255）</summary>
    public class PpgLedCalibrationConfig
    {
        public ushort RedLedLevel { get; set; } = 128;
        public ushort RedTriggerLevel { get; set; } = 128;
        public ushort IrLedLevel { get; set; } = 128;
        public ushort IrTriggerLevel { get; set; } = 128;
        public ushort GreenLedLevel { get; set; } = 128;
        public ushort GreenTriggerLevel { get; set; } = 128;
    }
}
