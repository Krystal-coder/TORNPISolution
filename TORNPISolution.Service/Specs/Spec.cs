using TORNPISolution.Interface;
using TORNPISolution.Share.Enum;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 规格类：每模块一个 Build 方法，GetAllModules 是模块目录。
    /// 测试项名称统一引用 <see cref="SpecItems"/> 常量（项名唯一来源，TestItem 上报同引用）。
    /// 动态规格怎么定义：这里写的 SpecMin/SpecMax 只是静态规格（默认/兜底）；需按仪器读回值现算规格的项（如血压：仪器值±容差）不改这里，由 TestItem 运行时把动态规格随 TestItemResult 带回，UI 判定优先用动态规格，未测到才用这里的静态值。
    /// 本文件留目录入口 GetAllModules（建好后与执行端对账有无实现）与两工位共用的书写辅助；模块清单按工位在分部文件里。
    /// 分部文件的书写约定：#region 模块清单 里一个模块一段、段与段之间不夹辅助方法，辅助统一放文件末尾的 region——
    /// 目录要从上往下扫着读，夹一行取配置的辅助就把清单断了；新模块照此加，别开特例。
    /// </summary>
    public partial class Spec : ITestModuleService
    {
        private readonly ILogService _log = new LogService(nameof(Spec));
        private readonly IInstrumentConfigService _configService;
        private readonly ITestItemService? _items;

        /// <param name="configService">仪器配置服务：A05 项清单按当前勾选的静态压力点动态生成（一点一项）</param>
        /// <param name="items">执行端，供目录建好后对账模块有无实现；可空——目录不该因为拿不到执行端就建不出来</param>
        public Spec(IInstrumentConfigService configService, ITestItemService? items = null)
        {
            _configService = configService;
            _items = items;
        }

        /// <summary>
        /// 嵌软版本管控规格（A16“产品信息获取(嵌软版本)”项的规格与判定唯一来源）：
        /// 产品读回版本与它不一致判 FAIL（防烧录老版本）；版本升级时只改这一处，规格列显示与判定同步生效。
        /// </summary>
        public const string ExpectedFirmwareVersion = "V0.1.POR264002";

        public const string FirmwareStatus = "03-00";
        /// <summary>
        /// 模块目录：先整机工位、后单板工位，各自按书写顺序（顺序即新建方案时的默认顺序，可拖拽调整）。
        /// 页面按模块自己的 Category 过滤，所以这里返回全量不影响左右两栏各只显示本类。
        /// 新增模块：在对应工位的分部文件里写一个 Build 方法 + 加进那一份清单，别改本方法。
        /// </summary>
        public IReadOnlyList<TestModuleModel> GetAllModules()
        {
            _log.Info($"{DateTime.Now} Start calling the Spec.GetAllModules() method . {Environment.NewLine}");
            try
            {
                var list = new List<TestModuleModel>();
                list.AddRange(BuildWholeMachineModules());
                list.AddRange(BuildBoardModules());
                VerifyAllModulesImplemented(list);
                return list;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in Spec.class GetAllModules method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the Spec.GetAllModules() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// 逐个模块 Id 向执行端对一句“有没有实现”：漏实现的模块拖进方案要跑到那一轮才抛 NotSupportedException，看上去像设备问题；
        /// 这里在上架时就记 Error，未注入执行端则记 Warn 跳过。
        /// </summary>
        private void VerifyAllModulesImplemented(IReadOnlyList<TestModuleModel> modules)
        {
            // 没注入也要留痕：正常一路不报任何内容，不报时人分不清是“没漏”还是“根本没查”
            if (_items == null)
            {
                _log.Warn("未注入执行端，模块目录与分派表的对账已跳过（请核对组合根里 Spec 的注册）");
                return;
            }
            var missing = modules.Where(m => !_items.ImplementsModule(m.Id)).Select(m => $"{m.Id}（{m.Name}）").ToList();
            if (missing.Count > 0)
                _log.Error($"模块目录里有 {missing.Count} 个模块执行端没实现，拖进方案会直接抛：{string.Join("、", missing)}");
        }

        #region 书写辅助（对应模板的 TIName[j]=... 与 ResultName[k]/SpecMin[k]/SpecMax[k] 赋值）

        /// <summary>
        /// 新建一个测试模块（TI），返回模块对象供追加测试项；模块进入目录的顺序由各工位的清单方法决定。对应模板 TIName[j]/TIComments[j] 一行。
        /// 这里没有 icon 参数是有意的：模块图标从没被任何界面绑过，值又是 \uE90F 这种没人认得的码点，25 处调用各带一次只是噪声。
        /// 以后真要加图标，走 PackIconKind 枚举名（与仪器导航那边的 DeviceNavItem.Icon 同一套），不要把码点填回来。
        /// </summary>
        private static TestModuleModel AddTI(string id, ModuleCategory category, string name, string description)
            => new TestModuleModel { Id = id, Category = category, Name = name, Description = description };

        /// <summary>
        /// 追加 A16 产品通讯初始化的三个通用通讯项（仅本模块拥有，全方案只判定一次；其余模块不重复问询）：
        /// MAC 地址（读回不为空即合格）+ 嵌软版本（与 <see cref="ExpectedFirmwareVersion"/> 比对）+ 当前状态查询。
        /// </summary>
        private static void AddHandshakeItems(TestModuleModel ti)
        {
            AddResult(ti, name: SpecItems.ProductMacAddress,
                description: "0x72 读 MAC（回包前 6 字节）",
                specMin: 1, specMax: 1, unit: "", valueSource: "Product", hideSpecRange: true);
            AddResult(ti, name: SpecItems.ProductFirmwareVersion,
                description: "0x72 读嵌软版本，与管控规格比对",
                specMin: 1, specMax: 1, unit: "", valueSource: "Product", specText: ExpectedFirmwareVersion);
            AddResult(ti, name: SpecItems.ProductStatusQuery,
                description: "0x74 读测试模式和状态",
                specMin: 1, specMax: 1, unit: "", valueSource: "Product", specText: FirmwareStatus);
        }

        /// <summary>曲线纵轴单位字样：微伏。μ 用 \u03BC 转义写死，免得手打时混进外形相同的 MICRO SIGN（U+00B5）造成字形不一致</summary>
        protected const string WaveformUnitMicroVolt = "\u03BCV";

        /// <summary>
        /// 给模块追加一条测试项。对应模板 ResultName[k]/SpecMin[k]/SpecMax[k] 三行。
        /// waveform = 波形呈现模式（None / Inline 行内折叠 / Overview ECG Signal）：该项执行时会上抛实时采样点才声明，
        /// UI 按这里渲染（用 Overview 还要在模块上声明 ti.Overview），不要在 UI 里按项名判断。
        /// waveformUnit = 曲线纵轴单位（与上面 unit 不同源：速率项的 unit 是 mmHg/s、心率项是 BPM，而曲线画的仍是压力 mmHg）；留空则沿用 unit。
        /// waveformConfigSource = 横轴倍率去哪个仪器配置读（None = 不吃配置，用界面默认 24 像素/秒）；后两个只在声明了 waveform 时有意义。
        /// valueSource = 测试值来源：Instrument（默认，仪器值）/ Product（产品上报值）/ Both（双列比对）/ Error（误差口径：主值=误差按静态规格判，双列=仪器/产品值）。
        /// hideSpecRange=true = 通讯检查项：无数值规格，规格列显示“有响应即通过”。
        /// groupLabel 非空 = 明细表按这个标题分节展示（同一模块内相同的归一节，节头可折叠）；整模块都不填就是平铺。
        /// specText 非空 = 字符串规格项（如嵌软版本管控）：规格列直接显示该文本。
        /// 暂不具备自动化条件的预留项不用跳过标记：模块执行时报“预留（不判定）”结果即可。
        /// </summary>
        private static void AddResult(TestModuleModel ti, string name, string description, double specMin, double specMax, string unit, WaveformPresentation waveform = WaveformPresentation.None, string valueSource = "Instrument", bool hideSpecRange = false, string specText = "", string groupLabel = "", string waveformUnit = "", WaveformConfigSource waveformConfigSource = WaveformConfigSource.None)
        {
            ti.Items.Add(new TestItemModel
            {
                Name = name,
                Description = description,
                LowerLimit = specMin,
                UpperLimit = specMax,
                Unit = unit,
                Waveform = waveform,
                WaveformUnit = waveformUnit,
                WaveformConfigSource = waveformConfigSource,
                GroupLabel = groupLabel,
                ValueSource = valueSource,
                HideSpecRange = hideSpecRange,
                SpecText = specText
            });
        }

        #endregion
    }
}
