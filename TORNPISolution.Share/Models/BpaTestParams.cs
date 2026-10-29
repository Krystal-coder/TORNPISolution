namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 鲸扬 BPA700 测试参数配置（充放气 / 静态压力 / 动态血压三类测试），随 instruments.json 一起保存/加载，供测试模块执行时读取。
    /// </summary>
    public class BpaTestParams
    {
        /// <summary>静态压力参数（BPARunStaticPressure，勾选的压力点由上位机逐点循环调用）</summary>
        public BpaStaticPressureParams StaticPressure { get; set; } = new();

        /// <summary>动态血压参数（BPARunBPTest，Default/Customized 两种方法）</summary>
        public BpaDynamicBpParams DynamicBloodPressure { get; set; } = new();

        /// <summary>BP-004 血压精度/重复性参数（A06）：压力组可勾选增删，每组一个心率</summary>
        public BpaBpAccuracyParams BpAccuracy { get; set; } = new();

        /// <summary>BP-005 心率精度/重复性参数（A21）：压力一组，心率可勾选增删多个点</summary>
        public BpaHeartRateAccuracyParams BpHeartRate { get; set; } = new();

        /// <summary>超压测试参数（保留仪器过压测试）</summary>
        public BpaOverpressureParams Overpressure { get; set; } = new();

        /// <summary>充放气流程的超时保护（等待按键启动 / 整轮测量）</summary>
        public BpaCuffSpec CuffTest { get; set; } = new();

        /// <summary>压力控制功能测试（BP-006~008）过程参数</summary>
        public BpaPressureControlParams PressureControl { get; set; } = new();

        /// <summary>气路类两项（A18 放气速率 BP-007 / A22 气路泄露 BP-010）的判定区间</summary>
        public BpaAirPathParams AirPath { get; set; } = new();

        /// <summary>波形显示配置（按测试项自定义 X 轴模式与时间刻度，Y 轴固定 mmHg）</summary>
        public BpaWaveformDisplayConfig WaveformDisplay { get; set; } = new();

        /// <summary>JJG692-2010 标准检测参数（放气速率 / 气密性，均为康伟达 0x04 功能专属）</summary>
        public Jjg692Params Jjg692 { get; set; } = new();
    }

    /// <summary>
    /// JJG692-2010 标准检测参数组（仅放气速率（0x12）/ 气密性（0x13）两项使用，按康伟达协议 0x04 功能下发），判定规格值留空时只报实测值不判 PASS/FAIL。
    /// 血压重复性不在本组（即 BP-004 的"精度及重复性"，已归 A06，点位与轮数见 <see cref="BpaTestParams.BpAccuracy"/>，±2mmHg 判据按文档写死）：
    /// 协议 0x04/0x11 设备自检帧只回"完成/失败"状态位、拿不到三轮收缩/舒张数值，算不出极差。
    /// </summary>
    public class Jjg692Params
    {
        /// <summary>测量类型：0=成人 1=新生儿（协议仅 0x11 设备自检帧高 4 位定义，现模块走软件控点位路线、不下发 0x11，配置页不再展示）</summary>
        public int MeasureType { get; set; } = 0;

        /// <summary>袖带类型：0=外部袖带 1=内部成人（0x12/0x13 帧低 4 位共用）</summary>
        public int CuffType { get; set; } = 0;

        /// <summary>整项检测超时（秒）：仅 0x12/0x13 用，设备自行完成升压/循环/泄放的兜底上限</summary>
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>放气速率（听诊，0x12）参数与判定区间</summary>
        public Jjg692DeflationRateParams DeflationRate { get; set; } = new();

        /// <summary>气密性（0x13）参数与判定上限</summary>
        public Jjg692AirtightnessParams Airtightness { get; set; } = new();
    }

    /// <summary>
    /// BP-004 血压精度/重复性参数（A06）：一行一组收缩压/舒张压/心率，心率按组设置（发给模拟仪驱动脉搏波，不参与判定）。
    /// </summary>
    public class BpaBpAccuracyParams
    {
        /// <summary>压力组清单（勾选的行参与测试，顺序即执行顺序）</summary>
        public List<BpaBpPressureGroup> Groups { get; set; } = new()
        {
            new() { Systolic = 120, Diastolic = 80, HeartRate = 80 },
            new() { Systolic = 150, Diastolic = 100, HeartRate = 100 },
            new() { Systolic = 200, Diastolic = 150, HeartRate = 120 },
        };

        /// <summary>每个压力组重复的轮数（最少 1 轮；1 轮时组内没有极差可算，执行端与目录都不出重复性项）</summary>
        public int Repeats { get; set; } = 3;

        /// <summary>每轮测完让产品休息的秒数（含换组之间与最后一轮收尾）</summary>
        public int RestSeconds { get; set; } = 60;

        /// <summary>单次完整血压测量超时（秒）：从发出开始测量指令算起，超过该时长没收到产品结果帧即结束本轮并中止本模块</summary>
        public int SingleMeasureTimeoutSeconds { get; set; } = 120;

        /// <summary>休息期间泄压回零（false=仪器保持待命、袖带残压不动）</summary>
        public bool VentDuringRest { get; set; } = true;
    }

    /// <summary>一组血压设定（一次血压模拟的收缩/舒张目标与驱动脉搏波的心率）</summary>
    public class BpaBpPressureGroup
    {
        /// <summary>是否参与测试</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>收缩压（mmHg）</summary>
        public double Systolic { get; set; } = 120;

        /// <summary>舒张压（mmHg）</summary>
        public double Diastolic { get; set; } = 80;

        /// <summary>心率（BPM，本组激励参数，不参与判定）</summary>
        public int HeartRate { get; set; } = 80;
    }

    /// <summary>
    /// BP-005 心率精度/重复性参数（A21）：压力只用一组，心率一行一个点可勾选增删。
    /// </summary>
    public class BpaHeartRateAccuracyParams
    {
        /// <summary>收缩压（mmHg，全模块只用这一组压力）</summary>
        public double Systolic { get; set; } = 120;

        /// <summary>舒张压（mmHg）</summary>
        public double Diastolic { get; set; } = 80;

        /// <summary>心率点清单（勾选的点参与测试，顺序即执行顺序）</summary>
        public List<BpaHeartRatePoint> HeartRates { get; set; } = new()
        {
            new() { Value = 60 }, new() { Value = 80 }, new() { Value = 100 }, new() { Value = 120 },
        };

        /// <summary>每个心率点重复的轮数（最少 1 轮；平均值项 1 轮即取该轮）</summary>
        public int Repeats { get; set; } = 3;

        /// <summary>每轮测完让产品休息的秒数（含换点之间与最后一轮收尾）</summary>
        public int RestSeconds { get; set; } = 60;

        /// <summary>单次完整血压测量超时（秒）：从发出开始测量指令算起，超过该时长没收到产品结果帧即结束本轮并中止本模块</summary>
        public int SingleMeasureTimeoutSeconds { get; set; } = 120;

        /// <summary>休息期间泄压回零（false=仪器保持待命、袖带残压不动）</summary>
        public bool VentDuringRest { get; set; } = true;
    }

    /// <summary>一个心率设定（BPM）</summary>
    public class BpaHeartRatePoint
    {
        /// <summary>是否参与测试</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>心率（BPM）</summary>
        public int Value { get; set; } = 80;
    }

    /// <summary>0x12 放气速率（听诊）：帧 = [04, 12, 袖带, 设定压力2, 开始气压2, 停止气压2]；
    /// 测试值 = 设备回传漏气率（mmHg/s），判定区间留空则不判。</summary>
    public class Jjg692DeflationRateParams
    {
        /// <summary>设定压力点（mmHg，帧内 u16）</summary>
        public double SetPressure { get; set; } = 200;

        /// <summary>开始气压（mmHg，计时起点）</summary>
        public double StartPressure { get; set; } = 160;

        /// <summary>停止气压（mmHg，计时终点）</summary>
        public double StopPressure { get; set; } = 60;

        /// <summary>漏气率合格下限（mmHg/s，null=不判）</summary>
        public double? MinRate { get; set; }

        /// <summary>漏气率合格上限（mmHg/s，null=不判）</summary>
        public double? MaxRate { get; set; }
    }

    /// <summary>
    /// 气路类两项的判定区间（A18 放气速率 BP-007 的加压/泄压时长、A22 气路泄露 BP-010 的高低压制气量），只走 BPA700 静态压力序列流程。
    /// 任一端留空按不判该端处理（下限空=不限下、上限空用测试要求文档暂定的兜底值），两端都留空则该项不判 PASS/FAIL、只报实测值。
    /// </summary>
    public class BpaAirPathParams
    {
        /// <summary>加压时间 t1 合格下限（s）</summary>
        public double? InflateTimeMin { get; set; }

        /// <summary>加压时间 t1 合格上限（s，留空兜底 30）</summary>
        public double? InflateTimeMax { get; set; }

        /// <summary>泄压时间 t2 合格下限（s）</summary>
        public double? DeflateTimeMin { get; set; }

        /// <summary>泄压时间 t2 合格上限（s，留空兜底 60）</summary>
        public double? DeflateTimeMax { get; set; }

        /// <summary>高压段泄气量 ΔP_H 合格下限（mmHg，留空=不限下）：终点高于起点时差值为负</summary>
        public double? HighLossMin { get; set; }

        /// <summary>高压段泄气量 ΔP_H 合格上限（mmHg，留空兜底 2）</summary>
        public double? HighLossMax { get; set; }

        /// <summary>低压段泄气量 ΔP_L 合格下限（mmHg，留空=不限下）</summary>
        public double? LowLossMin { get; set; }

        /// <summary>低压段泄气量 ΔP_L 合格上限（mmHg，留空兜底 2）</summary>
        public double? LowLossMax { get; set; }
    }

    /// <summary>0x13 气密性：帧 = [04, 13, 袖带, 设定压力2, 稳压时间1]；
    /// 测试值 = 设备回传漏气率（mmHg/s），上限留空则不判。</summary>
    public class Jjg692AirtightnessParams
    {
        /// <summary>设定压力点（mmHg，帧内 u16）</summary>
        public double SetPressure { get; set; } = 200;

        /// <summary>稳压时间（秒，帧内 1 字节）</summary>
        public int StabilizeSeconds { get; set; } = 60;

        /// <summary>漏气率合格上限（mmHg/s，null=不判）</summary>
        public double? MaxLeakRate { get; set; }
    }

    /// <summary>
    /// 充放气流程的超时保护配置（需测试员按产品物理键启动的流程使用，如 BP-006）：超时后自动停止本轮并判 FAIL，避免产品异常时测试一直充气不停止。
    /// </summary>
    public class BpaCuffSpec
    {
        /// <summary>等待按键启动超时（秒）：仪器进入待测后，等待产品物理按键的最长时间</summary>
        public int WaitStartTimeoutSeconds { get; set; } = 30;

        /// <summary>整轮测量超时（秒）：启动后充气→稳压→放气全程的兜底上限，超过即停止测试</summary>
        public int TotalTimeoutSeconds { get; set; } = 60;

        /// <summary>快速放气时间规格上限（秒）：BP-002 改按设备端状态（0x74）判定后暂无执行端消费，保留字段兼容已存配置</summary>
        public double MaxDeflationSeconds { get; set; } = 10;
    }

    /// <summary>
    /// 静态压力测试参数：SDK 一次调用只支持 1 个压力点，多点测试由上位机按勾选的点循环调用，最多 10 个。
    /// </summary>
    public class BpaStaticPressureParams
    {
        /// <summary>BPA 充气：true=BPA700 自行充气，false=外部设备充气</summary>
        public bool BpaInflation { get; set; } = true;

        /// <summary>量测时间（每个压力点的稳压保持秒数，0~999；仅 BPA700 序列有效，康伟达按收敛判据 2.5s 稳压窗取均值、不额外保持）。
        /// 保持窗只需盖住 Stable 事件里发起的 0x76 回读（约 0.2s）：2s 已在台架验证过（100/200mmHg 两点产品上报值正常回读），配大了只是多等仪器补压</summary>
        public int MeasureSeconds { get; set; } = 2;

        /// <summary>点间泄压：每个压力点测完先等归零并回读确认再升下一点（false=不等不确认。两台仪器点间都保不住压：BPA700 无泄压命令、残压自行回落，康伟达下一点开头的清场仪式会泄残压）</summary>
        public bool VentBetweenPoints { get; set; } = false;

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
    /// 动态血压测试参数：方法 = Default 用 BPCommon 六参数；方法 = Customized 用 BPEnvelopeCustomized 包络曲线参数（此时 Default 参数不参与）。
    /// </summary>
    public class BpaDynamicBpParams
    {
        /// <summary>方法：Default / Customized</summary>
        public string Method { get; set; } = "Default";

        /// <summary>类型：Adult / Neonatal（动态血压页的类型下拉）</summary>
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

    /// <summary>超压测试参数（保留仪器过压测试）</summary>
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
    /// 压力控制功能测试（A04 BP-006~009）过程参数：四项均以产品串口回读压力（0x76）判定产品行为，充气一律由产品气泵完成（模拟仪只当手臂提供脉搏波，不参与加压）。
    /// BP-006 最高充气压力 = 正常自动测量全程回读峰值；
    /// BP-007 匀速放气控制 = 与 BP-006 同一轮测量的匀速放气段平均速率；
    /// BP-008 快速放气功能 = 充气到触发压力发 0x79 status=1 停止后，峰值降至归零判定压力的耗时；
    /// BP-009 过压保护 = 0x75 充气，首次超响应起算压力到 0x80 过压状态置位的间隔，以及置位持续时长。
    /// 合格线（≤300mmHg、2~6mmHg/s、≤10s、≤15s、≤3s）不在此处，由 Spec 单一来源判定。
    /// </summary>
    public class BpaPressureControlParams
    {
        // ===== 激励档（BP-006/007/008 三项共用：模拟仪扮演的“手臂”，独立于 BP-004 压力组） =====

        /// <summary>激励收缩压（mmHg）：设高才能驱动产品气泵充到 260mmHg 以上</summary>
        public double InflateSystolic { get; set; } = 220;

        /// <summary>激励舒张压（mmHg）</summary>
        public double InflateDiastolic { get; set; } = 180;

        /// <summary>激励心率（BPM）</summary>
        public double InflateHeartRate { get; set; } = 80;

        /// <summary>激励脉搏量（mL，0=用模拟仪默认值）</summary>
        public double InflatePulseVolume { get; set; } = 8;

        // ===== BP-007 匀速放气控制 =====

        /// <summary>速率计算区间上限（mmHg）：只取峰值后区间内回读点计算平均速率，避开起点波动</summary>
        public double BP007RateSegmentHigh { get; set; } = 130;

        /// <summary>速率计算区间下限（mmHg）：避开接近 0 的噪声</summary>
        public double BP007RateSegmentLow { get; set; } = 30;

        /// <summary>匀速放气采样结束压力（mmHg）：血压模拟曲线结束后回读降到该值即认为放气采完、提前收尾</summary>
        public double BP007DeflateEndPressure { get; set; } = 20;

        /// <summary>匀速放气最长等待（秒）：曲线结束后最多再等这么久，到点就不再等回读降到位（兜底）</summary>
        public int BP007MaxDeflateSeconds { get; set; } = 40;

        /// <summary>分段速率段宽（mmHg）：匀速段内按该宽度切段算各段速率，只展示不判定（文档未给“稳定”的量化上限）</summary>
        public double BP007RateSliceMmHg { get; set; } = 20;

        // ===== BP-008 快速放气功能 =====

        /// <summary>停止触发压力（mmHg）：产品充气回读达到该压力即发 0x79 status=1 关闭测量，触发产品快速放气（测试要求文档值 260）</summary>
        public double BP008StopTriggerPressure { get; set; } = 260;

        /// <summary>充气等待超时（秒）：产品回读超时未达到停止触发压力且未自行转入放气，判 FAIL</summary>
        public int BP008InflateWaitSeconds { get; set; } = 45;

        /// <summary>提前退出兜底（mmHg）：回读从峰值下降该值说明产品已自行放气，不等触发压力直接计时（产品可能充不到 260）</summary>
        public double BP008PeakFallEscapeMmHg { get; set; } = 10;

        /// <summary>放气终点压力（mmHg）：回读降到该值以下视为放气完成（测试要求文档值 15）</summary>
        public double BP008EndPressure { get; set; } = 15;

        /// <summary>放气最长等待（秒）：窗内未降至终点压力时，测试值给到超时点（必超规格）并在产品列写明是超时不是实测耗时</summary>
        public int BP008ZeroTimeoutSeconds { get; set; } = 15;

        // ===== BP-009 过压保护 =====

        /// <summary>0x75 目标压力（mmHg）：当前默认 260 低于判据门限 330，两条判据均无法评估、必然判 FAIL；上机确认 0x75 能动作后需调到明显高于 330（建议 350）才能出结论</summary>
        public double BP009InflateTargetPressure { get; set; } = 260;

        /// <summary>0x75 最长充气时间（秒）：到点产品底层自行放气，也是本项唯一的停气兜底，不宜设大</summary>
        public int BP009InflateTimeoutSeconds { get; set; } = 30;

        /// <summary>响应时限起算压力（mmHg）：首次超该压力的时刻为判据“15s 内启动泄压”的起点（文档值 300）</summary>
        public double BP009ResponseStartPressure { get; set; } = 300;

        /// <summary>超压判定压力（mmHg）：回读是否到过该压力决定“超压持续≤3s”能否评估（文档值 330）</summary>
        public double BP009SustainedPressure { get; set; } = 330;
    }

    /// <summary>压力采样点（波形数据：相对开始时刻的秒数 + 压力值，单位跟随仪器设置）</summary>
    /// <param name="TimeSeconds">相对采样开始的秒数</param>
    /// <param name="Pressure">压力值（单位跟随仪器压力单位设置）</param>
    /// <param name="Segment">测试点段号（多点扫描项按测试点序号区分颜色；单次输出项为 0）</param>
    /// <param name="Series">曲线编号（0=仪器输出曲线；1=产品回传曲线，仅 A10 双波形展示用）</param>
    public readonly record struct PressureSamplePoint(double TimeSeconds, double Pressure, int Segment = 0, int Series = 0);

    /// <summary>
    /// BPA700 波形显示配置（按测试项名存放），随 instruments.json 持久化；Y 轴固定 mmHg，X 轴模式与时间刻度可按项自定义。
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
