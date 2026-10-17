using System.Collections.Concurrent;
using System.Diagnostics;
using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Flashing
{
    /// <summary>
    /// 烧录编排：一轮只做一种动作，每路独立并发、互不牵连（通道 3 失败不影响其余七路继续）。
    /// 两条动作的流程差别决定了写法：
    ///   SPI Flash 要先调厂商上位机把工程数据下载进烧录器内部——厂商的 APP 一次会搜到所有烧录器、
    ///     而且下载时要用那些 COM 口，所以这一步是全局一次而不是每路一次；本轮此时还没打开任何串口，
    ///     不存在自己占住自己要的口。下载成功后才各通道并发烧写。
    ///   主控是纯每路独立：各起一个命令行进程、各占一个串口。
    /// 顺序固定先 Flash 后主控（主控的 boot 要从 Flash 里加载），所以这里不做成可配的开关。
    /// 状态回调在采集线程上直接调用，与测试执行那边同一套约定：界面自己 marshal 回 UI 线程。
    /// </summary>
    public class FlashingService : IFlashingService
    {
        private readonly IFlashingConfigService _configService;
        private readonly ILogService _log;

        // 本轮的取消闸：调用方传进来的 ct 管整轮（页面上的“停止”），每路还各自挂一个 linked 的（卡片右键只停这一路）。
        // 只在 RunRoundAsync 活着期间有值，收尾统一 Dispose；同一时刻只有一轮在跑（界面那边用 FlashRunState 挡着）
        private CancellationTokenSource? _roundCts;
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _channelCts = new();

        public FlashingService(IFlashingConfigService configService, ILogService? log = null)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _log = log ?? new LogService(nameof(FlashingService));
        }

        /// <summary>
        /// 只停某一路：其余路不受影响，这一路自己按“已取消”落定。
        /// 正在等芯片复位的通道走这一条最有用：那一块板没摆好，不该让其余七路陪着等握手超时。
        /// </summary>
        public void CancelChannel(int channel)
        {
            if (!_channelCts.TryGetValue(channel, out var source))
            {
                _log.Info($"收到停止通道 {channel} 的请求，但本轮没有这一路在跑（已落定或本来没参与）");
                return;
            }

            try
            {
                source.Cancel();
                _log.Warn($"已请求停止通道 {channel}：这一路收尾，其余不受影响");
            }
            catch (ObjectDisposedException)
            {
                // 正好撞上本轮收尾把闸拆了：它已经跑完了，不用再停
            }
        }

        public async Task<IReadOnlyList<ChannelFlashReport>> RunRoundAsync(
            FlashAction action,
            IReadOnlyList<int>? channels,
            Action<ChannelFlashReport>? onChannelUpdate,
            CancellationToken ct = default)
        {
            _roundCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var roundToken = _roundCts.Token;
            try
            {
                var config = _configService.Load();
                var works = PlanChannels(config, action, channels);
                if (works.Count == 0)
                {
                    _log.Warn($"本轮{action.ToDisplayText()}没有可烧的通道：通道清单为空、这些通道都没启用、或点的编号不在清单里");
                    return Array.Empty<ChannelFlashReport>();
                }

                var runnable = Precheck(action, config, works, onChannelUpdate);
                if (runnable.Count == 0)
                {
                    _log.Error($"本轮{action.ToDisplayText()}没有任何一路通过预检，逐路原因见各通道明细");
                    return Snapshot(works, action);
                }

                // 一路一个 linked 闸：先给每路配好，再并发开跑，不然右键停第 3 路时会连带把其余七路一起打断
                foreach (var work in runnable)
                    _channelCts[work.Channel] = CancellationTokenSource.CreateLinkedTokenSource(roundToken);

                if (action == FlashAction.WizProFlash)
                {
                    var download = await DownloadProjectOnceAsync(config.WizPro, runnable, onChannelUpdate, roundToken).ConfigureAwait(false);
                    if (!download.Success)
                    {
                        // 工程数据没进烧录器，后面每一路都不可能成：给同一句原因，别去开八个串口各报一次超时
                        foreach (var work in runnable)
                            Finish(work, FlashResult.Fail($"工程数据没下载进去：{download.Message}"), onChannelUpdate);
                        return Snapshot(works, action);
                    }
                }

                await Task.WhenAll(runnable.Select(work =>
                    RunChannelAsync(work, action, config, onChannelUpdate, _channelCts[work.Channel].Token))).ConfigureAwait(false);

                var reports = Snapshot(works, action);
                _log.Info($"本轮{action.ToDisplayText()}结束：成功 {reports.Count(r => r.Success == true)}/{reports.Count}，" +
                          $"失败 {reports.Count(r => r.Success == false)}");
                return reports;
            }
            finally
            {
                // 闸必须在返回前拆掉：下一轮重建时不留旧令牌，否则上一轮被停的那路会把新一轮一起停掉
                foreach (var key in _channelCts.Keys)
                    if (_channelCts.TryRemove(key, out var source)) source.Dispose();

                _roundCts?.Dispose();
                _roundCts = null;
            }
        }

        // ==================== 本轮范围与预检 ====================

        /// <summary>
        /// 按通道号取出这一路的参数：清单顺序就是通道号（第一行是通道 1）。
        /// 没点名通道就取全部已启用的行；点名了但清单里没有的编号只记日志，不静默丢掉
        /// （界面点的通道号跟清单行号对不上时，静默跳过会让人以为软件漏烧）。
        /// </summary>
        private List<ChannelWork> PlanChannels(FlashingConfig config, FlashAction action, IReadOnlyList<int>? channels)
        {
            var wanted = channels is { Count: > 0 } ? channels.ToHashSet() : null;
            var works = new List<ChannelWork>();

            for (var index = 0; index < config.Channels.Count; index++)
            {
                var channel = index + 1;
                var settings = config.Channels[index];

                if (wanted != null)
                {
                    if (!wanted.Remove(channel)) continue;
                }
                else if (!settings.Enabled)
                {
                    continue;
                }

                var work = new ChannelWork(channel, settings.Name, settings, action);
                // 本轮这一路实际用哪个口，建工作项时就定下来：预检不通过的路也要能在记录里查到当时配的是哪个口
                work.State.Port = action == FlashAction.WizProFlash ? settings.WizProPortName : settings.FreqchipPortName;
                works.Add(work);
            }

            // 点名了却没匹配上的编号（全部匹配上时这个集合已被 Remove 空）：只记日志不静默丢掉，
            // 界面点的通道号跟清单行号对不上时，静默跳过会让人以为软件漏烧了
            if (wanted is { Count: > 0 })
                foreach (var missing in wanted)
                    _log.Warn($"通道 {missing} 不在烧录清单里（清单只有 {config.Channels.Count} 行），本轮跳过它");

            return works;
        }

        /// <summary>
        /// 开烧前逐路检查参数，不通过的直接标失败、不参与后面的下载与烧写。
        /// 拦在这里是因为放过去只会得到一句握手超时或打不开串口，现场查半天发现是配置填错。
        /// </summary>
        private List<ChannelWork> Precheck(
            FlashAction action, FlashingConfig config, IReadOnlyList<ChannelWork> works, Action<ChannelFlashReport>? notify)
        {
            // 上位机与工程文件路径对所有路都一样，先整体查一遍，缺了就没必要逐路报八遍同样的话
            var globalProblem = action == FlashAction.WizProFlash ? WizProProjectDownloader.CheckArguments(config.WizPro) : null;
            if (globalProblem != null)
                _log.Error($"烧录参数不完整：{globalProblem}");

            var runnable = new List<ChannelWork>();
            foreach (var work in works)
            {
                var problem = globalProblem ?? CheckChannel(action, config, work.Settings);
                if (problem == null)
                {
                    runnable.Add(work);
                    continue;
                }

                work.State.Trace.Add($"[{Now()}] 预检不通过：{problem}");
                Finish(work, FlashResult.Fail(problem), notify);
            }

            return runnable;
        }

        private static string? CheckChannel(FlashAction action, FlashingConfig config, FlashChannelSettings settings)
        {
            if (action == FlashAction.WizProFlash)
                return string.IsNullOrWhiteSpace(settings.WizProPortName)
                    ? "这一路没配烧录器串口（WizProPortName）"
                    : null;

            if (string.IsNullOrWhiteSpace(settings.FreqchipPortName))
                return "这一路没配主控烧录串口（FreqchipPortName）";

            return FreqchipFlasher.CheckArguments(config.Freqchip, settings.FreqchipPortName);
        }

        // ==================== SPI Flash 的工程数据下载（全局一次） ====================

        private async Task<FlashResult> DownloadProjectOnceAsync(
            WizProFlashSettings settings, IReadOnlyList<ChannelWork> works, Action<ChannelFlashReport>? notify, CancellationToken ct)
        {
            foreach (var work in works)
            {
                work.State.Stage = FlashStage.Downloading;
                work.State.Detail = "下载工程数据到烧录器";
                work.State.Trace.Add($"[{Now()}] 下载工程数据（全局一次，覆盖所有烧录器）");
                Push(work, notify);
            }

            var result = await WizProProjectDownloader.DownloadAsync(settings, _log, ct).ConfigureAwait(false);

            foreach (var work in works)
            {
                work.State.Detail = result.Success ? "工程数据已下载" : $"工程数据下载失败：{result.Message}";
                foreach (var line in result.Trace ?? Array.Empty<string>())
                    work.State.Trace.Add($"[{Now()}] {line}");
                Push(work, notify);
            }

            return result;
        }

        // ==================== 每路并发 ====================

        /// <summary>单路的全部异常都收在这里：一条路的失败不能把整轮的 WhenAll 打断。</summary>
        private async Task RunChannelAsync(
            ChannelWork work, FlashAction action, FlashingConfig config, Action<ChannelFlashReport>? notify, CancellationToken ct)
        {
            work.State.Timer = Stopwatch.StartNew();
            try
            {
                var result = action == FlashAction.WizProFlash
                    ? await ProgramWizAsync(work, config.WizPro, notify, ct).ConfigureAwait(false)
                    : await ProgramFreqchipAsync(work, config.Freqchip, notify, ct).ConfigureAwait(false);

                Finish(work, result, notify);
            }
            catch (OperationCanceledException)
            {
                Finish(work, FlashResult.Canceled(work.State.Trace.ToArray()), notify);
            }
            catch (Exception ex)
            {
                _log.Error($"{work.Name} 烧录异常：{ex.Message}");
                Finish(work, FlashResult.Fail(ex.Message), notify);
            }
        }

        private async Task<FlashResult> ProgramWizAsync(
            ChannelWork work, WizProFlashSettings settings, Action<ChannelFlashReport>? notify, CancellationToken ct)
        {
            var port = work.Settings.WizProPortName;
            using IWizProProgrammer programmer = new WizProProgrammer(work.Channel, port, settings, _log);

            await programmer.OpenAsync(ct).ConfigureAwait(false);
            work.State.Trace.Add($"[{Now()}] 串口 {port} 已打开");

            if (settings.SelfCheckBeforeProgram)
            {
                Mark(work, FlashStage.SelfChecking, "烧录器自检", notify);
                if (!await programmer.SelfCheckAsync(ct).ConfigureAwait(false))
                    return FlashResult.Fail("烧录器自检不通过：内部工程数据异常，要重新下载", 0, FlashStage.SelfChecking);
            }

            return await programmer.ProgramAsync(ProgressFor(work, notify), ct).ConfigureAwait(false);
        }

        private async Task<FlashResult> ProgramFreqchipAsync(
            ChannelWork work, FreqchipFlashSettings settings, Action<ChannelFlashReport>? notify, CancellationToken ct)
        {
            var port = work.Settings.FreqchipPortName;
            using IFreqchipFlasher flasher = new FreqchipFlasher(work.Channel, port, settings, _log);

            Mark(work, FlashStage.WaitingReset, "启动烧录工具", notify);
            return await flasher.FlashAsync(ProgressFor(work, notify), ct).ConfigureAwait(false);
        }

        // ==================== 状态推进与上报 ====================

        private IProgress<FlashProgress> ProgressFor(ChannelWork work, Action<ChannelFlashReport>? notify)
            => new DirectProgress(p =>
            {
                var changed = work.State.Stage != p.Stage;
                work.State.Stage = p.Stage;
                work.State.Percent = p.Percent;
                work.State.Detail = p.Detail;

                // 只在阶段真的换了一步时记时间线：握手倒计时每 200ms 刷一次，全记进去明细就没法看了
                if (changed) work.State.Trace.Add($"[{Now()}] {p.Detail}");
                Push(work, notify);
            });

        private void Mark(ChannelWork work, FlashStage stage, string detail, Action<ChannelFlashReport>? notify)
        {
            work.State.Stage = stage;
            work.State.Detail = detail;
            work.State.Trace.Add($"[{Now()}] {detail}");
            Push(work, notify);
        }

        private void Finish(ChannelWork work, FlashResult result, Action<ChannelFlashReport>? notify)
        {
            work.State.Timer?.Stop();
            work.State.Result = result;
            work.State.Stage = result.Stage;
            work.State.Detail = result.Message;
            if (result.Success) work.State.Percent = 100;

            work.State.Trace.Add($"[{Now()}] {(result.Success ? "成功" : "未通过")}：{result.Message}");
            _log.Info($"{work.Name} {work.Action.ToDisplayText()} {(result.Success ? "PASS" : "FAIL")}，" +
                      $"耗时 {work.State.Elapsed.TotalSeconds:0.#}s");
            Push(work, notify);
        }

        private static void Push(ChannelWork work, Action<ChannelFlashReport>? notify)
            => notify?.Invoke(work.State.ToReport(work.Channel, work.Name, work.Action));

        private static List<ChannelFlashReport> Snapshot(IReadOnlyList<ChannelWork> works, FlashAction action)
            => works.Select(w => w.State.ToReport(w.Channel, w.Name, action)).ToList();

        private static string Now() => DateTime.Now.ToString("HH:mm:ss.fff");

        /// <summary>一路本轮的可变状态：对外只发不可变快照，见 <see cref="ChannelFlashReport"/></summary>
        private sealed class ChannelState
        {
            public FlashStage Stage { get; set; } = FlashStage.Idle;
            public int Percent { get; set; } = -1;
            public string Detail { get; set; } = "等待开始";
            public string Port { get; set; } = string.Empty;
            public FlashResult? Result { get; set; }
            public List<string> Trace { get; } = new();
            public Stopwatch? Timer { get; set; }

            public TimeSpan Elapsed => Timer?.Elapsed ?? TimeSpan.Zero;

            public ChannelFlashReport ToReport(int channel, string name, FlashAction action) => new(
                channel, name, Port, action, Stage, Percent, Detail, Result, Trace.ToArray(), Elapsed);
        }

        private sealed class ChannelWork(int channel, string name, FlashChannelSettings settings, FlashAction action)
        {
            public int Channel { get; } = channel;
            public string Name { get; } = name;
            public FlashChannelSettings Settings { get; } = settings;
            public FlashAction Action { get; } = action;
            public ChannelState State { get; } = new();
        }

        /// <summary>
        /// 同步转发的进度接收器。不用系统的 Progress&lt;T&gt;：它会 post 到创建时捕获的同步上下文，
        /// 八条路并发时更新会乱序、也不保证在通道结束前跑完，界面就可能拿上一条的进度盖掉这一条的结论。
        /// </summary>
        private sealed class DirectProgress(Action<FlashProgress> handler) : IProgress<FlashProgress>
        {
            public void Report(FlashProgress value) => handler(value);
        }
    }
}
