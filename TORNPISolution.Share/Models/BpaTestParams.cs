namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 鲸扬 BPA700 测试参数配置（对应官方上位机的三类测试），
    /// 随 instruments.json 一起保存/加载，供测试模块执行时读取。
    /// </summary>
    public class BpaTestParams
    {
        /// <summary>静态压力参数（BPARunStaticPressure，勾选的压力点由上位机逐点循环调用）</summary>
        public BpaStaticPressureParams StaticPressure { get; set; } = new();

        /// <summary>动态血压参数（BPARunBPTest，Default/Customized 两种方法）</summary>
        public BpaDynamicBpParams DynamicBloodPressure { get; set; } = new();

        /// <summary>超压测试参数（BPARunOverpressureTest）</summary>
        public BpaOverpressureParams Overpressure { get; set; } = new();

        /// <summary>产品充放气测试（BP-001/BP-002）规格与超时</summary>
        public BpaCuffSpec CuffTest { get; set; } = new();

        /// <summary>压力控制功能测试（BP-006~008）过程参数</summary>
        public BpaPressureControlParams PressureControl { get; set; } = new();

        /// <summary>波形显示配置（按测试项自定义 X 轴模式与时间刻度，Y 轴固定 mmHg）</summary>
        public BpaWaveformDisplayConfig WaveformDisplay { get; set; } = new();
    }

    /// <summary>
    /// 产品充放气测试（M03 BP-001/BP-002）规格与超时配置。
    /// 超时避免产品异常时测试一直充气不停止：超时后自动停止本轮并判 FAIL。
    /// </summary>
    public class BpaCuffSpec
    {
        /// <summary>等待按键启动超时（秒）：BPARunBPTest 进入待测后，等待产品物理按键的最长时间</summary>
        public int WaitStartTimeoutSeconds { get; set; } = 30;

        /// <summary>整轮测量超时（秒）：启动后充气→稳压→放气全程的兜底上限，超过即停止测试</summary>
        public int TotalTimeoutSeconds { get; set; } = 60;

        /// <summary>BP-002 快速放气时间规格上限（秒）：放气时间 ≤ 上限视为放气完成（PASS）</summary>
        public double MaxDeflationSeconds { get; set; } = 10;
    }

    /// <summary>
    /// 静态压力测试参数。SDK 一次调用只支持 1 个压力点，
    /// 多点测试由上位机按勾选的点循环调用，最多 10 个（与官方上位机一致）。
    /// </summary>
    public class BpaStaticPressureParams
    {
        /// <summary>BPA 充气：true=BPA700 自行充气，false=外部设备充气</summary>
        public bool BpaInflation { get; set; } = true;

        /// <summary>量测时间（每个压力点的稳压保持秒数，0~999；仅 BPA700 序列有效，康伟达按收敛判据 2.5s 稳压窗取均值、不额外保持）</summary>
        public int MeasureSeconds { get; set; } = 10;

        /// <summary>压力点列表（单位跟随仪器压力单位：mmHg 20~400 / kPa 2.7~53.3），默认官方 10 点，上限 10 个</summary>
        public List<BpaPressurePoint> Points { get; set; } = new()
        {
            new() { Value = 30 },  new() { Value = 60 },  new() { Value = 90 },
            new() { Value = 120 }, new() { Value = 150 }, new() { Value = 171 },
            new() { Value = 210 }, new() { Value = 240 }, new() { Value = 270 },
            new() { Value = 300 }
        };
    }

    /// <summary>静态压力点：勾选 + 压力值</summary>
    public class BpaPressurePoint
    {
        /// <summary>是否勾选（勾选的点才参与测试）</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>压力值（单位跟随仪器压力单位设置）</summary>
        public double Value { get; set; }
    }

    /// <summary>
    /// 动态血压测试参数。方法 = Default 用 BPCommon 六参数；
    /// 方法 = Customized 用 BPEnvelopeCustomized 包络曲线参数（此时 Default 参数不参与）。
    /// </summary>
    public class BpaDynamicBpParams
    {
        /// <summary>方法：Default / Customized</summary>
        public string Method { get; set; } = "Default";

        /// <summary>类型：Adult / Neonatal（官方上位机动态血压页的类型下拉）</summary>
        public string Category { get; set; } = "Adult";

        /// <summary>血压默认值预设（Default 方法下拉选中项，格式“收缩压/舒张压(平均动脉压)”，列表随类型变化），选中后同步填充收缩压/舒张压</summary>
        public string DefaultBpPreset { get; set; } = "120/80(93)";

        // ===== Default 方法参数（BPCommon） =====

        /// <summary>收缩压（mmHg 25~300 / kPa 3.3~39.9，单位跟随仪器设置）</summary>
        public double Systolic { get; set; } = 120;

        /// <summary>舒张压（mmHg 10~250 / kPa 1.3~33.3）</summary>
        public double Diastolic { get; set; } = 80;

        /// <summary>心率（30~300 BPM）</summary>
        public int HeartRate { get; set; } = 60;

        /// <summary>脉搏量（0.1~2.0 ml；心率 >150 时 0.1~1.0 ml）</summary>
        public double PulseVolume { get; set; } = 0.5;

        /// <summary>收缩压偏移（mmHg -20~20 / kPa -2.6~2.6）</summary>
        public double EnvelopSystolic { get; set; }

        /// <summary>舒张压偏移（mmHg -20~20 / kPa -2.6~2.6）</summary>
        public double EnvelopDiastolic { get; set; }

        // ===== Customized 方法包络参数（BPEnvelopeCustomized） =====
        // x 轴八点：SettingPoint2 > StartPressure > Systolic > MeanArterialPressure2 > MeanArterialPressure1 > Diastolic > EndPressure > SettingPoint1
        // y 轴五点百分比 0~100；默认值取自鲸扬官方 Python 示例

        /// <summary>包络点 2（x 轴，mmHg 0~299）</summary>
        public double SettingPoint2 { get; set; } = 156;

        /// <summary>起始压力（x 轴，mmHg 0~299）</summary>
        public double StartPressure { get; set; } = 136;

        /// <summary>平均动脉压 2（x 轴，mmHg 0~299）</summary>
        public double MeanArterialPressure2 { get; set; } = 97;

        /// <summary>平均动脉压 1（x 轴，mmHg 0~299）</summary>
        public double MeanArterialPressure1 { get; set; } = 93;

        /// <summary>结束压力（x 轴，mmHg 0~299）</summary>
        public double EndPressure { get; set; } = 71;

        /// <summary>包络点 1（x 轴，mmHg 0~299）</summary>
        public double SettingPoint1 { get; set; } = 51;

        /// <summary>收缩压比率（y 轴，0~100%）</summary>
        public int SystolicRate { get; set; } = 53;

        /// <summary>平均动脉压比率 1（y 轴，0~100%）</summary>
        public int MeanAriterialRate1 { get; set; } = 97;

        /// <summary>平均动脉压比率 2（y 轴，0~100%）</summary>
        public int MeanAriterialRate2 { get; set; } = 100;

        /// <summary>舒张压比率（y 轴，0~100%）</summary>
        public int DiastolicRate { get; set; } = 45;

        /// <summary>偏移比率（y 轴，0~100%）</summary>
        public int OffsetRate { get; set; } = 15;
    }

    /// <summary>超压测试参数（BPARunOverpressureTest，BP-008 过压保护用：默认 350/300/320）</summary>
    public class BpaOverpressureParams
    {
        /// <summary>BPA 充气：true=BPA700 自行充气，false=外部设备充气</summary>
        public bool BpaInflation { get; set; } = true;

        /// <summary>目标压力（单位跟随仪器压力单位设置）</summary>
        public double TargetPressure { get; set; } = 350;

        /// <summary>阈值 1</summary>
        public double Threshold1 { get; set; } = 300;

        /// <summary>阈值 2（超过即触发快速放气）</summary>
        public double Threshold2 { get; set; } = 320;
    }

    /// <summary>
    /// 压力控制功能测试（M04 BP-006~008）过程参数。三项均以产品串口回读压力判定产品行为：
    /// BP-006 匀速放气控制 = 产品测量周期匀速放气段算平均速率；
    /// BP-007 快速放气控制 = 产品充气到触发压力发停止后回读归零耗时；
    /// BP-008 过压保护 = 充气 300→320 期间回读骤降点（触发压力，阈值在 Overpressure 节点）。
    /// 规格上下限不在此处（2~6mmHg/s、≤10s、300~320mmHg 由 Spec 单一来源判定）。
    /// </summary>
    public class BpaPressureControlParams
    {
        // ===== BP-006 匀速放气控制 =====

        /// <summary>速率计算区间上限（mmHg）：只取峰值后区间内回读点计算平均速率，避开起点波动</summary>
        public double BP006RateSegmentHigh { get; set; } = 130;

        /// <summary>速率计算区间下限（mmHg）：避开接近 0 的噪声</summary>
        public double BP006RateSegmentLow { get; set; } = 30;

        /// <summary>匀速放气采样结束压力（mmHg）：产品回读降到该值以下不再纳入速率样本</summary>
        public double BP006DeflateEndPressure { get; set; } = 20;

        /// <summary>匀速放气最长等待（秒）：产品回读未按时降到位即停止采样（兜底）</summary>
        public int BP006MaxDeflateSeconds { get; set; } = 40;

        // ===== BP-007 快速放气控制 =====

        /// <summary>停止触发压力（mmHg）：产品充气回读达到该压力后发停止测量（0x7D）触发产品快速放气</summary>
        public double BP007StopTriggerPressure { get; set; } = 150;

        /// <summary>充气等待超时（秒）：产品回读超时未达到停止触发压力判 FAIL</summary>
        public int BP007InflateWaitSeconds { get; set; } = 45;

        /// <summary>归零判定压力（mmHg）：产品回读降到该值以下视为已归零</summary>
        public double BP007ZeroPressure { get; set; } = 5;

        /// <summary>归零最长等待（秒）：超时按当前耗时上报（规格 ≤10s 判定在 Spec）</summary>
        public int BP007ZeroTimeoutSeconds { get; set; } = 15;
    }

    /// <summary>压力采样点（波形数据：相对开始时刻的秒数 + 压力值，单位跟随仪器设置）</summary>
    /// <param name="TimeSeconds">相对采样开始的秒数</param>
    /// <param name="Pressure">压力值（单位跟随仪器压力单位设置）</param>
    /// <param name="Segment">测试点段号（多点扫描项按测试点序号区分颜色；单次输出项为 0）</param>
    public readonly record struct PressureSamplePoint(double TimeSeconds, double Pressure, int Segment = 0);

    /// <summary>
    /// BPA700 波形显示配置（按测试项名存放），随 instruments.json 持久化。
    /// Y 轴固定 mmHg；X 轴模式与时间刻度可按项自定义。
    /// </summary>
    public class BpaWaveformDisplayConfig
    {
        /// <summary>按测试项名索引的波形显示配置（如 "BP-001充气时间"）</summary>
        public Dictionary<string, BpaWaveformItemConfig> ItemConfigs { get; set; } = new();

        /// <summary>获取指定测试项的波形配置；未配置时返回默认值</summary>
        public BpaWaveformItemConfig GetItemConfig(string itemName)
        {
            if (ItemConfigs.TryGetValue(itemName, out var cfg)) return cfg;
            return new BpaWaveformItemConfig();
        }
    }

    /// <summary>单个血压测试项的波形显示配置</summary>
    public class BpaWaveformItemConfig
    {
        /// <summary>X 轴模式：Time = 时间（秒），SampleIndex = 采样点序号</summary>
        public string XAxisMode { get; set; } = "Time";

        /// <summary>时间刻度（像素/秒），仅 XAxisMode=Time 时生效；≤0 使用默认 24</summary>
        public double TimeScale { get; set; } = 24;
    }
}
