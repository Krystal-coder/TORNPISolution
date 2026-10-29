using TORNPISolution.Share.Enum;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// Spec：单板工位（Category=Board）
    /// </summary>
    public partial class Spec
    {
        /// <summary>单板工位目录：按书写顺序（B 前缀自己连号，与整机尾号无关）</summary>
        private IReadOnlyList<TestModuleModel> BuildBoardModules() => new List<TestModuleModel>
        {
            BuildBoardInitInstruments(),       // B01 初始化仪器（内容同 A00，各自独立）
            BuildBoardBarcodeRead(),           // B02 产品条码获取（逐台读码器取码，一项一槽位）
            BuildBoardBluetoothTest(),         // B03 蓝牙功能测试（逐台网关一项地址 + 一项信号强度）
            BuildBoardPowerOn(),               // B04 综测仪上电（逐台综测仪逐通道缓输出，一项一通道回读电压）
        };

        #region 模块清单（一个模块一段，按书写顺序）

        //======================================B01-----------初始化仪器（内容同整机 A00，各自独立）
        private static TestModuleModel BuildBoardInitInstruments()
        {
            var ti = AddTI(TestModuleIds.BoardInitInstruments, ModuleCategory.Board, "初始化仪器",
                "初始化固高IO等硬件，全部成功为 PASS");

            AddResult(ti, name: SpecItems.GenIoInit,
                description: "启动 EtherCAT 总线",
                specMin: 1, specMax: 1, unit: "");

            return ti;
        }

        //======================================B02-----------产品条码获取
        // 一项一槽位：启用的读码器按清单顺序每台占 SlotsPerReader 个槽位（与执行端同一口径：SpecItems.BoardBarcodeSlots）。
        private TestModuleModel BuildBoardBarcodeRead()
        {
            var slots = GetBarcodeSlots();
            var ti = AddTI(TestModuleIds.BoardBarcodeRead, ModuleCategory.Board, "产品条码获取",
                "逐台读码器触发取码，每个槽位扫到条码即 PASS");

            if (slots.Count == 0)
                _log.Warn("B02 目录构建：没有已启用的读码器，条码项清单为空（单板配置→工位仪器）");

            foreach (var (slot, _, _) in slots)
            {
                AddResult(ti, name: SpecItems.BarcodeSlot(slot),
                    description: "触发本槽位对应的读码器，扫到非空条码即 PASS（仪器列显示条码原文）",
                    specMin: 1, specMax: 1, unit: "", hideSpecRange: true);
            }

            return ti;
        }

        //======================================B03-----------蓝牙功能测试
        // 静态规格直接取配置里的阈值：目录每次重建，改阈值无需改代码（与 B02 同一口径：SpecItems.BoardBluetoothSlots）。
        // 地址识别的取法：该行填了 ExpectedMac 就按它匹配，没填则取识别窗口里信号最强的一条。
        private TestModuleModel BuildBoardBluetoothTest()
        {
            var config = LoadBoardConfig();
            var slots = SpecItems.BoardBluetoothSlots(config);
            var ti = AddTI(TestModuleIds.BoardBluetoothTest, ModuleCategory.Board, "蓝牙功能测试",
                "逐台网关先识别广播地址，再采信号强度取均值判定");

            if (slots.Count == 0)
                _log.Warn("B03 目录构建：没有已启用的蓝牙网关，填不上地址与强度项（单板配置→工位仪器）");

            foreach (var (slot, gateway) in slots)
            {
                var settings = config.BluetoothGateways[gateway];
                AddResult(ti, name: SpecItems.BluetoothMac(slot),
                    description: $"{settings.Name} 识别窗口（{settings.IdentifyWindowMs}ms）内扫到广播地址即 PASS",
                    specMin: 1, specMax: 1, unit: "", hideSpecRange: true);
                AddResult(ti, name: SpecItems.BluetoothRssi(slot),
                    description: $"{settings.Name} 采 {settings.RssiSampleCount} 条广播取均值，不低于 {settings.RssiMinDbm}dBm 判 PASS（条数/最差/最好写在仪器列）",
                    specMin: settings.RssiMinDbm, specMax: 0, unit: "dBm", valueSource: "Instrument");
            }

            return ti;
        }

        //======================================B04-----------综测仪上电
        // 一台已启用综测仪占四个硬件固定通道，一项一通道（与执行端同一口径：SpecItems.BoardPowerSlots）。
        // 上电时可选先 *RST 清掉残留与过流锁，回读走 po 通路；跑完只断网络连接、输出保持供电。
        private TestModuleModel BuildBoardPowerOn()
        {
            var config = LoadBoardConfig();
            var slots = SpecItems.BoardPowerSlots(config);
            var ti = AddTI(TestModuleIds.BoardPowerOn, ModuleCategory.Board, "综测仪上电",
                "逐台综测仪逐通道缓输出到设定电压，回读本通道电压与设定比对；后续模块要带电跑，本模块须排在它们前面");

            if (slots.Count == 0)
                _log.Warn("B04 目录构建：没有已启用的综测仪，上电项清单为空（仪器连接配置→综测仪）");

            foreach (var (slot, tester, channel) in slots)
            {
                var settings = config.Cry5242Testers[tester];
                var lower = Math.Max(0, settings.OutputMilliVolts - settings.VoltageToleranceMilliVolts);
                var upper = settings.OutputMilliVolts + settings.VoltageToleranceMilliVolts;
                AddResult(ti, name: SpecItems.PowerVoltage(slot),
                    description: $"{settings.Name} CH{channel} 缓输出 {settings.OutputMilliVolts}mV（斜率 {settings.RampMilliSeconds}ms、过流保护 {settings.ProtectionMilliAmps}mA），回读落在 {lower}~{upper}mV 判 PASS",
                    specMin: lower, specMax: upper, unit: "mV");
            }

            return ti;
        }

        #endregion

        #region 取配置与槽位辅助（上面各 Build 方法共用，不定义测试项）

        /// <summary>读配置算 B02 槽位清单；配置读不到就返回空清单（目录项为空，只记一条日志）</summary>
        private List<(int Slot, int ReaderIndex, int IndexInReader)> GetBarcodeSlots()
        {
            try
            {
                return SpecItems.BoardBarcodeSlots(_configService.Load()).ToList();
            }
            catch (Exception ex)
            {
                _log.Warn($"读取码器清单失败（B02 项清单为空）：{ex.Message}");
                return new List<(int Slot, int ReaderIndex, int IndexInReader)>();
            }
        }

        /// <summary>读整份仪器配置供 B03/B04 列项；读不到退回默认配置（与执行端读同一份）</summary>
        private InstrumentConnectionConfig LoadBoardConfig()
        {
            try
            {
                return _configService.Load();
            }
            catch (Exception ex)
            {
                _log.Warn($"读取仪器配置失败（B03/B04 按默认配置列项）：{ex.Message}");
                return new InstrumentConnectionConfig();
            }
        }

        #endregion
    }
}
