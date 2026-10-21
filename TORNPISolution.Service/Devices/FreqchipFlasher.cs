using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 主控 MCU 的命令行烧录：起厂商烧录工具的进程，逐行读它 stdout 上的 JSON 进度与结论。
    /// 工具的握手原理决定了顺序不能反：芯片只在一上电那一刻用内部 boot 程序找工具，
    /// 所以要先起进程（它回 Ready 表示串口已开、正在等芯片复位），再让芯片复位；
    /// 握手超时就是这个窗口的时长，逐路人工按复位键时这段时间要留够。
    /// 读输出走事件加队列而不是同步 ReadLine：同步读会一直卡在下一行到来之前，取消和超时都响应不了。
    /// 标准错误也照样在读：不读它的话 stderr 缓冲写满会把工具顶死在那儿，表现为整路不动。
    /// 循环顺序是先取行、取不到才判进程退出，反了会在进程刚退、最后一行还没入队时漏掉结论行。
    /// </summary>
    public class FreqchipFlasher : IFreqchipFlasher
    {
        // 工具输出的状态词（使用说明里的五种）
        private const string StatusReady = "Ready";
        private const string StatusErasing = "Erasing";
        private const string StatusProgramming = "Programming";
        private const string StatusFinished = "Finished";
        private const string StatusFail = "Fail";

        /// <summary>结论行的 Code，只有 1 是成功</summary>
        private const int CodeSuccess = 1;

        /// <summary>取不到输出时的轮询节拍（毫秒），也是握手倒计时刷新的间隔</summary>
        private const int PollMs = 200;

        private readonly FreqchipFlashSettings _settings;
        private readonly ILogService _log;
        private readonly string _tag;
        private bool _disposed;

        public FreqchipFlasher(int channel, string portName, FreqchipFlashSettings settings, ILogService? log = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException($"通道 {channel} 没有配烧录串口（烧录配置里这一路的 FreqchipPortName 是空的）", nameof(portName));

            Channel = channel;
            PortName = portName;
            _tag = $"主控烧录通道{channel}";
            _log = log ?? new LogService($"{nameof(FreqchipFlasher)}:CH{channel}");
        }

        public int Channel { get; }

        public string PortName { get; }

        /// <summary>
        /// 开烧前检查参数能不能用：返回 null 表示没问题，否则是可直接展示给操作人员的错因。
        /// 芯片系列与波特率的搭配单独拦一道：30xx 系列最高只能 460800，填高了工具只会报握手超时。
        /// </summary>
        public static string? CheckArguments(FreqchipFlashSettings settings, string portName)
        {
            if (string.IsNullOrWhiteSpace(portName)) return "这一路没配烧录串口";
            if (string.IsNullOrWhiteSpace(settings.ToolPath)) return "没配烧录工具路径";
            if (!File.Exists(settings.ToolPath)) return $"烧录工具不存在：{settings.ToolPath}";
            if (string.IsNullOrWhiteSpace(settings.FirmwarePath)) return "没配固件路径";
            if (!File.Exists(settings.FirmwarePath)) return $"固件文件不存在：{settings.FirmwarePath}";
            if (settings.HandshakeTimeoutSeconds is < 1 or > 30)
                return $"握手超时只能 1~30 秒，当前 {settings.HandshakeTimeoutSeconds}";
            if (settings.Chip == FreqchipChip.Series30xx && settings.BaudRate > 460800)
                return $"30xx 系列波特率最高 460800，当前填了 {settings.BaudRate}";
            if (string.IsNullOrWhiteSpace(settings.EraseSize)) return "没配擦除范围（-size）";
            return null;
        }

        /// <summary>工具错误码转中文（使用说明给的 1~10 全表，超出范围的照实说没记述）</summary>
        public static string DescribeCode(int code) => code switch
        {
            1 => "烧录成功",
            2 => "打开串口失败（串口被别的程序占着，或这一路配的不是这个口）",
            3 => "擦除 flash 失败",
            4 => "写文件失败",
            5 => "写 boot 失败",
            6 => "参数不对：没传 -download",
            7 => "打开固件文件失败",
            8 => "切换波特率失败",
            9 => "串口被拔出",
            10 => "握手超时：芯片没在窗口内复位（没压到位、没按复位键，或波特率超过芯片上限）",
            _ => $"未记述的错误码 {code}（工具说明里没有这一条，原文见明细）",
        };

        /// <summary>拼命令行参数：路径可能带空格，一律加引号</summary>
        private string BuildArguments() =>
            $"-download -p {PortName} -file \"{_settings.FirmwarePath}\" " +
            $"-b {_settings.BaudRate} -t {_settings.HandshakeTimeoutSeconds} " +
            $"-chip {(int)_settings.Chip} -size {_settings.EraseSize}";

        public Task<FlashResult> FlashAsync(IProgress<FlashProgress>? onProgress, CancellationToken ct = default)
            => Task.Run<FlashResult>(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var problem = CheckArguments(_settings, PortName);
            if (problem != null)
            {
                _log.Error($"{_tag} 参数不对，没有起进程：{problem}");
                return FlashResult.Fail(problem);
            }

            var trace = new List<string>();
            var lines = new ConcurrentQueue<string>();
            var handshakeMs = Math.Clamp(_settings.HandshakeTimeoutSeconds, 1, 30) * 1000L;

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _settings.ToolPath,
                    Arguments = BuildArguments(),
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(_settings.ToolPath)) ?? string.Empty,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };

            void OnOutput(object? sender, DataReceivedEventArgs e)
            {
                if (e.Data != null) lines.Enqueue(e.Data);
            }

            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += OnOutput;

            var deadline = Environment.TickCount64 + _settings.ProcessTimeoutMs;
            var readyAt = 0L;                 // 收到 Ready 的时刻，用来算握手还剩几秒
            var stage = FlashStage.Idle;

            try
            {
                _log.Info($"{_tag} 起烧录工具：{Path.GetFileName(_settings.ToolPath)} {BuildArguments()}");
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    if (lines.TryDequeue(out var line))
                    {
                        trace.Add(line);
                        _log.Debug($"{_tag} {line}");

                        var verdict = ParseLine(line, out var lineStage, out var percent, out var code, out var message);
                        if (verdict == LineVerdict.Ignored) continue;

                        if (verdict == LineVerdict.Ready)
                        {
                            readyAt = Environment.TickCount64;
                            stage = lineStage;
                            onProgress?.Report(new FlashProgress(stage, -1,
                                $"{message}（这一路还剩 {_settings.HandshakeTimeoutSeconds} 秒，请按复位键）"));
                            continue;
                        }

                        if (verdict == LineVerdict.Done)
                        {
                            var ok = code == CodeSuccess;
                            var text = ok ? "烧录成功" : $"{DescribeCode(code)}｜工具原文：{message}";
                            if (ok) _log.Info($"{_tag} {text}");
                            else _log.Error($"{_tag} {text}");

                            return ok ? FlashResult.Ok(text, trace)
                                      : FlashResult.Fail(text, code, FlashStage.Failed, trace);
                        }

                        stage = lineStage;
                        onProgress?.Report(new FlashProgress(stage, percent, message));
                        continue;
                    }

                    // 没新输出：先看进程是不是自己退了，再看总超时，最后刷握手倒计时
                    if (process.HasExited)
                    {
                        _log.Error($"{_tag} 工具退出但没给出结论行（退出码 {process.ExitCode}）");
                        return FlashResult.Fail($"烧录工具没给出结论就退出了（退出码 {process.ExitCode}，原始输出见明细）",
                            0, FlashStage.Failed, trace);
                    }

                    if (Environment.TickCount64 > deadline)
                    {
                        KillTree(process);
                        _log.Error($"{_tag} 超过 {_settings.ProcessTimeoutMs}ms 工具还没结束，已强制结束进程");
                        return FlashResult.Fail($"烧录工具卡住不动，已强制结束（超过 {_settings.ProcessTimeoutMs / 1000} 秒）",
                            0, FlashStage.Failed, trace);
                    }

                    if (stage == FlashStage.WaitingReset && readyAt > 0)
                    {
                        var leftMs = handshakeMs - (Environment.TickCount64 - readyAt);
                        if (leftMs > 0)
                            onProgress?.Report(new FlashProgress(FlashStage.WaitingReset,
                                (int)Math.Clamp(leftMs * 100 / handshakeMs, 0, 100),
                                $"等芯片复位握手，剩 {leftMs / 1000 + 1} 秒（按下这一路的复位键）"));
                    }

                    Thread.Sleep(PollMs);
                }
            }
            catch (OperationCanceledException)
            {
                // 取消必须把进程收掉：留着它串口就一直被占，下一轮根本开不了
                KillTree(process);
                _log.Warn($"{_tag} 已取消，烧录工具进程已结束");
                throw;
            }
            finally
            {
                process.OutputDataReceived -= OnOutput;
                process.ErrorDataReceived -= OnOutput;
            }
        }, ct);

        /// <summary>一行的解析结论</summary>
        private enum LineVerdict
        {
            /// <summary>不是 JSON（工具还会打 FILE PATH / BAUDRATE / Version 这些抬头行），只留原文</summary>
            Ignored,

            /// <summary>串口已开、正在等芯片复位</summary>
            Ready,

            /// <summary>过程中的进度</summary>
            Progress,

            /// <summary>结论行（成功或失败，看 Code）</summary>
            Done,
        }

        /// <summary>解析一行输出。属性名一律按大小写不敏感取：厂商文档里进度字段一会儿写 Progress 一会儿写 progress，用区分大小写解析会静默丢掉进度数字，界面看着就像卡死在那一步。</summary>
        private LineVerdict ParseLine(string line, out FlashStage stage, out int percent, out int code, out string message)
        {
            stage = FlashStage.Idle;
            percent = -1;
            code = 0;
            message = line;

            var start = line.IndexOf('{');
            if (start < 0) return LineVerdict.Ignored;

            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(line.Substring(start));
                root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return LineVerdict.Ignored;
            }

            if (root.ValueKind != JsonValueKind.Object) return LineVerdict.Ignored;

            var status = ReadString(root, "Status");
            if (string.IsNullOrEmpty(status)) return LineVerdict.Ignored;

            code = ReadInt(root, "Code");
            percent = ReadInt(root, "Progress");
            message = ReadString(root, "Message") ?? status;

            switch (status)
            {
                case StatusReady:
                    stage = FlashStage.WaitingReset;
                    message = "串口已打开，等芯片复位";
                    return LineVerdict.Ready;
                case StatusErasing:
                    stage = FlashStage.Erasing;
                    message = "擦除 flash";
                    return LineVerdict.Progress;
                case StatusProgramming:
                    stage = FlashStage.Programming;
                    message = "写入固件";
                    return LineVerdict.Progress;
                case StatusFinished:
                    stage = FlashStage.Succeeded;
                    return LineVerdict.Done;
                case StatusFail:
                    stage = FlashStage.Failed;
                    return LineVerdict.Done;
                default:
                    _log.Warn($"{_tag} 工具回了一个不认识的状态：{line}");
                    return LineVerdict.Ignored;
            }
        }

        /// <summary>按名字取字符串字段（JsonDocument 不支持大小写不敏感，只能自己扫）</summary>
        private static string? ReadString(JsonElement root, string name)
            => TryFind(root, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        /// <summary>按名字取整数字段，取不到返回 -1（0 是合法值，比如 Progress:0）；数字带引号也认</summary>
        private static int ReadInt(JsonElement root, string name)
        {
            if (!TryFind(root, name, out var value)) return -1;

            return value.ValueKind switch
            {
                JsonValueKind.Number => value.TryGetInt32(out var number) ? number : -1,
                JsonValueKind.String => int.TryParse(value.GetString(), out var parsed) ? parsed : -1,
                _ => -1,
            };
        }

        private static bool TryFind(JsonElement root, string name, out JsonElement value)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        /// <summary>结束进程（连子进程一起）：工具被卡住或取消时不留占着串口的僵尸进程</summary>
        private static void KillTree(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            catch (InvalidOperationException)
            {
                // 进程已经退出了：正是要的结果
            }
            catch (Exception)
            {
                // 杀不掉也不要把异常抛到界面上，原始输出已经留在明细里
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
