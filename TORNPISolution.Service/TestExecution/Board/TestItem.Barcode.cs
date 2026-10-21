using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：B02 产品条码获取（单板工位）。
    /// 一台读码器收 SlotsPerReader 条：触发一次取第一条，接着不再触发、只等相机把同一次触发的后续条推来；没收够就重新触发补扫，补扫时跳过本轮已经见过的文本（还摆在视野里的那件会被再推一遍）。
    /// 槽位号口径与目录构建同一个函数（<see cref="SpecItems.BoardBarcodeSlots"/>）。
    /// 逐台并发采集（每台是独立的触发/结果两条连接），采集结果串行上报（结果字典不是线程安全的）；某台连不上只判它自己那几个槽位 FAIL，不拦别的读码器。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>一台读码器最多触发几轮：首轮 + 补扫两轮，还收不齐就按收不齐判 FAIL</summary>
        private const int BarcodeTriggerRounds = 3;

        //测试项目**************************---B02---**************************产品条码获取（单板）
        public async Task BoardBarcodeModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("产品条码获取")) return;

            var config = _configService.Load();
            // 按本轮范围筛槽位：单板页右键“只跑通道 3”时只触发它对应的那台读码器
            var slots = WithinSlotScope(SpecItems.BoardBarcodeSlots(config), s => s.Slot);
            if (slots.Count == 0)
            {
                _log.Warn("产品条码获取：本轮要的槽位里没有可对上的已启用读码器（单板配置→工位仪器），不采集");
                return;
            }

            var groups = slots.GroupBy(s => s.ReaderIndex)
                              .Select(g => (ReaderIndex: g.Key, Count: g.Count()))
                              .ToList();
            var collected = await Task.WhenAll(groups.Select(g =>
                ReadBarcodeGroupAsync(g.ReaderIndex, config.BarcodeReaders[g.ReaderIndex], g.Count, token)))
                .ConfigureAwait(false);
            var byReader = collected.ToDictionary(c => c.ReaderIndex, c => c.Codes);

            // 上报串行：采集可以并发，写结果字典不行（Dictionary 并发写会炸，且逐项回调要按目录顺序回填）
            var got = 0;
            foreach (var (slot, readerIndex, indexInReader) in slots)
            {
                var codes = byReader.TryGetValue(readerIndex, out var arr) ? arr : Array.Empty<string?>();
                var code = indexInReader < codes.Length ? codes[indexInReader] : null;
                if (!string.IsNullOrWhiteSpace(code)) got++;

                await collector.SetAsync(SpecItems.BarcodeSlot(slot),
                    string.IsNullOrWhiteSpace(code)
                        ? TestItemResult.OfText(0, "未读到条码")
                        : TestItemResult.OfText(1, code));
                _log.Info($"产品条码-{slot}：{(string.IsNullOrWhiteSpace(code) ? "未读到" : code)}");
            }

            _log.Info($"产品条码获取完成：读到 {got}/{slots.Count} 条");
        }

        /// <summary>一台读码器收到够数或跑满轮数为止：触发一次拿一条，同一次触发里接着读后续条；任何异常都在本方法内收住（本台负责的槽位拿不到码即 FAIL），不让整模块抛。</summary>
        private async Task<(int ReaderIndex, string?[] Codes)> ReadBarcodeGroupAsync(
            int readerIndex, HikBarcodeReaderSettings settings, int expected, CancellationToken token)
        {
            var codes = new string?[expected];
            using IBarcodeReader reader = new HikBarcodeReader(settings, _log);
            try
            {
                await reader.OpenAsync(token).ConfigureAwait(false);

                var seen = new List<string>();
                for (var round = 0; round < BarcodeTriggerRounds && seen.Count < expected; round++)
                {
                    if (round > 0)
                        _log.Info($"{settings.Name} 只收到 {seen.Count}/{expected} 条，第 {round + 1} 轮重新触发补扫");

                    AddFresh(seen, await reader.ScanAsync(ct: token).ConfigureAwait(false));
                    // 同一次触发相机可能连着推几条：不再触发，接着等剩下的
                    for (var extra = 0; extra < expected && seen.Count < expected; extra++)
                        AddFresh(seen, await reader.ReadAsync(ct: token).ConfigureAwait(false));
                }

                // 按槽位顺序落位，多余的（补扫可能多收一条）丢掉，收不齐的槽位留着空上报「未读到条码」
                for (var i = 0; i < codes.Length && i < seen.Count; i++) codes[i] = seen[i];
                if (seen.Count < expected)
                    _log.Warn($"{settings.Name} 触发 {BarcodeTriggerRounds} 轮仍只收到 {seen.Count}/{expected} 条");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 建链失败多半是相机侧没开「TCP服务器端协议」或两个端口填错，把原因原样带进日志
                _log.Error($"{settings.Name} 采集条码失败，本台负责的槽位判 FAIL：{ex.Message}");
            }
            finally
            {
                try { await reader.CloseAsync().ConfigureAwait(false); }
                catch (Exception ex) { _log.Warn($"{settings.Name} 关闭连接异常：{ex.Message}"); }
            }

            return (readerIndex, codes);
        }

        /// <summary>收下这条码：空文本与本轮已经见过的都不要（补扫会把同一件的码再推一遍）</summary>
        private static void AddFresh(List<string> seen, string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            if (!seen.Contains(code, StringComparer.OrdinalIgnoreCase)) seen.Add(code);
        }
    }
}
