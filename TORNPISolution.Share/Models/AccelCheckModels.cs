namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 产品 0x60 主动推送的一帧加速度采样；角度算法与嵌入式 s_accel_sample 一致（单位 °，加速度单位 g）。
    /// 原始 X/Y/Z 为 mg（int16 小端），姿态角用 atan2/atan 现算，与产品端上报口径对齐。
    /// </summary>
    public sealed class AccelSample
    {
        /// <summary>产品侧累计序号（uint32，跨姿态标签不重置，丢帧靠它的跳变发现）</summary>
        public uint DeviceSeq { get; init; }

        /// <summary>X 轴加速度（mg）</summary>
        public short X { get; init; }

        /// <summary>Y 轴加速度（mg）</summary>
        public short Y { get; init; }

        /// <summary>Z 轴加速度（mg）</summary>
        public short Z { get; init; }

        /// <summary>上位机收到该帧的时刻（实测帧间隔由它算）</summary>
        public DateTime ReceivedAt { get; init; }

        public double Xg => X / 1000.0;
        public double Yg => Y / 1000.0;
        public double Zg => Z / 1000.0;

        /// <summary>绕 X 轴转角（°）：atan2(Y, Z)</summary>
        public double RollDegrees => Math.Atan2(Yg, Zg) * Rad2Deg;

        /// <summary>绕 Y 轴转角（°）：atan(-X / √(Y²+Z²))</summary>
        public double PitchDegrees => Math.Atan(-Xg / Math.Sqrt(Yg * Yg + Zg * Zg)) * Rad2Deg;

        /// <summary>合矢量模（g）：静态应≈1，明显偏离说明传感器或装配异常</summary>
        public double ResultantG => Math.Sqrt(Xg * Xg + Yg * Yg + Zg * Zg);

        private const double Rad2Deg = 57.29578;
    }

    /// <summary>
    /// 本档（姿态标签）示教出来的名义角：Roll/Pitch 各一个，null 表示该轴没示教。
    /// </summary>
    public sealed record AccelNominal(double? Roll, double? Pitch)
    {
        /// <summary>至少示教了一个轴才算"本档已置零"</summary>
        public bool HasAny => Roll != null || Pitch != null;
    }

    /// <summary>
    /// 加速度姿态角与名义角的比对：判哪一轴由判定角选择决定（Roll / Pitch / Roll&amp;Pitch）
    /// 名义角优先取本档示教值；没示教时用标签文本里那个角——两轴都判时该角按 Roll 用、Pitch 按 0° 判
    /// 偏差只比倾角大小，不比方向：实测 −41 对设置 45 记 4°
    /// </summary>
    public static class AccelAngleJudge
    {
        public const string AxisRoll = "Roll";
        public const string AxisPitch = "Pitch";
        public const string AxisBoth = "Roll & Pitch";

        public static readonly string[] Axes = { AxisRoll, AxisPitch, AxisBoth };

        /// <summary>
        /// 从姿态标签里捞名义角度：取文本中出现的第一个数（含负号与小数），"平放0°"、"−90 度侧立"、"45" 都能读。
        /// 读不出来返回 null，表示这条标签无法比对（不标红，界面提示）。
        /// </summary>
        public static double? ParseTagAngle(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var match = System.Text.RegularExpressions.Regex.Match(
                tag.Replace("−", "-"), @"-?\d+(\.\d+)?");
            return match.Success ? double.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        /// <summary>
        /// 把一条采样连同比对结果算成完整记录：表格与 CSV 都从这里出，两边口径不会分叉。
        /// 单轴口径下本条就用那一轴；两轴口径下取偏差大的那一轴作本条口径（原始 Roll/Pitch 都在 CSV 里，事后照样能分开捞）。
        /// </summary>
        public static AccelCheckRecord Evaluate(int seq, string angleTag, AccelSample sample,
            string axis, double toleranceDegrees, AccelNominal? taught = null)
        {
            if (axis != AxisBoth)
            {
                var pitch = axis == AxisPitch;
                var nominal = (pitch ? taught?.Pitch : taught?.Roll) ?? ParseTagAngle(angleTag);
                var measured = pitch ? sample.PitchDegrees : sample.RollDegrees;
                var deviation = nominal == null ? (double?)null : Deviation(measured, nominal.Value);

                return new AccelCheckRecord(seq, angleTag, sample, axis, measured, nominal, deviation,
                    deviation != null && deviation.Value > toleranceDegrees);
            }

            if (taught?.HasAny == true)
                return Weakest(seq, angleTag, sample, taught.Roll, taught.Pitch, toleranceDegrees);

            var tagAngle = ParseTagAngle(angleTag);
            if (tagAngle == null)
                return new AccelCheckRecord(seq, angleTag, sample, AxisRoll, sample.RollDegrees,
                    null, null, false);

            return Weakest(seq, angleTag, sample, tagAngle, 0, toleranceDegrees);
        }

        /// <summary>两轴各自比、取偏差大的那一轴作本条口径；某一轴没有名义角就只按另一轴判</summary>
        private static AccelCheckRecord Weakest(int seq, string angleTag, AccelSample sample,
            double? rollNominal, double? pitchNominal, double toleranceDegrees)
        {
            var rollDev = rollNominal == null ? (double?)null : Deviation(sample.RollDegrees, rollNominal.Value);
            var pitchDev = pitchNominal == null ? (double?)null : Deviation(sample.PitchDegrees, pitchNominal.Value);
            var usePitch = pitchDev != null && (rollDev == null || pitchDev > rollDev);
            var deviation = usePitch ? pitchDev : rollDev;

            return new AccelCheckRecord(seq, angleTag, sample, usePitch ? AxisPitch : AxisRoll,
                usePitch ? sample.PitchDegrees : sample.RollDegrees,
                usePitch ? pitchNominal : rollNominal, deviation,
                deviation != null && deviation.Value > toleranceDegrees);
        }

        /// <summary>倾角大小之差：翻上翻下算同一个姿态，符号不进偏差，实测值本身照原样显示与落盘</summary>
        private static double Deviation(double measured, double nominal)
            => Math.Abs(Math.Abs(measured) - Math.Abs(nominal));
    }

    /// <summary>
    /// 加速度充电检查的一条记录
    /// 本地序号 + 采集时的姿态标签 + 采样 + 与标签名义角的比对结果
    /// 判定角与容差是采集时的设置
    /// </summary>
    public sealed record AccelCheckRecord(int Seq, string AngleTag, AccelSample Sample, string CompareAxis,
        double CompareAngle, double? TagAngle, double? Deviation, bool Exceeded)
    {
        /// <summary>合矢量（g）：静态应≈1，明显偏离是传感器或装配问题，只作提示不参与判定</summary>
        public double ResultantG => Sample.ResultantG;
    }
}
