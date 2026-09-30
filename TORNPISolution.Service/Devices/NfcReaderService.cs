using System.Linq.Expressions;
using System.Reflection;
using TORNPISolution.Interface;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// THS_HF NFC 读卡器控制类（岩柱 YZ806/YZ906 高频读写器）。
    /// 运行时反射加载厂商 Reader_DLLV1.dll（.NET Framework 托管库，已修补 PE 头为 AnyCPU 以在 x64/.NET 8 加载），
    /// 调用其 Reader.ReaderMethod 完成串口连接与 NTAG 标签读写，无需自行拼协议帧。
    /// 依赖：DLL 须在输出目录（csproj 已配自动复制）+ System.IO.Ports（Service 已引用）。
    /// </summary>
    public class NfcReaderService : INfcReader
    {
        private const string DllFileName = "Reader_DLLV1.dll";
        private const string ReaderTypeName = "Reader.ReaderMethod";
        private const int DefaultTimeoutMs = 2000;

        /// <summary>读写器地址（THS 协议默认广播地址 0xFF）</summary>
        private const byte ReaderId = 0xFF;

        private readonly ILogService _log;
        private readonly SemaphoreSlim _commandLock = new(1, 1);

        private object? _reader;                 // Reader.ReaderMethod 实例
        private Delegate? _analyCallbackHolder;  // 保持委托引用防止 GC
        private Delegate? _recvCallbackHolder;

        // 请求-响应配对：发送指令后等待下一帧解析结果
        private TaskCompletionSource<object?>? _pendingFrame;

        public NfcReaderService(ILogService? log = null)
        {
            _log = log ?? new LogService(nameof(NfcReaderService));
        }

        public bool IsOpen { get; private set; }

        public Task OpenAsync(string portName, int baudRate, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (IsOpen) return;

                // 1. 加载厂商 DLL（与 exe 同目录）
                var dllPath = Path.Combine(AppContext.BaseDirectory, DllFileName);
                if (!File.Exists(dllPath))
                    throw new FileNotFoundException($"未找到 NFC 读卡器库：{dllPath}");

                var asm = Assembly.LoadFrom(dllPath);
                var readerType = asm.GetType(ReaderTypeName)
                    ?? throw new InvalidOperationException($"DLL 中未找到类型 {ReaderTypeName}");

                _reader = Activator.CreateInstance(readerType)
                    ?? throw new InvalidOperationException("创建 ReaderMethod 实例失败");

                // 2. 挂接解析回调（AnalyCallback：void(MessageTran)）与原始数据回调
                HookCallback(readerType, "AnalyCallback", OnFrameParsed, out _analyCallbackHolder);
                HookCallback(readerType, "ReceiveCallback", OnRawReceived, out _recvCallbackHolder);

                // 3. 打开串口：int OpenCom(string port, int baud, out string err)
                dynamic rd = _reader;
                int ret = rd.OpenCom(portName, baudRate, out string err);
                if (ret < 0 || !string.IsNullOrEmpty(err))
                {
                    var msg = $"NFC 读卡器打开 {portName}@{baudRate} 失败：{err}";
                    _log.Error(msg);
                    throw new IOException(msg);
                }

                IsOpen = true;
                _log.Info($"NFC 读卡器已连接：{portName}@{baudRate}");
            }, ct);
        }

        public Task CloseAsync()
        {
            return Task.Run(() =>
            {
                if (_reader == null || !IsOpen) return;
                try
                {
                    dynamic rd = _reader;
                    rd.CloseCom();
                }
                catch (Exception ex)
                {
                    _log.Warn($"NFC 读卡器关闭异常：{ex.Message}");
                }
                IsOpen = false;
                _log.Info("NFC 读卡器已断开");
            });
        }

        /// <summary>读取固件版本（验证读卡器通讯链路）</summary>
        public async Task<string> GetFirmwareVersionAsync(CancellationToken ct = default)
        {
            EnsureOpen();
            var frame = await SendAndWaitAsync(
                rd => rd.GetFirmwareVersion(ReaderId), "GetFirmwareVersion", ct).ConfigureAwait(false);

            var data = ExtractData(frame);
            var version = data.Length > 0 ? BitConverter.ToString(data).Replace("-", " ") : "无返回";
            _log.Info($"NFC 读卡器固件版本：{version}");
            return version;
        }

        /// <summary>读取 NTAG 标签连续页（快速读指令，返回 pageCount*4 字节）</summary>
        public async Task<byte[]> ReadPagesAsync(byte startPage, byte pageCount, CancellationToken ct = default)
        {
            EnsureOpen();
            if (pageCount == 0) return Array.Empty<byte>();

            byte endPage = (byte)(startPage + pageCount - 1);
            var frame = await SendAndWaitAsync(
                rd => rd.CMD_MF_NTAGFASTPageRead(ReaderId, (byte)0, startPage, endPage),
                $"NTAG 快速读页 [{startPage}..{endPage}]", ct).ConfigureAwait(false);

            var data = ExtractData(frame);
            _log.Info($"NFC 读页成功：起始页 {startPage}，共 {pageCount} 页，{data.Length} 字节：{ToHex(data)}");
            return data;
        }

        /// <summary>写入 NTAG 标签连续页（数据不足 4 的倍数自动补 0）</summary>
        public async Task WritePagesAsync(byte startPage, byte[] data, CancellationToken ct = default)
        {
            EnsureOpen();
            if (data.Length == 0) return;

            // NTAG 每页 4 字节，补齐到整页
            var pageCount = (byte)((data.Length + 3) / 4);
            var pagedata = new byte[pageCount * 4];
            Array.Copy(data, pagedata, data.Length);

            var frame = await SendAndWaitAsync(
                rd => rd.CMD_MF_NTAGPageWrite(ReaderId, (byte)0, startPage, pageCount, pagedata),
                $"NTAG 写页 [{startPage}] {data.Length} 字节", ct).ConfigureAwait(false);

            _log.Info($"NFC 写页成功：起始页 {startPage}，{data.Length} 字节：{ToHex(data)}");
            await Task.CompletedTask;
            _ = frame; // 写指令应答帧仅用于确认超时内收到响应
        }

        /// <summary>读取标签 UID（ISO14443A 寻卡获取卡号）</summary>
        public async Task<byte[]> GetUidAsync(CancellationToken ct = default)
        {
            EnsureOpen();
            var frame = await SendAndWaitAsync(
                rd => rd.CMD_ISO14443A_GET_SNR(ReaderId, (byte)1, (byte)0),
                "ISO14443A 寻卡取 UID", ct).ConfigureAwait(false);

            var uid = ExtractData(frame);
            _log.Info($"NFC 标签 UID：{ToHex(uid)}");
            return uid;
        }

        public void Dispose()
        {
            try
            {
                if (_reader != null && IsOpen)
                {
                    dynamic rd = _reader;
                    rd.CloseCom();
                    IsOpen = false;
                }
            }
            catch { /* 释放时忽略 */ }
            _commandLock.Dispose();
            GC.SuppressFinalize(this);
        }

        #region 内部实现

        private void EnsureOpen()
        {
            if (_reader == null || !IsOpen)
                throw new InvalidOperationException("NFC 读卡器未连接，请先打开串口");
        }

        /// <summary>
        /// 发送指令并等待读卡器应答帧（串行化，防止指令交叉）。
        /// </summary>
        private async Task<object?> SendAndWaitAsync(Func<dynamic, int> send, string description, CancellationToken ct)
        {
            await _commandLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _pendingFrame = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

                dynamic rd = _reader!;
                int ret = send(rd);
                if (ret < 0)
                    throw new IOException($"NFC 指令 [{description}] 发送失败（返回 {ret}）");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(DefaultTimeoutMs);

                var completed = await Task.WhenAny(_pendingFrame.Task, Task.Delay(Timeout.Infinite, cts.Token))
                    .ConfigureAwait(false);
                if (completed != _pendingFrame.Task)
                    throw new TimeoutException($"NFC 指令 [{description}] 等待应答超时（{DefaultTimeoutMs}ms）");

                return await _pendingFrame.Task.ConfigureAwait(false);
            }
            finally
            {
                _pendingFrame = null;
                _commandLock.Release();
            }
        }

        /// <summary>
        /// 给厂商类的回调字段（AnalyCallback / ReceiveCallback）挂接处理函数。
        /// 委托参数类型（MessageTran 等）编译期不可见，
        /// 用表达式树构造与字段类型一致的委托，转发到 Action&lt;object?&gt;。
        /// </summary>
        private void HookCallback(Type readerType, string fieldName, Action<object?> handler, out Delegate holder)
        {
            var field = readerType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
            {
                _log.Warn($"ReaderMethod 未找到回调字段 {fieldName}，跳过挂接");
                holder = null!;
                return;
            }

            var delegateType = field.FieldType;
            var invoke = delegateType.GetMethod("Invoke")!;
            var param = invoke.GetParameters()[0].ParameterType;

            var paramExpr = Expression.Parameter(param, "e");
            var handlerExpr = Expression.Constant(handler);
            var body = Expression.Call(
                handlerExpr,
                typeof(Action<object?>).GetMethod("Invoke")!,
                Expression.Convert(paramExpr, typeof(object)));
            holder = Expression.Lambda(delegateType, body, paramExpr).Compile();
            field.SetValue(_reader, holder);
        }

        /// <summary>解析回调：收到一帧已解析的应答（MessageTran）</summary>
        private void OnFrameParsed(object? msgTran)
        {
            _pendingFrame?.TrySetResult(msgTran);
        }

        /// <summary>原始数据回调（仅记录日志，便于现场调试）</summary>
        private void OnRawReceived(object? raw)
        {
            if (raw is byte[] bytes && bytes.Length > 0)
                _log.Debug($"NFC 原始帧：{ToHex(bytes)}");
        }

        /// <summary>从 MessageTran 帧对象提取数据段（AryData 属性）</summary>
        private static byte[] ExtractData(object? frame)
        {
            if (frame == null) return Array.Empty<byte>();
            try
            {
                dynamic mt = frame;
                byte[]? data = mt.AryData;
                return data ?? Array.Empty<byte>();
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private static string ToHex(byte[] data) =>
            data.Length == 0 ? "(空)" : BitConverter.ToString(data).Replace("-", " ");

        #endregion
    }
}
