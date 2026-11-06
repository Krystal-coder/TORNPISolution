using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 加速度充电检查的运行层：常驻持有产品（DUT）串口，管采集启停、上报间隔、姿态标签换档与 CSV 落盘。
    /// 串口参数取仪器配置里的被测产品段，本层不再单独配口；同一个 COM 口只能有一个持有者，连着时整机/单板不得开方案。
    /// 进入产测在连接、退出产测只在断开：停传感器与离开本页都不退产测态、不交出串口。
    /// </summary>
    public sealed class AccelCheckService : IDisposable
    {
        private const int DefaultIntervalMs = 1_000;

        /// <summary>判定角度容差默认值（°，临时值，后续按 SOP 调整）</summary>
        private const double DefaultToleranceDegrees = 3;

        private readonly IInstrumentConfigService _configService;
        private readonly ILogService _log = new LogService(nameof(AccelCheckService));
        private readonly AccelCheckCsvWriter _csv = new();
        private readonly AccelRollTrace _rollTrace = new();
        private readonly object _gate = new();
        private readonly object _csvLock = new();
        private readonly object _reportLock = new();

        /// <summary>还没渲染完的报告：退出时给它一点宽限，否则「断开即退出」会把刚起渲的那份掐掉</summary>
        private readonly List<Task> _pendingReports = new();

        private ProductStreamController? _channel;

        /// <summary>连接进行中：打开串口改到线程池后界面不再定住，这段时间口既没连上也还没交出去</summary>
        private int _connecting;

        /// <summary>断开进行中：停流、退产测、收尾 CSV、交口要 5 秒上下，这期间再点一次只会把这些重发一遍</summary>
        private int _disconnecting;

        /// <summary>落盘暂停中：摆姿态那一段的动数据不进 CSV，读线程每条采样都要问一次</summary>
        private int _savePaused;

        /// <summary>界面段内序号（换一次姿态标签从 1 重数）；CSV 的序号由落盘类自己数，暂停保存期间两边会错开</summary>
        private int _seq;
        private uint _lastDeviceSeq;
        private bool _hasDeviceSeq;
        private long _lastSampleTicks;
        private double _intervalEmaMs;

        /// <summary>各姿态标签示教出来的名义角（进程内，换方案不带走）：一帧 Roll 就有 ±1.5° 抖动，置零取本档均值</summary>
        private readonly Dictionary<string, AccelNominal> _taught = new();
        private double _tagRollSum;
        private double _tagPitchSum;
        private int _tagSampleCount;

        public AccelCheckService(IInstrumentConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>判定完成的一条采样（在读线程上触发，订阅方只许入队）</summary>
        public event Action<AccelCheckRecord>? SampleJudged;

        /// <summary>连接、采集、保存等状态变化（后台线程触发，界面侧自己回 UI 线程）</summary>
        public event Action? StateChanged;

        /// <summary>这一轮两份报告都渲染完了，给两文件全路径（没出来的那项为 null）；在后台线程触发，界面侧自己回 UI 线程</summary>
        public event Action<string?, string?>? ReportsGenerated;

        public bool IsConnected { get; private set; }

        /// <summary>
        /// 产测态记账（进程内）：进入拿到应答置真、退出拿到应答置假，产品真值拿不到，只用来决定按钮写「进入」还是「退出」。
        /// 换产品、产品断电后这个数会失真，以界面回执与日志为准。
        /// </summary>
        public bool InProductionMode { get; private set; }

        /// <summary>这个 COM 口是否还归这一路：连着就归，只有「断开」才交，主界面据此锁返回键、整机/单板开新一轮前先看它</summary>
        public bool IsPortBusy => IsConnected || Volatile.Read(ref _connecting) > 0 || Volatile.Read(ref _disconnecting) > 0;

        public bool IsSensorRunning { get; private set; }

        /// <summary>断开收尾是否正在进行：界面据此置灰断开键，二次点击不再重发停流与退产测</summary>
        public bool IsDisconnecting => Volatile.Read(ref _disconnecting) > 0;

        public bool IsSaving => _csv.IsSaving;

        /// <summary>已开文件但暂停写入：文件不关、段不结，只是这段采样不落盘</summary>
        public bool IsSavePaused => Volatile.Read(ref _savePaused) > 0;

        /// <summary>串口参数摘要（只读自仪器配置）</summary>
        public string PortSummary { get; private set; } = "未连接";

        public string CsvFile => _csv.CurrentFile ?? string.Empty;

        /// <summary>Roll 曲线缓冲（界面展开曲线时按它取数）：与表格同源，暂停保存只是不落盘，曲线照记</summary>
        public AccelRollTrace RollTrace => _rollTrace;

        /// <summary>姿态标签：既是 CSV 的一列，也是判定偏差的名义角来源</summary>
        public string AngleTag { get; private set; } = string.Empty;

        public double ToleranceDegrees { get; private set; } = DefaultToleranceDegrees;

        public string CompareAxis { get; private set; } = AccelAngleJudge.AxisBoth;

        public int IntervalMs { get; private set; } = DefaultIntervalMs;

        /// <summary>本次连接累计收到的采样帧数</summary>
        public long FrameCount { get; private set; }

        /// <summary>按产品序号算出的累计丢帧数</summary>
        public long DroppedFrames { get; private set; }

        /// <summary>实测帧间隔（ms，滑动均值）：0x62 的单位未经实机证实，靠它回显真实节拍</summary>
        public double MeasuredIntervalMs => Math.Round(_intervalEmaMs, 1);

        public BatteryReading? Battery { get; private set; }

        /// <summary>充电状态（0x6D：1=充电中，0=未充电；null=还没读到）</summary>
        public byte? Charging { get; private set; }

        // ==================== 连接 ====================

        /// <summary>
        /// 按仪器配置里的产品串口参数建立常驻通道，并进入产测模式 + 关掉传感器（工装一对一，接上就归这一路）。
        /// 产品在上口后 20 秒内收不到进入产测就不在这个态，之后不再补发（产测态一直保留到退出，无需心跳）。
        /// </summary>
        /// <returns>给操作人员看的一句话回执：产品没应答进入产测时点明下一步</returns>
        public async Task<string> ConnectAsync(CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} Start calling the AccelCheckService.ConnectAsync() method . {Environment.NewLine}");
            var claimed = false;
            try
            {
                if (IsConnected) return string.Empty;
                // 界面不再定住，二次点击会在同一个口上开两遍；口正在被这一路拿走时整机/单板那一轮也要等
                if (Interlocked.CompareExchange(ref _connecting, 1, 0) != 0)
                    return "这一路正在连接（最多 5 秒），等它给回执再点";
                claimed = true;

                var settings = _configService.Load().Product;
                var channel = new ProductStreamController(settings);
                channel.AccelSampleReceived += OnAccelSample;
                // 限时打开最多等 5 秒（端口被占或 USB 枚举慢时真会等满）：这一等不能趴在 UI 线程上，否则点「连接」界面先定住
                await Task.Run(channel.Open).ConfigureAwait(false);

                lock (_gate)
                {
                    _channel = channel;
                    IsConnected = true;
                    PortSummary = $"{settings.PortName} @ {settings.BaudRate}";
                    ResetCounters();
                }

                var enter = await channel.RequestAsync(ProductProtocol.EnterProductionTestMode, "进入产测",
                                        timeoutMs: 2000, ct: ct).ConfigureAwait(false);
                if (enter != null) InProductionMode = true;   // 与手动补发同口径：产品认账才改记账

                // 建立时先关一次传感器：上一轮异常退出可能把产品留在推流态，火发不等应答（与独立工装程序的做法一致）
                await channel.SendAsync(ProductProtocol.StopAccelSensor, "关闭加速度传感器(建立清场)").ConfigureAwait(false);

                _log.Info($"加速度充电检查已连接 {PortSummary}，进入产测{(enter == null ? "无应答" : "有应答")}");
                StateChanged?.Invoke();
                return enter == null
                    ? "串口已连上，但产品没应答进入产测：断开重连一次，或在「配置 → 被测产品」点「进入产测」补发（上口后 20 秒内是窗口）"
                    : string.Empty;
            }
            finally
            {
                if (claimed) Interlocked.Exchange(ref _connecting, 0);
                _log.Info($"{DateTime.Now} Ended calling the AccelCheckService.ConnectAsync() method . {Environment.NewLine}");
            }
        }

        public async Task DisconnectAsync()
        {
            _log.Info($"{DateTime.Now} Start calling the AccelCheckService.DisconnectAsync() method . {Environment.NewLine}");
            var claimed = Interlocked.CompareExchange(ref _disconnecting, 1, 0) == 0;
            try
            {
                if (!claimed)
                {
                    _log.Info("断开已在收尾中，这一路不再重复收尾");
                    return;
                }

                var channel = _channel;
                try
                {
                    await StopSensorAsync().ConfigureAwait(false);

                    // 退出产测压在断开这一下：等 1 秒拿个确认就够，发不出去也只记账，不能挡住把口交出去
                    if (channel != null)
                    {
                        var exit = await channel.RequestAsync(ProductProtocol.ExitProductionTestMode, "退出产测",
                                                              timeoutMs: 1000).ConfigureAwait(false);
                        if (exit != null) InProductionMode = false;   // 没应答时不改记账：到底退没退只有产品说了算
                        _log.Info(exit == null ? "退出产测无应答：产品可能仍在产测态" : "产品已退出产测态");
                    }

                    StopSaving();
                }
                catch (Exception ex)
                {
                    _log.Warn($"断开前收尾异常，仍继续交出口：{ex.GetType().Name} {ex.Message}");
                }
                finally
                {
                    ProductStreamController? owned;
                    lock (_gate)
                    {
                        owned = _channel;
                        _channel = null;
                        IsConnected = false;
                        PortSummary = "未连接";
                    }

                    if (owned != null)
                    {
                        owned.AccelSampleReceived -= OnAccelSample;
                        owned.Close();
                        owned.Dispose();
                    }
                }

                _log.Info("加速度充电检查已断开产品串口");
                StateChanged?.Invoke();
            }
            finally
            {
                if (claimed) Interlocked.Exchange(ref _disconnecting, 0);
                _log.Info($"{DateTime.Now} Ended calling the AccelCheckService.DisconnectAsync() method . {Environment.NewLine}");
            }
        }

        // ==================== 采集 ====================

        /// <summary>启动加速度传感器：以控制码 0x60 的任一回来帧</summary>
        public async Task<bool> StartSensorAsync()
        {
            _log.Info($"{DateTime.Now} Start calling the AccelCheckService.StartSensorAsync() method . {Environment.NewLine}");
            try
            {
                var channel = _channel;
                if (channel == null) throw new InvalidOperationException("产品串口未连接");

                var resp = await channel.RequestAsync(ProductProtocol.StartAccelSensor, "启动加速度传感器")
                                        .ConfigureAwait(false);
                IsSensorRunning = resp != null;
                if (resp == null) _log.Warn("启动加速度传感器无应答：界面按未启动处理，可再点一次");
                else _log.Info("加速度传感器已启动，开始收采样帧");

                StateChanged?.Invoke();
                return resp != null;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the AccelCheckService.StartSensorAsync() method . {Environment.NewLine}");
            }
        }

        public async Task StopSensorAsync()
        {
            _log.Info($"{DateTime.Now} Start calling the AccelCheckService.StopSensorAsync() method . {Environment.NewLine}");
            try
            {
                IsSensorRunning = false;
                var channel = _channel;
                if (channel == null) return;

                var resp = await channel.RequestAsync(ProductProtocol.StopAccelSensor, "关闭加速度传感器").ConfigureAwait(false);
                if (resp == null) _log.Warn("关闭加速度传感器无应答：产品可能还在推采样");
                else _log.Info("加速度传感器已关闭");
                StateChanged?.Invoke();
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the AccelCheckService.StopSensorAsync() method . {Environment.NewLine}");
            }
        }

        /// <summary>设上报间隔（0x62 + uint16 LE，单位与整机 ECG 用的采样率命令同源，实机以回显的实测间隔为准）</summary>
        public async Task<bool> ApplyIntervalAsync(int intervalMs)
        {
            _log.Info($"{DateTime.Now} Start calling the AccelCheckService.ApplyIntervalAsync() method . {Environment.NewLine}");
            try
            {
                var clamped = Math.Clamp(intervalMs, 1, ushort.MaxValue);
                var channel = _channel;
                if (channel == null)
                {
                    _log.Warn("产品串口未连接，上报间隔不下发");
                    return false;
                }

                var data = new[] { (byte)(clamped & 0xFF), (byte)((clamped >> 8) & 0xFF) };
                var resp = await channel.RequestAsync(ProductProtocol.SetSampleRate, "设置上报间隔", data)
                                        .ConfigureAwait(false);
                if (resp != null) IntervalMs = clamped;

                StateChanged?.Invoke();
                return resp != null;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the AccelCheckService.ApplyIntervalAsync() method . {Environment.NewLine}");
            }
        }

        /// <summary>读电池（0x6C）与充电状态（0x6D）：充电检查要看的就是这两项，读不到时保留上一次的值并由界面提示</summary>
        public async Task QueryChargingAsync()
        {
            _log.Info($"{DateTime.Now} Start calling the AccelCheckService.QueryChargingAsync() method . {Environment.NewLine}");
            try
            {
                var channel = _channel;
                if (channel == null) return;

                var battery = await channel.RequestAsync(ProductProtocol.GetBatteryInfo, "读电池信息")
                                           .ConfigureAwait(false);
                if (battery is { Payload.Length: >= 5 })
                    Battery = new BatteryReading(ProductProtocol.ReadUInt32LE(battery.Payload, 0),
                                                 battery.Payload[4], battery.Payload);

                var charging = await channel.RequestAsync(ProductProtocol.GetChargingStatus, "读充电状态")
                                            .ConfigureAwait(false);
                if (charging is { Payload.Length: >= 1 }) Charging = charging.Payload[0];

                StateChanged?.Invoke();
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the AccelCheckService.QueryChargingAsync() method . {Environment.NewLine}");
            }
        }

        // ==================== 产测模式（仪器配置页那枚按钮走这里）====================

        /// <summary>
        /// 手动补发进入/退出产测（用串口助手手动联调、产品没应答时用）：临时开一条通道发一帧、拿到应答就关，
        /// 不抢本页常驻口，也不走整机那套 ProductController。
        /// </summary>
        /// <returns>给操作人员看的一句话结果</returns>
        public async Task<string> ApplyProductionModeAsync(bool enter)
        {
            _log.Info($"{DateTime.Now} Start calling the AccelCheckService.ApplyProductionModeAsync() method . {Environment.NewLine}");
            var name = enter ? "进入产测" : "退出产测";
            try
            {
                if (IsPortBusy) return "这个串口正被加速度充电检查占着（或还在收尾），产测态由那边进出，这里不用再发";
                if (TestRunState.IsRunning) return "整机/单板那一轮正在用这个串口，等它跑完再" + name;

                var resp = await Task.Run(() => SendOneShotAsync(
                    enter ? ProductProtocol.EnterProductionTestMode : ProductProtocol.ExitProductionTestMode, name))
                    .ConfigureAwait(false);

                if (resp == null) return $"{name}失败：2 秒内产品没应答，可再点一次";

                // 只在产品认账时才改记账：没应答时到底在不在产测态谁也不知道，按钮维持原样让人重试
                InProductionMode = enter;
                return enter ? "产品已进入产测态（一直保留到收到退出）" : "产品已退出产测态";
            }
            catch (TimeoutException ex)
            {
                return $"{name}失败：{ex.Message}";
            }
            catch (UnauthorizedAccessException ex)
            {
                return $"{name}失败：串口被占用（{ex.Message}）";
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in AccelCheckService.class ApplyProductionModeAsync method, message: {ex}", ex);
                return $"{name}失败：{ex.Message}";
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the AccelCheckService.ApplyProductionModeAsync() method . {Environment.NewLine}");
            }
        }

        /// <summary>临时开一条通道发一帧等应答，用完即关：串口动作全在线程池上，不让 UI 线程碰 SerialPort</summary>
        private async Task<ProductProtocol.ParsedFrame?> SendOneShotAsync(ProductCmd cmd, string commandName)
        {
            var channel = new ProductStreamController(_configService.Load().Product);
            try
            {
                channel.Open();
                return await channel.RequestAsync(cmd, commandName, timeoutMs: 2000).ConfigureAwait(false);
            }
            finally
            {
                channel.Close();
                channel.Dispose();
            }
        }

        // ==================== 标签与保存 ====================

        /// <summary>判定角度容差（°）与判定轴（Roll / Pitch / Roll&amp;Pitch）改了不影响已采到的原始值，只重算超限</summary>
        public void SetJudgement(double toleranceDegrees, string axis)
        {
            ToleranceDegrees = toleranceDegrees;
            CompareAxis = axis;
        }

        /// <summary>
        /// 换姿态标签：标签文字真变了才把段内序号退回 1、正在保存时另起一段（上一段的平均/最大/最小结在段尾），仍写同一份 CSV。
        /// 文字没变就原地不动：按一次「切换」劈开同一段，会让界面那条连续曲线与报告的逐段统计对不上。
        /// </summary>
        /// <returns>真的换档为 true；标签文字与当前一致为 false</returns>
        public bool ChangeAngleTag(string tag)
        {
            _log.Info($"{DateTime.Now} Start calling the AccelCheckService.ChangeAngleTag() method . {Environment.NewLine}");
            try
            {
                lock (_gate)
                {
                    if (string.Equals(AngleTag, tag, StringComparison.Ordinal))
                    {
                        _log.Info($"姿态标签仍是「{tag}」，不另起一段，继续这一段记录");
                        return false;
                    }

                    AngleTag = tag;
                    _seq = 0;
                    _tagRollSum = 0;
                    _tagPitchSum = 0;
                    _tagSampleCount = 0;
                }

                _log.Info($"姿态标签已切到「{AngleTag}」，段内序号从 1 重新开始");
                if (!IsSaving) return true;
                lock (_csvLock)
                {
                    _csv.BeginBlock(AngleTag);
                }
                _log.Info($"已在 {CsvFile} 内另起一段：{AngleTag}");
                return true;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the AccelCheckService.ChangeAngleTag() method . {Environment.NewLine}");
            }
        }

        /// <summary>当前姿态标签已示教的名义角（没示教过为 null）</summary>
        public AccelNominal? Taught
        {
            get { lock (_gate) return TaughtFor(AngleTag); }
        }

        /// <summary>按标签取示教名义角：重算历史行时每行用自己的标签查，不能拿当前档的示教值套上去</summary>
        public AccelNominal? TaughtFor(string tag)
        {
            lock (_gate) return _taught.GetValueOrDefault(tag);
        }

        /// <summary>本档累计到的采样条数（置零取均值用，0 表示还没收到帧）</summary>
        public int TeachSampleCount
        {
            get { lock (_gate) return _tagSampleCount; }
        }

        /// <summary>把当前姿态标签的名义角置为本档实测均值（Roll/Pitch 各一个），两轴随后各自判定</summary>
        /// <returns>置零结果；本档还没有采样时返回 null</returns>
        public AccelNominal? Teach()
        {
            AccelNominal nominal;
            string tag;
            int count;

            lock (_gate)
            {
                if (_tagSampleCount == 0) return null;
                count = _tagSampleCount;
                nominal = new AccelNominal(_tagRollSum / count, _tagPitchSum / count);
                tag = AngleTag;
                _taught[tag] = nominal;
            }

            _log.Info($"姿态标签「{tag}」名义角已置零：Roll {nominal.Roll:F2}° / Pitch {nominal.Pitch:F2}°（{count} 条均值）");
            return nominal;
        }

        /// <summary>撤销当前标签的置零，退回按标签文本里的名义角只比一个轴</summary>
        public void ClearTeach()
        {
            string tag;
            lock (_gate)
            {
                tag = AngleTag;
                _taught.Remove(tag);
            }

            _log.Info($"姿态标签「{tag}」已撤销名义角置零，回到标签文本口径");
        }

        /// <summary>开始保存：落盘动作在锁内，日志与状态广播必须在锁外（读线程每条采样都要拿同一把锁，不能让磁盘 I/O 把它钉住）</summary>
        public string StartSaving()
        {
            if (string.IsNullOrWhiteSpace(AngleTag))
                ChangeAngleTag("未命名");

            string path;
            lock (_csvLock)
            {
                path = _csv.Start(AngleTag);
            }

            Volatile.Write(ref _savePaused, 0);
            _log.Info($"开始保存采样：{path}");
            StateChanged?.Invoke();
            return path;
        }

        /// <summary>
        /// 停止保存：这份逐帧 CSV 只当中转，从它出两份报告（PDF 报告 + 同名标红 xlsx），标红版落定即删 CSV，
        /// 最后只留 PDF 与 xlsx 两份。报告在后台渲染。
        /// </summary>
        /// <returns>这一轮的中转 CSV 全路径；这一段没有数据时为 null</returns>
        public string? StopSaving()
        {
            Volatile.Write(ref _savePaused, 0);
            string? data;
            lock (_csvLock)
            {
                if (!_csv.IsSaving) return null;
                data = _csv.CurrentFile;
                _csv.Close();
            }

            if (data != null) GenerateReports(data);
            StateChanged?.Invoke();
            return data;
        }

        /// <summary>暂停/继续落盘：文件不关、段不结，只是这段采样不写；摆好姿态再切标签、继续，两段之间就是这段空档</summary>
        public void SetSavePaused(bool paused)
        {
            Volatile.Write(ref _savePaused, paused ? 1 : 0);
            _log.Info($"采样落盘已{(paused ? "暂停" : "继续")}（姿态标签「{AngleTag}」，文件 {CsvFile}）");
            StateChanged?.Invoke();
        }

        // ==================== 报告 ====================

        /// <summary>报告渲染丢后台：一份采样固定出两份（PDF + 标红 xlsx），标红版落定就把中转 CSV 删掉，只留两份成品；回执要马上回界面，退出时的收尾预算也只有几秒</summary>
        private void GenerateReports(string dataPath)
        {
            var meta = CaptureMeta();
            var task = Task.Run(() =>
            {
                var pdf = AccelReportService.WritePdf(dataPath, meta);
                var workbook = AccelCheckExcelWriter.WriteFromCsv(dataPath);
                if (workbook != null)
                {
                    try
                    {
                        File.Delete(dataPath);
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"中转 CSV 没删掉（{dataPath}）：{ex.Message}");
                    }
                }
                ReportsGenerated?.Invoke(pdf, workbook);
            });

            lock (_reportLock)
            {
                _pendingReports.RemoveAll(t => t.IsCompleted);
                _pendingReports.Add(task);
            }
        }

        /// <summary>出报告时的口径快照：页头与「判定口径」按界面此刻的设置写</summary>
        private AccelReportMeta CaptureMeta() => new()
        {
            PortSummary = PortSummary,
            ToleranceDegrees = ToleranceDegrees,
            CompareAxis = CompareAxis,
        };

        // ==================== 采样入口 ====================

        /// <summary>读线程回调：算判定 → 进曲线 → 落 CSV → 上抛给界面排队，全程不碰 UI</summary>
        private void OnAccelSample(AccelSample sample)
        {
            int seq;
            string tag;
            string axis;
            double tolerance;
            AccelNominal? taught;

            lock (_gate)
            {
                seq = ++_seq;
                tag = AngleTag;
                axis = CompareAxis;
                tolerance = ToleranceDegrees;
                taught = _taught.GetValueOrDefault(tag);

                _tagRollSum += sample.RollDegrees;
                _tagPitchSum += sample.PitchDegrees;
                _tagSampleCount++;
            }

            TrackDeviceSeq(sample.DeviceSeq);
            TrackInterval(sample.ReceivedAt);

            FrameCount++;
            var record = AccelAngleJudge.Evaluate(seq, tag, sample, axis, tolerance, taught);
            _rollTrace.Append(tag, sample.ReceivedAt, sample.RollDegrees);

            lock (_csvLock)
            {
                // 暂停只掐落盘这一件事：界面照刷、序号照走，CSV 那边这段不占号
                if (!IsSavePaused) _csv.Write(record);
            }

            SampleJudged?.Invoke(record);
        }

        /// <summary>产品序号连续性：跳号即丢帧（序号是产品侧累计的，跨标签不重置），只累计总数；首帧与乱序不计</summary>
        private void TrackDeviceSeq(uint deviceSeq)
        {
            lock (_gate)
            {
                if (!_hasDeviceSeq)
                {
                    _hasDeviceSeq = true;
                    _lastDeviceSeq = deviceSeq;
                    return;
                }

                var gap = (long)deviceSeq - _lastDeviceSeq - 1;
                _lastDeviceSeq = deviceSeq;
                if (gap <= 0 || gap > 1000) return;

                DroppedFrames += gap;
            }
        }

        /// <summary>实测帧间隔（滑动均值）：0x62 的单位实机没坐实，靠这个数看真实的上传节拍</summary>
        private void TrackInterval(DateTime receivedAt)
        {
            lock (_gate)
            {
                var previous = _lastSampleTicks;
                _lastSampleTicks = receivedAt.Ticks;
                if (previous == 0) return;

                var deltaMs = (receivedAt.Ticks - previous) / TimeSpan.TicksPerMillisecond;
                if (deltaMs <= 0 || deltaMs > 60_000) return;

                _intervalEmaMs = _intervalEmaMs <= 0 ? deltaMs : _intervalEmaMs * 0.8 + deltaMs * 0.2;
            }
        }

        private void ResetCounters()
        {
            _seq = 0;
            _hasDeviceSeq = false;
            _lastSampleTicks = 0;
            _intervalEmaMs = 0;
            FrameCount = 0;
            DroppedFrames = 0;
            Battery = null;
            Charging = null;
            _rollTrace.Reset();
        }

        // ==================== 收尾 ====================

        /// <summary>关窗口前调用：限时收尾，超时只记账不拦退出</summary>
        public void StopAndAwaitWrapUp(int timeoutMs = 5_000)
        {
            if (IsConnected || IsSaving)
            {
                try
                {
                    Task.Run(() => DisconnectAsync()).Wait(timeoutMs);
                }
                catch (Exception ex)
                {
                    _log.Warn($"退出时加速度充电检查收尾未完成（{ex.GetType().Name} {ex.Message}），产品可能仍在产测态或持续推采样");
                }
            }

            AwaitPendingReports();
        }

        /// <summary>
        /// 退出时给后台那份报告一点宽限：PDF 是断开这一下才起渲的，全不等就会得到「报告没出来」。
        /// 等满就算了——宁可少一份报告，也不拖着退出。
        /// </summary>
        private void AwaitPendingReports(int timeoutMs = 1_500)
        {
            Task[] pending;
            lock (_reportLock)
            {
                _pendingReports.RemoveAll(t => t.IsCompleted);
                pending = _pendingReports.ToArray();
            }

            if (pending.Length == 0) return;

            try
            {
                Task.WaitAll(pending, timeoutMs);
            }
            catch (Exception ex)
            {
                _log.Warn($"退出时报告渲染未完成（{ex.GetType().Name} {ex.Message}）");
            }
        }

        public void Dispose() => StopAndAwaitWrapUp();
    }
}
