using TORNPISolution.Interface;

namespace TORNPISolution.Service.TestModules
{
    /// <summary>
    /// NFC 功能测试单项结果
    /// </summary>
    public class NfcTestItemResult
    {
        /// <summary>测试项名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>是否通过</summary>
        public bool Pass { get; set; }

        /// <summary>实测值/内容描述</summary>
        public string Actual { get; set; } = string.Empty;

        /// <summary>备注（失败原因等）</summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// NFC 功能测试：
    /// 测试内容为 MAC 信息写入和验证 ——
    /// 使用 NFC 读卡器读取设备上的 NFC 标签，将 MAC 地址写入标签用户区，
    /// 再回读标签内容进行比对验证。
    ///
    /// 标签约定：NTAG 系列（每页 4 字节），用户区从第 4 页开始，
    /// MAC（6 字节）占用 2 页（补 0 到 8 字节）。
    /// </summary>
    public class NfcFunctionTest
    {
        /// <summary>MAC 写入起始页（NTAG 用户区起点）</summary>
        public const byte MacStartPage = 4;

        /// <summary>MAC 占用页数（6 字节补零到 8 字节 = 2 页）</summary>
        public const byte MacPageCount = 2;

        private readonly INfcReader _reader;
        private readonly ILogService _log;

        public NfcFunctionTest(INfcReader reader, ILogService? log = null)
        {
            _reader = reader;
            _log = log ?? new LogService(nameof(NfcFunctionTest));
        }

        /// <summary>
        /// 执行完整 NFC 功能测试流程：
        /// 1. 寻卡读取 UID（确认标签在场）
        /// 2. MAC 信息写入标签用户区
        /// 3. 回读比对，验证 MAC 写入正确性
        /// 4. 读取标签内容输出（测试步骤：查看标签内容）
        /// </summary>
        /// <param name="macAddress">要写入的 MAC 地址（6 字节）</param>
        public async Task<List<NfcTestItemResult>> RunAsync(byte[] macAddress, CancellationToken ct = default)
        {
            _log.Info($"{DateTime.Now} Start calling the NfcFunctionTest.RunAsync() method . {Environment.NewLine}");
            try
            {
                if (macAddress == null || macAddress.Length != 6)
                    throw new ArgumentException("MAC 地址必须为 6 字节", nameof(macAddress));

                var results = new List<NfcTestItemResult>();

            // ---- 步骤 1：读取 NFC 标签（寻卡取 UID） ----
            byte[] uid;
            try
            {
                uid = await _reader.GetUidAsync(ct).ConfigureAwait(false);
                results.Add(new NfcTestItemResult
                {
                    Name = SpecItems.NfcTagDetect,
                    Pass = uid.Length > 0,
                    Actual = ToHex(uid),
                    Message = uid.Length > 0 ? "读取到 NFC 标签" : "未读取到标签，请确认标签放置到位"
                });
                if (uid.Length == 0) return results;
            }
            catch (Exception ex)
            {
                _log.Error("NFC 测试：寻卡失败", ex);
                results.Add(new NfcTestItemResult { Name = SpecItems.NfcTagDetect, Pass = false, Message = ex.Message });
                return results;
            }

            // ---- 步骤 2：MAC 信息写入 ----
            try
            {
                await _reader.WritePagesAsync(MacStartPage, macAddress, ct).ConfigureAwait(false);
                results.Add(new NfcTestItemResult
                {
                    Name = SpecItems.NfcMacWrite,
                    Pass = true,
                    Actual = ToHex(macAddress),
                    Message = $"已写入第 {MacStartPage}~{MacStartPage + MacPageCount - 1} 页"
                });
            }
            catch (Exception ex)
            {
                _log.Error("NFC 测试：MAC 写入失败", ex);
                results.Add(new NfcTestItemResult { Name = SpecItems.NfcMacWrite, Pass = false, Message = ex.Message });
                return results;
            }

            // ---- 步骤 3：MAC 信息验证（回读比对） ----
            try
            {
                var readback = await _reader.ReadPagesAsync(MacStartPage, MacPageCount, ct).ConfigureAwait(false);
                var match = readback.Length >= macAddress.Length
                    && readback.AsSpan(0, macAddress.Length).SequenceEqual(macAddress);

                results.Add(new NfcTestItemResult
                {
                    Name = SpecItems.NfcMacVerify,
                    Pass = match,
                    Actual = ToHex(readback),
                    Message = match ? "回读内容与写入 MAC 一致" : "回读内容与写入 MAC 不一致"
                });
            }
            catch (Exception ex)
            {
                _log.Error("NFC 测试：MAC 回读验证失败", ex);
                results.Add(new NfcTestItemResult { Name = SpecItems.NfcMacVerify, Pass = false, Message = ex.Message });
            }

            // ---- 步骤 4：读取标签内容（测试步骤：查看标签内容） ----
                try
                {
                    var content = await ReadTagContentAsync(MacStartPage, 8, ct).ConfigureAwait(false);
                    results.Add(new NfcTestItemResult
                    {
                        Name = SpecItems.NfcTagContentRead,
                        Pass = content.Length > 0,
                        Actual = content,
                        Message = "标签用户区内容"
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new NfcTestItemResult { Name = SpecItems.NfcTagContentRead, Pass = false, Message = ex.Message });
                }

                return results;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in NfcFunctionTest.class RunAsync method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the NfcFunctionTest.RunAsync() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// 读取标签内容并转为十六进制 + ASCII 双栏文本，供界面展示。
        /// </summary>
        /// <param name="startPage">起始页</param>
        /// <param name="pageCount">页数</param>
        public async Task<string> ReadTagContentAsync(byte startPage, byte pageCount, CancellationToken ct = default)
        {
            var data = await _reader.ReadPagesAsync(startPage, pageCount, ct).ConfigureAwait(false);
            if (data.Length == 0) return string.Empty;

            var hex = BitConverter.ToString(data).Replace("-", " ");
            var ascii = new string(data.Select(b => b >= 0x20 && b <= 0x7E ? (char)b : '.').ToArray());
            var text = $"HEX: {hex}{Environment.NewLine}ASCII: {ascii}";
            _log.Info($"NFC 标签内容（{startPage} 页起 {pageCount} 页）：{text}");
            return text;
        }

        private static string ToHex(byte[] data) =>
            data.Length == 0 ? "(空)" : BitConverter.ToString(data).Replace("-", " ");
    }
}
