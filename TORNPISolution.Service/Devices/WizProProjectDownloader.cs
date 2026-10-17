using System.Diagnostics;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 把工程数据下载进 WizPro 烧录器内部 Flash（脱机烧写用的数据）。
    /// 这一步不走串口，而是调厂商上位机的命令行：APP路径 空格 工程文件路径 [空格 OFF]，
    /// 它自己搜索设备、下载数据，退出时在自己所在目录生成一个结果文件。
    /// 两条现场约束：
    ///   一是调用前必须先把本通道的串口关掉——厂商 APP 下载时要用这些 COM 口，占着它会下载失败；
    ///   二是结果文件的内部格式厂商没给，所以这里不猜内容：文件出现只当它干完活了，
    ///       真正的成败交给随后的复位自检（回 WPGN 就是内部数据异常），内容原样留进明细供排查。
    /// </summary>
    public static class WizProProjectDownloader
    {
        /// <summary>厂商上位机退出时写在它自己所在目录的结果文件（名字固定，大小写按文档原样）</summary>
        private const string ResultFileName = "ResDW.Txt";

        /// <summary>轮询结果文件的节拍（毫秒）</summary>
        private const int PollMs = 200;

        /// <summary>开下载前先检查路径能不能用，返回 null 表示没问题</summary>
        public static string? CheckArguments(WizProFlashSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.AppPath)) return "没配烧录器上位机路径（WizPro.AppPath）";
            if (!File.Exists(settings.AppPath)) return $"上位机不存在：{settings.AppPath}";
            if (string.IsNullOrWhiteSpace(settings.ProjectPath)) return "没配工程文件路径（WizPro.ProjectPath）";
            if (!File.Exists(settings.ProjectPath)) return $"工程文件不存在：{settings.ProjectPath}";
            if (settings.DownloadTimeoutMs <= 0) return "下载等待时间要大于 0";
            return null;
        }

        /// <summary>
        /// 下载工程数据到烧录器。调用方必须先关掉这一路的串口，并在成功后重新打开、再跑复位自检。
        /// 失败不抛异常，用返回结果的 Success 与明细表达；取消时上位机进程会被结束并往上抛。
        /// </summary>
        public static async Task<FlashResult> DownloadAsync(
            WizProFlashSettings settings, ILogService log, CancellationToken ct = default)
        {
            var problem = CheckArguments(settings);
            if (problem != null)
            {
                log.Error($"工程下载没有开始：{problem}");
                return FlashResult.Fail(problem, 0, FlashStage.Downloading);
            }

            var appDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.AppPath))!;
            var resultFile = Path.Combine(appDirectory, ResultFileName);
            var trace = new List<string>();

            try
            {
                // 先把上一轮的结果文件删掉：不删的话它会一直在那儿，本轮根本没跑完也会被当成成功
                if (File.Exists(resultFile))
                {
                    File.Delete(resultFile);
                    trace.Add($"已删除上一轮的 {ResultFileName}");
                }
            }
            catch (Exception ex)
            {
                // 删不掉多半是被人开着看了：这时候等它出现毫无意义，直接说明原因
                var message = $"{ResultFileName} 删不掉，无法判断本轮下载结果：{ex.Message}";
                log.Error($"工程下载失败：{message}");
                return FlashResult.Fail(message, 0, FlashStage.Downloading, trace);
            }

            // OFF 让上位机下载完自己退出；不退出就不会生成结果文件，等待只会白等满超时
            var arguments = settings.CloseAppAfterDownload
                ? $"\"{settings.ProjectPath}\" OFF"
                : $"\"{settings.ProjectPath}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = settings.AppPath,
                Arguments = arguments,
                WorkingDirectory = appDirectory,
                UseShellExecute = true,       // 上位机是图形程序，不重定向它的输出（也没有可重定向的控制台）
            };

            log.Info($"开始下载工程数据：{Path.GetFileName(settings.AppPath)} {arguments}");

            using var process = new Process { StartInfo = startInfo };
            var deadline = Environment.TickCount64 + settings.DownloadTimeoutMs;

            try
            {
                process.Start();

                while (Environment.TickCount64 < deadline)
                {
                    ct.ThrowIfCancellationRequested();

                    if (File.Exists(resultFile))
                        return ReadResult(resultFile, appDirectory, log, trace);

                    await Task.Delay(PollMs, ct).ConfigureAwait(false);

                    // 再查一次：文件常常就在进程退出前后那一瞬落盘
                    if (File.Exists(resultFile))
                        return ReadResult(resultFile, appDirectory, log, trace);

                    if (process.HasExited && !settings.CloseAppAfterDownload)
                    {
                        // 没带 OFF 却自己退了，说明上位机没能开始干活（弹了错误框也会被当成正常退出）
                        var message = $"上位机退出但没生成 {ResultFileName}（退出码 {process.ExitCode}）：检查工程文件与设备是否在线";
                        log.Error($"工程下载失败：{message}");
                        return FlashResult.Fail(message, 0, FlashStage.Downloading, trace);
                    }
                }

                // 带 OFF 却等满超时：进程还挂着就结束它，否则下一轮串口也开不了
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        trace.Add("等结果超时，已结束上位机进程");
                    }
                    catch (Exception ex)
                    {
                        trace.Add($"等结果超时，上位机进程结束失败：{ex.Message}");
                    }
                }

                var timeout = $"等 {settings.DownloadTimeoutMs}ms 没等到 {ResultFileName}：下载没完成，先确认烧录器在线、串口没被占用";
                log.Error($"工程下载失败：{timeout}");
                return FlashResult.Fail(timeout, 0, FlashStage.Downloading, trace);
            }
            catch (OperationCanceledException)
            {
                // 取消要把上位机收掉：它挂着就会一直占着设备与串口
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception ex) { log.Warn($"取消时结束上位机进程失败：{ex.Message}"); }

                log.Warn("工程下载已取消");
                throw;
            }
            catch (Exception ex)
            {
                var message = $"调上位机下载工程失败：{ex.Message}";
                log.Error(message);
                return FlashResult.Fail(message, 0, FlashStage.Downloading, trace);
            }
        }

        /// <summary>
        /// 读结果文件。内容格式厂商没给，所以只留原文不解析：成败由随后的复位自检判定，
        /// 这里只负责把上位机留下的线索抓进日志，供自检报数据异常时对照。
        /// </summary>
        private static FlashResult ReadResult(string resultFile, string appDirectory, ILogService log, List<string> trace)
        {
            string content;
            try
            {
                // 刚出现的文件可能还在被写，读失败不算错：留一句说明就够，结论交给自检
                content = File.ReadAllText(resultFile);
            }
            catch (Exception ex)
            {
                content = $"（{ResultFileName} 读不出来：{ex.Message}）";
            }

            trace.Add($"{ResultFileName}（位于 {appDirectory}）内容：");
            foreach (var line in content.Split('\n'))
            {
                var text = line.TrimEnd('\r');
                if (!string.IsNullOrWhiteSpace(text)) trace.Add($"  {text}");
            }

            log.Info($"工程数据下载完成，{ResultFileName} 内容已留档（成败由烧录器自检判定）");
            return FlashResult.Ok("工程数据下载完成", trace);
        }
    }
}
