using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SkiaSharp;
using TORNPISolution.Common;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// ECG 波形采集页：与整机测试、加速度充电检查共用被测产品串口，收 0x8D 成对帧（ADC 码值 + 电压码值）双路画波形。
    /// 只做采集、观察与 CSV 导出，不产测试项、不进方案；内存留 60 分钟，超出按最旧 10 分钟块滚动落盘。
    /// </summary>
    public class EcgCaptureViewModel : ViewModelBase
    {
        /// <summary>内存保留跨度（秒）：再长就只是把界面和内存一起拖垮，历史数据在滚动 CSV 里</summary>
        private const double RetentionSeconds = 60 * 60;

        /// <summary>一次切块落盘的跨度（秒）：与保留跨度成整数倍，切出去的块正好对上文件里的行</summary>
        private const double ChunkSeconds = 10 * 60;

        /// <summary>一次定时器 tick 最多补多少个点进缓冲：按批进场，避免逐帧排队把消息泵压死</summary>
        private const int DrainBatchPoints = 5_000;

        /// <summary>排队帧数上限：界面被拖住时宁可明着丢帧并计数，也不能让队列把内存吃掉</summary>
        private const int MaxPendingFrames = 2_000;

        private readonly EcgCaptureService _service;
        private readonly ILogService _log = new LogService(nameof(EcgCaptureViewModel));
        private readonly EcgCaptureCsvWriter _csv = new();
        private readonly ConcurrentQueue<EcgWavePoint[]> _pending = new();
        private readonly DispatcherTimer _drainTimer;
        private readonly List<EcgWavePoint> _raw = new();

        /// <summary>缓冲里第一个点的绝对采样序号：横轴与落盘时间都由它换算，切块只在头部推进</summary>
        private long _firstIndex;

        private long _droppedFrames;
        private bool _droppedLogged;
        private bool _awaitingWrapUp;

        public EcgCaptureViewModel(EcgCaptureService service)
        {
            _service = service;
            _drainTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            _drainTimer.Tick += (_, _) => DrainPending();

            _service.WaveReceived += OnWaveReceived;
            _service.StateChanged += OnServiceStateChanged;

            var rate = _service.SampleRateHz;
            Adc = new EcgTraceChannel(this, new EcgTraceStyle("ADC 原值", "码值", "F0", "F0",
                                      new SKColor(255, 0, 0), 8_200_000, 8_600_000, 1), rate, p => p.Adc);
            Millivolts = new EcgTraceChannel(this, new EcgTraceStyle("电压 (mV)", "mV", "F2", "F1",
                                      new SKColor(0, 0, 255), -5, 5, 0.01), rate, p => p.Millivolts);
            Channels = new[] { Adc, Millivolts };

            ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => !_service.IsConnected && CanUsePort());
            // 还在推流时不让人按断开：先看到「停止采集」把流停了，再交口
            DisconnectCommand = new RelayCommand(async () => await _service.DisconnectAsync(),
                () => _service.IsConnected && !_service.IsCapturing && !_service.IsDisconnecting);
            StartCaptureCommand = new RelayCommand(async () => await StartCaptureAsync(), () => _service.IsConnected && !_service.IsCapturing);
            StopCaptureCommand = new RelayCommand(async () => await _service.StopCaptureAsync(), () => _service.IsCapturing);
            NewSegmentCommand = new RelayCommand(NewSegment);
            ExportCommand = new RelayCommand(Export, () => _raw.Count > 0);
            ClearDisplayCommand = new RelayCommand(ClearAll, () => _raw.Count > 0);
            OpenFolderCommand = new RelayCommand(OpenFolder);

            RefreshState();
        }

        // ==================== 界面绑定 ====================

        /// <summary>两路通道（ADC / 电压），页上两个波形面板各绑一路</summary>
        public EcgTraceChannel Adc { get; }

        public EcgTraceChannel Millivolts { get; }

        /// <summary>两路一起过泵：新增数据、切块落盘、视图重建都按同一份缓冲来</summary>
        public EcgTraceChannel[] Channels { get; }

        public string PortText => _service.IsConnected
            ? $"{_service.PortSummary}（采集{(_service.IsCapturing ? "中" : "未开始")}）"
            : "未连接——串口参数取「配置→被测产品」";

        public string SummaryText =>
            $"波形帧 {_service.FrameCount:N0} ｜ 采样点 {_service.SampleCount:N0} ｜ 缓冲 {SecondsRetained:F1} s / {RetentionSeconds / 60:F0} min"
            + $" ｜ {_service.SampleRateHz} SPS{(_droppedFrames > 0 ? $" ｜ 界面排队满，已丢 {_droppedFrames:N0} 帧" : string.Empty)}";

        public string CsvText => _csv.SessionFile == null
            ? "还没落过盘：内存满 60 分钟才切块，要现在就存就点「导出当前」"
            : $"滚动文件：{_csv.SessionFile}";

        private string _hintText = "连上后点「开始采集」。这一页不进测试方案，连接时会替产品进入产测。";

        public string HintText
        {
            get => _hintText;
            private set => SetProperty(ref _hintText, value);
        }

        /// <summary>这个 COM 口是否还归这一路：主窗口的互斥闸门看它</summary>
        public bool IsPortBusy => _service.IsPortBusy;

        public bool IsCapturing => _service.IsCapturing;

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand StartCaptureCommand { get; }
        public ICommand StopCaptureCommand { get; }
        public ICommand NewSegmentCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ClearDisplayCommand { get; }
        public ICommand OpenFolderCommand { get; }

        private double SecondsRetained => _raw.Count / (double)_service.SampleRateHz;

        // ==================== 界面回灌的读图操作 ====================

        /// <summary>框选区间统计（按住 Shift 拖出的那段）：由波形面板算好横轴范围回灌</summary>
        public void ApplyRegionSelection(EcgTraceChannel channel, double x1, double x2)
        {
            if (x1 > x2) (x1, x2) = (x2, x1);

            var (start, end) = channel.RangeOf(_raw, _firstIndex, x1, x2);
            if (end - start < 2)
            {
                channel.ClearSelection();
                return;
            }

            var max = double.MinValue;
            var min = double.MaxValue;
            double sum = 0;
            for (var i = start; i < end; i++)
            {
                var y = channel.Value(_raw[i]);
                if (y > max) max = y;
                if (y < min) min = y;
                sum += y;
            }

            var count = end - start;
            var rate = _service.SampleRateHz;
            channel.ShowSelection(count, (_firstIndex + start) / (double)rate, (_firstIndex + end - 1) / (double)rate,
                                  max, min, sum / count);
            _log.Info($"ECG 区间统计（{channel.Title}）：{count} 点，最大 {max}，最小 {min}，平均 {sum / count}");
        }

        /// <summary>鼠标十字标读数：横轴邻域内按二维像素距离取最近点，避免只按横向最近读到离波形很远的值</summary>
        public void UpdateCursor(EcgTraceChannel channel, double x, double y, double pxPerX, double pxPerY, double xTol)
        {
            var (start, end) = channel.RangeOf(_raw, _firstIndex, x - xTol, x + xTol);
            if (end <= start)
            {
                channel.ShowCursor(null, null);
                return;
            }

            var best = start;
            var bestDistance = double.MaxValue;
            for (var i = start; i < end; i++)
            {
                var dx = (_firstIndex + i - x) * pxPerX;
                var dy = (channel.Value(_raw[i]) - y) * pxPerY;
                var distance = dx * dx + dy * dy;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = i;
            }

            channel.ShowCursor(_firstIndex + best, channel.Value(_raw[best]));
        }

        // ==================== 连接与采集 ====================

        /// <summary>整机/单板那一轮、加速度那一页在跑，或这一路正在后台收尾时，这个口不归本页</summary>
        private bool CanUsePort() => !TestRunState.IsRunning && !_service.IsPortBusy;

        private async Task ConnectAsync()
        {
            if (!CanUsePort())
            {
                HintText = "整机/单板那一轮或加速度充电检查还在用这个串口（或这一路正在收尾），先等它把口交出来再连";
                return;
            }

            try
            {
                var receipt = await _service.ConnectAsync();
                HintText = string.IsNullOrEmpty(receipt)
                    ? $"已连上 {_service.PortSummary}，产品已进入产测态，直接点「开始采集」"
                    : receipt;
            }
            catch (UnauthorizedAccessException)
            {
                // 同一个 COM 口只能有一个持有者：另一路辅助页连着未断开时占口，本页只能提示去处
                _log.Warn($"产品串口正被别处占着（{PortText}）");
                HintText = "这个 COM 口正被别处占着（多半是「加速度充电检查」那一页连了还没点「断开」）：回那一页交出口再连";
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in EcgCaptureViewModel.class ConnectAsync method, message: {ex}", ex);
                HintText = $"连接失败：{ex.Message}";
            }
            finally
            {
                RefreshState();
            }
        }

        private async Task StartCaptureAsync()
        {
            // 开始采集先把界面上填的纵轴量程落一次，避免第一帧就撞见自动缩放的轴
            foreach (var channel in Channels) channel.LockAxisFromInputs();

            var ok = await _service.StartCaptureAsync();
            HintText = ok
                ? "产品已开始推波形；回整机页要先点「断开」（它会连推流一起停）"
                : "开始心电测试没等到应答：界面按未采集处理，可再点一次";
            RefreshState();
        }

        // ==================== 轴与段 ====================

        /// <summary>通道上的轴键把操作结果回灌到页面顶部的提示行</summary>
        internal void SetHint(string text) => HintText = text;

        /// <summary>另起一段：本段内存先落盘，序号与时间轴回到 0，滚动 CSV 也另开一份</summary>
        private void NewSegment()
        {
            FlushToSessionCsv();
            _csv.FinishSegment();

            _drainTimer.Stop();
            _pending.Clear();
            _raw.Clear();
            _firstIndex = 0;
            _droppedFrames = 0;
            _droppedLogged = false;
            foreach (var channel in Channels) channel.ResetView();

            HintText = "已另起一段：上一段落进滚动 CSV，时间轴与序号回到 0";
            RefreshState();
        }

        /// <summary>清空：内存里的点直接丢（不落盘），两路轴回到默认量程；要留数据先点「导出当前」</summary>
        private void ClearAll()
        {
            _drainTimer.Stop();
            _pending.Clear();
            _raw.Clear();
            _firstIndex = 0;
            _droppedFrames = 0;
            _droppedLogged = false;
            foreach (var channel in Channels)
            {
                channel.ResetView();
                channel.ResetAxis();
            }

            HintText = "已清空内存里的波形（没落盘的这一段不留痕），两路轴回到默认量程";
            RefreshState();
        }

        private void Export()
        {
            try
            {
                var path = _csv.Export(_raw, _firstIndex, _service.SampleRateHz);
                HintText = path == null ? "内存里没有点，导出跳过" : $"已导出 {_raw.Count:N0} 个点：{path}";
                OnPropertyChanged(nameof(CsvText));
            }
            catch (Exception ex)
            {
                _log.Warn($"导出 ECG 波形 CSV 失败：{ex.Message}");
                HintText = $"导出失败：{ex.Message}";
            }
        }

        private void OpenFolder()
        {
            try
            {
                var directory = _csv.EnsureDirectory();
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _log.Warn($"打开 ECG 波形目录失败：{ex.Message}");
                HintText = $"打不开目录：{ex.Message}";
            }
        }

        // ==================== 页面切换与收尾 ====================

        /// <summary>离开本页：只收界面定时器；没点「断开」交出口就出不去，所以走到这里时推流、产测态与落盘都已收干净</summary>
        public void LeavePage()
        {
            _drainTimer.Stop();
            _log.Info($"已离开 ECG 波形采集页：{_service.PortSummary}");
        }

        public void EnterPage() => RefreshState();

        /// <summary>关窗口前调用：限时收尾（产品不能留在推流态，内存里这段要先落盘）</summary>
        public void StopAndAwaitWrapUp(int timeoutMs = 5_000)
        {
            _drainTimer.Stop();
            if (_awaitingWrapUp) return;   // 消息泵期间再进来一次直接返回，不叠消息泵

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                _service.StopAndAwaitWrapUp(timeoutMs);
                return;
            }

            _awaitingWrapUp = true;
            try
            {
                _log.Info($"===== 关闭窗口：ECG 波形采集限时收尾（最长 {timeoutMs / 1000}s）=====");

                // 服务层不引用 WPF，泵只能架在这层：收尾丢给线程池跑，UI 线程继续泵消息等它，界面不再被 5 秒硬阻塞钉死
                FlushToSessionCsv();
                var pending = Task.Run(() => _service.StopAndAwaitWrapUp(timeoutMs));
                var frame = new DispatcherFrame();
                pending.ContinueWith(_ => dispatcher.Invoke(() => frame.Continue = false), TaskScheduler.Default);
                _ = Task.Delay(timeoutMs).ContinueWith(_ => dispatcher.Invoke(() => frame.Continue = false), TaskScheduler.Default);
                Dispatcher.PushFrame(frame);

                if (!pending.IsCompleted)
                    _log.Warn($"ECG 波形采集收尾等待 {timeoutMs / 1000}s 仍未完成，不阻拦退出；产品可能仍在推波形");
            }
            finally
            {
                _awaitingWrapUp = false;
            }
        }

        // ==================== 采样进缓冲 ====================

        /// <summary>读线程回调：整帧入队；排队满到上限就明着丢并计数，不靠无限增长掩盖界面跟不上。
        /// 丢掉的那段不进时间轴（缓冲里的点按连续序号排），所以摘要必须报丢帧数，否则 CSV 的秒数会偏小而没人察觉</summary>
        private void OnWaveReceived(EcgWavePoint[] points)
        {
            if (_pending.Count > MaxPendingFrames)
            {
                Interlocked.Increment(ref _droppedFrames);
                if (!_droppedLogged)
                {
                    _droppedLogged = true;
                    _log.Warn($"ECG 波形排队已满（{MaxPendingFrames} 帧）：界面跟不上，开始丢帧显示（落盘与计数照常）");
                }

                return;
            }

            _pending.Enqueue(points);
        }

        private void DrainPending()
        {
            var hadData = _raw.Count > 0;
            var added = 0;
            while (added < DrainBatchPoints && _pending.TryDequeue(out var frame))
            {
                _raw.AddRange(frame);
                added += frame.Length;
            }

            if (added > 0) TrimToRetention();
            PumpViews();

            // 「导出当前」「清空显示」的空/有数据开关只在刚开始有数据这一下要重算，否则采起来一直是灰的
            if (!hadData && _raw.Count > 0) RequeryCommands();

            if (_pending.Count == 0 && !_service.IsCapturing) _drainTimer.Stop();
        }

        /// <summary>内存超过保留跨度就把最旧的整块切出去落盘：磁盘写在后台线程，既不占读线程也不占 UI 线程</summary>
        private void TrimToRetention()
        {
            var keep = (int)(RetentionSeconds * _service.SampleRateHz);
            if (_raw.Count <= keep) return;

            var cut = Math.Min((int)(ChunkSeconds * _service.SampleRateHz), _raw.Count);
            var block = _raw.GetRange(0, cut);
            _raw.RemoveRange(0, cut);
            _firstIndex += cut;
            WriteBlock(block, _firstIndex - cut);
        }

        /// <summary>把缓冲里现有的点整块追加到滚动 CSV（另起一段与退出前调）</summary>
        private void FlushToSessionCsv()
        {
            if (_raw.Count == 0) return;
            WriteBlock(new List<EcgWavePoint>(_raw), _firstIndex);
        }

        private void WriteBlock(List<EcgWavePoint> block, long firstIndex)
        {
            var rate = _service.SampleRateHz;
            Task.Run(() =>
            {
                try
                {
                    var path = _csv.AppendChunk(block, firstIndex, rate);
                    _log.Info($"ECG 波形已落盘 {block.Count} 点到 {path}");
                    if (Application.Current?.Dispatcher != null)
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(nameof(CsvText))));
                }
                catch (Exception ex)
                {
                    _log.Warn($"ECG 波形分块落盘失败：{ex.GetType().Name} {ex.Message}");
                }
            });
        }

        /// <summary>两路视图重建：贴着尾部的窗口跟着最新序号走，被用户拉过的视图保持原位只补新点（轴键改完也走这里）</summary>
        internal void PumpViews()
        {
            var rate = _service.SampleRateHz;
            var last = _firstIndex + _raw.Count;

            foreach (var channel in Channels)
            {
                // 轴限被图表自己改过（滚轮缩放或拖动）就不再强行跟随，否则用户永远拉不动波形
                if (channel.IsViewDrifted()) channel.IsFollowing = false;

                var window = channel.WindowSeconds * rate;
                var following = channel.IsFollowing;
                var vxMax = following ? last : channel.XAxes[0].MaxLimit ?? last;
                var vxMin = following ? last - window : channel.XAxes[0].MinLimit ?? (last - window);

                channel.BuildDisplay(_raw, _firstIndex, vxMin, vxMax);
                if (following) channel.ApplyFollowWindow(vxMin, vxMax);
            }

            OnPropertyChanged(nameof(SummaryText));
        }

        private void OnServiceStateChanged()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            if (dispatcher.CheckAccess()) RefreshState();
            else dispatcher.BeginInvoke(new Action(RefreshState));
        }

        private void RefreshState()
        {
            // 采集起来就按周期补数据，停下来且队列排空才收定时器：读线程不碰 DispatcherTimer
            if (_service.IsCapturing) _drainTimer.Start();

            OnPropertyChanged(nameof(PortText));
            OnPropertyChanged(nameof(IsPortBusy));
            OnPropertyChanged(nameof(IsCapturing));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(CsvText));
            RequeryCommands();
        }
    }
}
