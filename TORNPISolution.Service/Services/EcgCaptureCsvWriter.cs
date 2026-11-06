using System.Text;
using TORNPISolution.Data;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// ECG 波形采集的落盘：滚动会话 CSV（内存满 60 分钟切最旧 10 分钟块追加）与手动导出 CSV 都走这里。
    /// 采集段切换时 <see cref="FinishSegment"/> 只关当前会话文件，下一块另开一份；导出一次一份不追加。
    /// 时间列是本段起算的秒（采样序号 ÷ 采样率），ADC 与电压两列同轴，处理后不用二次对齐。
    /// </summary>
    public sealed class EcgCaptureCsvWriter
    {
        private static readonly string[] Header = { "时间(s)", "ADC原始值", "电压(mV)" };

        private readonly ILogService _log = new LogService(nameof(EcgCaptureCsvWriter));
        private readonly object _lock = new();

        private StreamWriter? _writer;

        /// <summary>当前滚动会话文件（本段还没落过时为 null）</summary>
        public string? SessionFile { get; private set; }

        /// <summary>把切出的一块追加到滚动会话 CSV；返回本块落盘的文件路径</summary>
        public string AppendChunk(IReadOnlyList<EcgWavePoint> chunk, long firstSampleIndex, int sampleRateHz)
        {
            lock (_lock)
            {
                var path = SessionFile;
                if (_writer == null)
                {
                    path = Path.Combine(TestDataPaths.EcgCaptureDirectory,
                        $"ECG波形_滚动_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    SessionFile = path;
                    Directory.CreateDirectory(TestDataPaths.EcgCaptureDirectory);
                    _writer = new StreamWriter(path, false, Encoding.UTF8);
                    _writer.WriteLine(string.Join(',', Header));
                }

                WriteRows(chunk, firstSampleIndex, sampleRateHz);
                _writer.Flush();
                return path!;
            }
        }

        /// <summary>结束本采集段：会话文件写完关闭，下一块数据另开一份</summary>
        public void FinishSegment()
        {
            lock (_lock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        /// <summary>把内存里的一段一次性导出成新文件；返回全路径，没有数据时返回 null</summary>
        public string? Export(IReadOnlyList<EcgWavePoint> points, long firstSampleIndex, int sampleRateHz)
        {
            if (points.Count == 0) return null;

            lock (_lock)
            {
                Directory.CreateDirectory(TestDataPaths.EcgCaptureDirectory);
                var path = Path.Combine(TestDataPaths.EcgCaptureDirectory,
                    $"ECG波形_导出_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                using var writer = new StreamWriter(path, false, Encoding.UTF8);
                writer.WriteLine(string.Join(',', Header));
                WriteRowsTo(writer, points, firstSampleIndex, sampleRateHz);
                _log.Info($"ECG 波形已导出 {points.Count} 个采样点：{path}");
                return path;
            }
        }

        /// <summary>打开目录用：确保目录存在并返回路径</summary>
        public string EnsureDirectory()
        {
            Directory.CreateDirectory(TestDataPaths.EcgCaptureDirectory);
            return TestDataPaths.EcgCaptureDirectory;
        }

        private void WriteRows(IReadOnlyList<EcgWavePoint> points, long firstSampleIndex, int sampleRateHz)
            => WriteRowsTo(_writer!, points, firstSampleIndex, sampleRateHz);

        /// <summary>逐点行：调用方持锁。时间固定 4 位小数（500SPS 下相邻点差 0.002s）</summary>
        private static void WriteRowsTo(TextWriter writer, IReadOnlyList<EcgWavePoint> points, long firstSampleIndex, int sampleRateHz)
        {
            var builder = new StringBuilder(points.Count * 24);
            for (var i = 0; i < points.Count; i++)
            {
                var seconds = (firstSampleIndex + i) / (double)sampleRateHz;
                builder.Append(seconds.ToString("F4", System.Globalization.CultureInfo.InvariantCulture))
                       .Append(',').Append(points[i].Adc.ToString(System.Globalization.CultureInfo.InvariantCulture))
                       .Append(',').Append(points[i].Millivolts.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
                       .AppendLine();
            }
            writer.Write(builder.ToString());
        }
    }
}
