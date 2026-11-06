namespace TORNPISolution.Share.Models
{
    /// <summary>Roll 折线上的一个采样点：收帧时刻 + 该帧的 Roll 倾角（°）</summary>
    public readonly record struct AccelRollPoint(DateTime At, double Roll);

    /// <summary>一次取数的结果：点连同取数时的抽稀次数与总点数一起回来；Full 为真表示这次给的是整段，界面侧要先清空再画</summary>
    public sealed record AccelRollChunk(AccelRollPoint[] Points, int Generation, int Count, bool Full);

    /// <summary>
    /// 一段 Roll 曲线：同一姿态标签的一次连续区间，切标签即断段。
    /// 读线程写、界面线程读，内部自带锁；点数过抽稀线就按 4 点取 min/max 折半，抽稀一次 Generation 加一。
    /// 平均/最大/最小按抽稀前的全部采样算，报告用的统计量不受抽稀影响。
    /// </summary>
    public sealed class AccelRollSegment
    {
        /// <summary>抽稀触发线（2×回落目标）：攒到这个数就 4 点取 min/max 折半，折完回到 TargetPointsPerSegment 那一档</summary>
        private const int DecimateThreshold = AccelRollTrace.TargetPointsPerSegment * 2;

        private readonly object _gate = new();
        private readonly List<AccelRollPoint> _points = new();
        private double _rollSum;
        private double _rollMin = double.MaxValue;
        private double _rollMax = double.MinValue;

        internal AccelRollSegment(string tag, int id, DateTime startedAt)
        {
            Tag = tag;
            Id = id;
            StartedAt = startedAt;
            LastAt = startedAt;
        }

        /// <summary>本段所属姿态标签：同名标签的多次区间各成一段，但共用图例里那一项（同色）</summary>
        public string Tag { get; }

        /// <summary>段号（进程内递增）：界面认段不认标签，同名标签的两段不会并成一段画</summary>
        public int Id { get; }

        public DateTime StartedAt { get; private set; }

        public DateTime LastAt { get; private set; }

        /// <summary>抽稀次数（在锁外读只用于比较，读到旧值只会多画一轮，不会画错）</summary>
        public int Generation { get; private set; }

        /// <summary>本段真实帧数（抽稀前）</summary>
        public int TotalSamples { get; private set; }

        public double AverageRoll
        {
            get { lock (_gate) return TotalSamples == 0 ? double.NaN : _rollSum / TotalSamples; }
        }

        public double MinRoll
        {
            get { lock (_gate) return TotalSamples == 0 ? double.NaN : _rollMin; }
        }

        public double MaxRoll
        {
            get { lock (_gate) return TotalSamples == 0 ? double.NaN : _rollMax; }
        }

        internal void Append(DateTime at, double roll)
        {
            lock (_gate)
            {
                _points.Add(new AccelRollPoint(at, roll));
                _rollSum += roll;
                if (roll < _rollMin) _rollMin = roll;
                if (roll > _rollMax) _rollMax = roll;
                TotalSamples++;
                LastAt = at;

                if (_points.Count > DecimateThreshold) Decimate();
            }
        }

        /// <summary>整段点快照（刚建好一条线时用它）</summary>
        public AccelRollChunk Snapshot()
        {
            lock (_gate) return new AccelRollChunk(_points.ToArray(), Generation, _points.Count, true);
        }

        /// <summary>
        /// 取 cursor 之后的新点。游标与抽稀次数一起进来比对：对不上（刚抽稀过、下标已经错位）就整段回吐，
        /// 界面侧照 Full 先清空——判断和取数在同一把锁里做，才不会拼出重复的一批。
        /// </summary>
        public AccelRollChunk ReadFrom(int cursor, int knownGeneration)
        {
            lock (_gate)
            {
                var full = knownGeneration != Generation || cursor < 0 || cursor > _points.Count;
                var from = full ? 0 : cursor;
                var slice = _points.GetRange(from, _points.Count - from).ToArray();
                return new AccelRollChunk(slice, Generation, _points.Count, full);
            }
        }

        /// <summary>4 个连续点折成 min/max 两个：波峰波谷留住，点数减半</summary>
        private void Decimate()
        {
            var folded = new List<AccelRollPoint>(_points.Count / 2 + 2);
            for (var i = 0; i < _points.Count; i += 4)
            {
                var last = Math.Min(i + 4, _points.Count);
                var iMin = i;
                var iMax = i;
                for (var j = i + 1; j < last; j++)
                {
                    if (_points[j].Roll < _points[iMin].Roll) iMin = j;
                    if (_points[j].Roll > _points[iMax].Roll) iMax = j;
                }

                // 按时刻先后给，否则同一桶里先波谷后波峰会把线往回画
                if (iMax < iMin) (iMin, iMax) = (iMax, iMin);
                folded.Add(_points[iMin]);
                if (iMax != iMin) folded.Add(_points[iMax]);
            }

            _points.Clear();
            _points.AddRange(folded);
            Generation++;
        }
    }

    /// <summary>
    /// Roll 曲线总缓冲：按姿态标签分段，切一次标签断一段，同名标签的多次区间各自成段、共用一个图例项。
    /// 只在内存里留抽稀后的点，界面展开曲线时按批补画；换产品重连即清空。
    /// </summary>
    public sealed class AccelRollTrace
    {
        /// <summary>每段折半后回到的点数（攒到它的 2 倍就折一次）：按 1 s 间隔约 13 分钟折一次，一屏画得动也不丢波峰波谷</summary>
        public const int TargetPointsPerSegment = 800;

        private readonly object _gate = new();
        private readonly List<AccelRollSegment> _segments = new();

        /// <summary>会话起点（第一段第一帧的时刻）：横轴按它铺秒</summary>
        public DateTime SessionStart { get; private set; } = DateTime.Now;

        /// <summary>本段是否还开着：标签没变就接着同一段</summary>
        private string _currentTag = string.Empty;

        /// <summary>按标签追加一帧：标签与上一帧不同就另起一段</summary>
        public void Append(string tag, DateTime at, double roll)
        {
            if (string.IsNullOrEmpty(tag)) return;

            lock (_gate)
            {
                // 会话起点认第一帧：重连后横轴仍从 0:00 起算，而不是认重连那一刻
                if (_segments.Count == 0) SessionStart = at;

                if (_segments.Count == 0 || _currentTag != tag)
                    _segments.Add(new AccelRollSegment(tag, _segments.Count + 1, at));

                _currentTag = tag;
                _segments[^1].Append(at, roll);
            }
        }

        /// <summary>当前所有段的句柄（界面与报告只读这份快照，不在读线程上枚举内部集合）</summary>
        public AccelRollSegment[] Segments()
        {
            lock (_gate) return _segments.ToArray();
        }

        /// <summary>重连时清空：一段曲线对应一次上电后的姿态走查，跨产品留着只会画出一条对不上文件的线</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _segments.Clear();
                _currentTag = string.Empty;
                SessionStart = DateTime.Now;
            }
        }
    }
}
