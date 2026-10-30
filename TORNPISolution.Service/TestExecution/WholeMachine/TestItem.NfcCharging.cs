using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Service.Hardware;
using TORNPISolution.Service.TestModules;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：A00 初始化仪器 / A02 NFC功能测试 / A15 充电测试。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>
        /// NFC 测试写入标签用的 MAC 地址（占位测试值，02 开头为本地管理地址）；TODO: 后续改为从产品本身 MAC 或测试系统下发获取，在此处替换即可。
        /// </summary>
        private static readonly byte[] TestMacAddress = { 0x02, 0x00, 0x00, 0xAA, 0xBB, 0xCC };

        /// <summary>
        /// 初始化仪器：固高IO模块等硬件初始化；成功写 1，失败/异常保持初始化的 0（规格 1~1，成功才 PASS）。
        /// </summary>
        //测试项目**************************---A00---**************************初始化仪器
        public async Task InitInstrumentsModule(TestItemResultCollector collector, CancellationToken token)
        {
            // GTN_Open 内部阻塞轮询，用 Task.Run 包裹避免卡 UI
            var rtn = await Task.Run(() => _hw.GenControl.GTN_Open(token), token);
            if (rtn != 0)
            {
                _log.Error($"固高IO模块初始化失败，错误码：{rtn}");
                return;
            }

            _log.Info("固高IO模块初始化成功");
            await collector.SetAsync(SpecItems.GenIoInit, TestItemResult.Of(1));
        }

        /// <summary>
        /// NFC功能测试（MAC 信息写入和验证）：寻卡 → 写 MAC → 回读验证 → 读标签内容一次性执行完整流程，返回全部测试项结果（1=PASS / 0=FAIL，按 Spec 静态规格 1~1 判定）。
        /// </summary>
        //测试项目**************************---A02---**************************NFC功能测试
        public async Task NfcFunctionModule(TestItemResultCollector collector, CancellationToken token)
        {
            var config = _configService.Load();
            using var reader = new NfcReaderService(_log);
            await reader.OpenAsync(config.NfcReader.PortName, config.NfcReader.BaudRate, token);

            var test = new NfcFunctionTest(reader, _log);
            var items = await test.RunAsync(TestMacAddress, token);
            foreach (var r in items)
            {
                await collector.SetAsync(r.Name, TestItemResult.Of(r.Pass ? 1 : 0));
            }
            _log.Info($"NFC功能测试完成，共 {items.Count} 项");
        }

        /// <summary>
        /// 充电测试（充电状态判断 / 当前电池电量测量 / 电池百分比获取）：通过产品串口协议读取充电状态（0x6D）和电池信息（0x6C：电压 uint32 LE + 百分比）。
        /// </summary>
        //测试项目**************************---A15---**************************充电测试
        public async Task ChargingTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            // 纯产品模块（0x6D/0x6C），不依赖任何仪器
            using var product = _productFactory();

            // 产品可用性以本轮 A16 产品通讯初始化记录为准（进入产测模式/0x72/0x74 均不再重发）；本模块仍需打开产品串口读充电状态 0x6D / 电池信息 0x6C
            if (!GetProductAvailable())
                _log.Warn("产品不可用（以 A16 产品通讯初始化记录为准）：充电测试项可能读回失败判 FAIL");
            try
            {
                await product.OpenAsync(token);
            }
            catch (Exception ex)
            {
                _log.Error($"充电测试：产品串口打开失败，跳过所有测试项（未报告项由收尾扫描判 FAIL）：{ex.Message}");
                return;
            }

            try
            {
                _log.Info("充电测试：发送充电状态查询（0x6D）...");
                var chargingStatus = await product.ReadChargingStatusAsync(token);
                await collector.SetAsync(SpecItems.ChargingStatus, TestItemResult.Of(chargingStatus));
                _log.Info($"充电状态判断：{chargingStatus}（1=充电中，0=未充电）");
            }
            catch (Exception ex)
            {
                _log.Error($"充电状态判断失败：{ex.Message}");
                await collector.SetAsync(SpecItems.ChargingStatus, TestItemResult.Of(0));
            }

            // 当前电池电量测量 + 电池百分比获取（0x6C，同一帧返回电压和百分比）
            try
            {
                _log.Info("充电测试：发送电池信息查询（0x6C）...");
                var battery = await product.ReadBatteryInfoAsync(token);
                if (battery != null)
                {
                    await collector.SetAsync(SpecItems.BatteryVoltage, TestItemResult.Of(battery.VoltageMv));
                    await collector.SetAsync(SpecItems.BatteryPercent, TestItemResult.Of(battery.Percentage));
                    _log.Info($"电池信息：电压={battery.VoltageMv}mV 百分比={battery.Percentage}%");
                }
                else
                {
                    _log.Warn("电池信息读取失败（产品无响应或 payload 不足）");
                    await collector.SetAsync(SpecItems.BatteryVoltage, TestItemResult.Of(0));
                    await collector.SetAsync(SpecItems.BatteryPercent, TestItemResult.Of(0));
                }
            }
            catch (Exception ex)
            {
                _log.Error($"电池信息读取失败：{ex.Message}");
                await collector.SetAsync(SpecItems.BatteryVoltage, TestItemResult.Of(0));
                await collector.SetAsync(SpecItems.BatteryPercent, TestItemResult.Of(0));
            }
        }
    }
}
