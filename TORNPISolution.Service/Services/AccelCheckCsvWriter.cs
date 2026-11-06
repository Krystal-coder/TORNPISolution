using System.Text;
using TORNPISolution.Data;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 加速度充电检查的采样落盘：一次保存一份，切姿态标签只另起一段不另开文件，每段末尾自动跟平均/最大/最小。
    /// 逐行 flush 的 CSV 是逐帧原始记录（中转用），停止保存后由服务层从它另出 PDF 与标红 xlsx，标红版落定即删 CSV。
    /// CSV 的序号是本段已写行数自己数的（与界面序号各算各的：暂停保存期间界面照常出序号，这里不占号）。
    /// </summary>
    public sealed class AccelCheckCsvWriter : IDisposable
    {
        private static readonly string[] Header =
        {
            "序号", "时间", "设置角度", "X(mg)", "Y(mg)", "Z(mg)", "Roll(°)", "Pitch(°)",
            "合矢量(g)", "比对角(°)", "标签角度(°)", "偏差(°)", "超判定"
        };

        /// <summary>参与段统计的数值列：与 Header 里的列号一一对应</summary>
        private static readonly int[] StatColumns = { 3, 4, 5, 6, 7, 8, 11 };

        private readonly ILogService _log = new LogService(nameof(AccelCheckCsvWriter));

        private readonly double[] _sum = new double[StatColumns.Length];
        private readonly double[] _min = new double[StatColumns.Length];
        private readonly double[] _max = new double[StatColumns.Length];
        private readonly int[] _count = new int[StatColumns.Length];

        private StreamWriter? _writer;
        private int _blockIndex;
        private int _blockRows;
        private string _blockTag = string.Empty;
        private DateTime _blockStart;

        /// <summary>当前正在写的文件（未保存时为 null）</summary>
        public string? CurrentFile { get; private set; }

        public bool IsSaving => _writer != null;

        /// <summary>开一份新的会话文件（已开着则先收尾），返回文件全路径</summary>
        public string Start(string angleTag)
        {
            Close();

            Directory.CreateDirectory(TestDataPaths.AccelCheckDirectory);
            var path = Path.Combine(TestDataPaths.AccelCheckDirectory,
                $"accel_{DateTime.Now:yyyyMMdd_HHmmss_fff}.csv");

            _writer = new StreamWriter(path, false, new UTF8Encoding(true));
            CurrentFile = path;
            _blockIndex = 0;

            BeginBlock(angleTag);
            _log.Info($"加速度充电检查开始保存：{path}");
            return path;
        }

        /// <summary>另起一段：先把上一段的平均/最大/最小结在段尾，再写本段段头与列头</summary>
        public void BeginBlock(string angleTag)
        {
            if (_writer == null) return;

            WriteBlockStats();

            _blockIndex++;
            _blockTag = angleTag ?? string.Empty;
            _blockStart = DateTime.Now;
            _blockRows = 0;
            Array.Clear(_sum, 0, _sum.Length);
            Array.Clear(_count, 0, _count.Length);
            for (var i = 0; i < _min.Length; i++)
            {
                _min[i] = double.MaxValue;
                _max[i] = double.MinValue;
            }

            _writer.WriteLine(Escape($"# 段 {_blockIndex}：{_blockTag}（起始 {_blockStart:yyyy-MM-dd HH:mm:ss}）"));
            _writer.WriteLine(string.Join(',', Header));
            _writer.Flush();
        }

        /// <summary>写一行，并把这一行的数值列并进本段统计；序号数的是本段已写几行，跟界面序号互不相干</summary>
        public void Write(AccelCheckRecord record)
        {
            if (_writer == null) return;

            var s = record.Sample;
            _writer.WriteLine(string.Join(',',
                _blockRows + 1,
                s.ReceivedAt.ToString("HH:mm:ss.fff"),
                Escape(record.AngleTag),
                s.X, s.Y, s.Z,
                F(s.RollDegrees), F(s.PitchDegrees),
                F(s.ResultantG),
                F(record.CompareAngle),
                record.TagAngle.HasValue ? F(record.TagAngle.Value) : string.Empty,
                record.Deviation.HasValue ? F(record.Deviation.Value) : string.Empty,
                record.Exceeded ? "超限" : (record.TagAngle.HasValue ? "合格" : "未比对")));
            _writer.Flush();

            Accumulate(s.X, s.Y, s.Z, s.RollDegrees, s.PitchDegrees, s.ResultantG, record.Deviation);
            _blockRows++;
        }

        public void Flush()
        {
            try { _writer?.Flush(); }
            catch (Exception ex) { _log.Warn($"加速度数据刷新到磁盘失败：{ex.Message}"); }
        }

        /// <summary>收尾当前文件：最后一段的统计也要结出来，中转 CSV 交给服务层另出报告（没在保存时什么都不做）</summary>
        /// <returns>这一轮的中转 CSV 全路径；没在保存时为 null</returns>
        public string? Close()
        {
            if (_writer == null) return null;

            // _writer 与 CurrentFile 同起同落，能走到这里路径必在
            var path = CurrentFile!;
            var blocks = _blockIndex;
            try
            {
                WriteBlockStats();
                _writer.Flush();
                _writer.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warn($"加速度数据文件收尾失败（{path}）：{ex.Message}");
            }

            _writer = null;
            CurrentFile = null;

            _log.Info($"加速度数据已收尾：{path}（{_blockTag} 末段 {_blockRows} 行，共 {blocks} 段，报告另出）");
            return path;
        }

        public void Dispose() => Close();

        private void Accumulate(double x, double y, double z, double roll, double pitch, double resultant, double? deviation)
        {
            Span<double> values = stackalloc double[StatColumns.Length];
            values[0] = x;
            values[1] = y;
            values[2] = z;
            values[3] = roll;
            values[4] = pitch;
            values[5] = resultant;
            // 没示教名义角的行比不出偏差，这类行不进偏差统计，否则平均被空值拉走
            values[6] = deviation ?? double.NaN;

            for (var i = 0; i < StatColumns.Length; i++)
            {
                var v = values[i];
                if (double.IsNaN(v)) continue;
                _sum[i] += v;
                _count[i]++;
                if (v < _min[i]) _min[i] = v;
                if (v > _max[i]) _max[i] = v;
            }
        }

        private void WriteBlockStats()
        {
            if (_writer == null || _blockRows == 0) return;

            // 段头写在前，统计落在段尾，Excel 里一眼能看出这几行是本段的结果
            _writer.WriteLine(StatRow("平均"));
            _writer.WriteLine(StatRow("最大"));
            _writer.WriteLine(StatRow("最小"));
            _writer.WriteLine();
            _writer.Flush();
        }

        private string StatRow(string label)
        {
            var cells = new string[Header.Length];
            Array.Fill(cells, string.Empty);
            cells[0] = label;
            cells[2] = Escape(_blockTag);

            for (var i = 0; i < StatColumns.Length; i++)
            {
                if (_count[i] == 0) continue;
                var value = label switch
                {
                    "平均" => _sum[i] / _count[i],
                    "最大" => _max[i],
                    _ => _min[i],
                };
                cells[StatColumns[i]] = F(value);
            }

            return string.Join(',', cells);
        }

        private static string F(double value) => value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>标签文本可能带逗号，带时按 CSV 规则加引号</summary>
        private static string Escape(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Contains(',') || text.Contains('"')
                ? "\"" + text.Replace("\"", "\"\"") + "\""
                : text;
        }
    }
}
