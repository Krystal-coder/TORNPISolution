using System.Text;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Flashing
{
    /// <summary>
    /// 烧录记录：一轮里每通道一行，追加到当天的 CSV（Config\FlashRecords\flash-yyyy-MM-dd.csv）。
    /// 一天一个文件而不是一轮一个——产线一天几百轮，一轮一个会把目录淹掉，按天一个才好按日期查；行内带轮次号，单看一行只有一路的结果，判整批要按轮次聚合。
    /// 写带 UTF-8 BOM——现场直接双击用 Excel 打开看，没 BOM 中文全是乱码。
    /// </summary>
    public class FlashRecordService : IFlashRecordService
    {
        private static readonly string StorageRoot =
            Path.Combine(AppContext.BaseDirectory, "Config", "FlashRecords");

        private const string Header = "轮次,时间,通道,位号,动作,串口,结果,错误码,原因,耗时秒,条码";

        private static readonly Encoding Utf8WithBom = new UTF8Encoding(true);

        private readonly ILogService _log;
        private readonly object _writeLock = new();

        public FlashRecordService(ILogService? log = null)
            => _log = log ?? new LogService(nameof(FlashRecordService));

        public string StorageDirectory => StorageRoot;

        public void AppendRound(
            string roundId, IReadOnlyList<ChannelFlashReport> reports, IReadOnlyDictionary<int, string>? barcodes = null)
        {
            if (reports.Count == 0) return;

            try
            {
                Directory.CreateDirectory(StorageRoot);
                var path = Path.Combine(StorageRoot, $"flash-{DateTime.Now:yyyy-MM-dd}.csv");
                var isNew = !File.Exists(path);
                var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                lock (_writeLock)
                {
                    using var writer = new StreamWriter(path, append: true, Utf8WithBom);
                    if (isNew) writer.WriteLine(Header);

                    foreach (var report in reports)
                    {
                        var barcode = barcodes != null && barcodes.TryGetValue(report.Channel, out var code) ? code : string.Empty;

                        writer.WriteLine(string.Join(",", new[]
                        {
                            Escape(roundId),
                            now,
                            report.Channel.ToString(),
                            Escape(report.Name),
                            Escape(report.ActionText),
                            Escape(report.Port),
                            Escape(VerdictOf(report)),
                            (report.Result?.ErrorCode ?? 0).ToString(),
                            Escape(report.Result?.Message ?? report.Detail),
                            report.Elapsed.TotalSeconds.ToString("0.0"),
                            Escape(barcode),
                        }));
                    }
                }

                _log.Info($"烧录记录已写入 {path}（轮次 {roundId}，{reports.Count} 行）");
            }
            catch (Exception ex)
            {
                _log.Error($"烧录记录写入失败，不影响本轮结果：{ex.Message}");
            }
        }

        /// <summary>结论列：取消与跳过单独立出来，与失败混在一列里会让事后良率统计把没跑的算成不良。</summary>
        private static string VerdictOf(ChannelFlashReport report) => report.Stage switch
        {
            FlashStage.Succeeded => "成功",
            FlashStage.Skipped => "跳过",
            FlashStage.Canceled => "取消",
            _ => report.Success == true ? "成功" : "失败",
        };

        /// <summary>CSV 字段转义：原因里必然带逗号与引号（设备回原文），含逗号、引号或换行时整段加引号、内部引号按规则翻倍，否则一行会被撑成好几列。</summary>
        private static string Escape(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            return text.AsSpan().IndexOfAny(",\"\r\n") >= 0
                ? $"\"{text.Replace("\"", "\"\"")}\""
                : text;
        }
    }
}
