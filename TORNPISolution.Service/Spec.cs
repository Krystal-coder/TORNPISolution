using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 规格类：所有测试模块的定义与测试规格集中在这一个类里管理。
    /// 书写方式参照传统测试程式的 Spec：
    ///   一个模块一段 "//===模块Id===模块名" 注释分隔，模块头一行定义 TI（名称/说明），
    ///   模块下逐条书写测试项：ResultName（名称/说明）+ SpecMin + SpecMax + 单位。
    /// 静态规格 vs 动态规格：
    ///   这里写的 SpecMin/SpecMax 是静态规格（默认/兜底）；
    ///   需要按仪器读回值现算规格的项（如血压：仪器值±容差），
    ///   运行时由 TestItem 把动态规格随 TestItemResult 带回 UI 覆盖显示与判定，
    ///   此时这里写的静态规格按"标准值±容差"预置作兜底（未测到动态规格时才生效）。
    /// 与模板的差异说明：
    ///   模板用固定索引数组（TIName[j] / ResultName[k] / VarIndex）按书写顺序编号，
    ///   本项目模块顺序由用户在方案中自选（可任意拖拽排序），
    ///   因此用模块 Id（TestModuleIds）标识模块、不靠书写顺序编号，
    ///   AddTI / AddResult 对应模板的 TIName[j] 赋值与 ResultName[k]/SpecMin[k]/SpecMax[k] 赋值。
    /// </summary>
    public class Spec : ITestModuleService
    {
        private readonly ILogService _log = new LogService(nameof(Spec));

        public IReadOnlyList<TestModuleModel> GetAllModules()
        {
            _log.Info($"{DateTime.Now} Start calling the Spec.GetAllModules() method . {Environment.NewLine}");
            try
            {
                var modules = new List<TestModuleModel>();
                TestModuleModel ti;

                //======================================M00-----------初始化仪器
                ti = AddTI(modules, TestModuleIds.InitInstruments, "初始化仪器", "\uE90F",
                    "初始化固高IO模块等硬件，全部初始化成功为 PASS");

                AddResult(ti, name: "固高IO模块初始化",          // ResultName
                    description: "启动 EtherCAT 总线",
                    specMin: 1, specMax: 1, unit: "");          // SpecMin / SpecMax（1=成功 0=失败，成功才 PASS）

                //======================================M01-----------血压仪初始化连接
                // 按仪器连接配置中选择的血压模拟仪初始化：选康伟达初始化康伟达，选鲸扬初始化 BPA700
                // BPA700 初始化时同时下发 Category/压力单位/袖带容量三项设置（默认 Adult/mmHg/500ml）
                ti = AddTI(modules, TestModuleIds.BpDeviceInit, "血压仪初始化连接", "\uE95E",
                    "按仪器连接配置初始化选中的血压模拟仪（康伟达/鲸扬 BPA700）");

                AddResult(ti, name: "血压模拟仪初始化连接",          // ResultName
                    description: "配置页面选择康伟达/鲸扬，未选择模拟仪则 FAIL",
                    specMin: 1, specMax: 1, unit: "");          // SpecMin / SpecMax（1=成功 0=失败，成功才 PASS）

                //======================================M02-----------NFC功能测试
                ti = AddTI(modules, TestModuleIds.NfcFunctionTest, "NFC功能测试", "\uE779",
                    "使用 NFC 读卡器读取设备 NFC 标签，写入并验证 MAC 信息");

                AddResult(ti, name: "标签识别",
                    description: "NFC 读卡器读取到标签 UID",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "MAC 信息写入",
                    description: "MAC 地址写入标签用户区",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "MAC 信息验证",
                    description: "回读标签内容比对 MAC",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "标签内容读取",
                    description: "查看标签用户区内容",
                    specMin: 1, specMax: 1, unit: "");

                //======================================M03-----------产品充放气测试（BP-001/BP-002）
                // 流程：BPARunBPTest 进入待测 -> 测试员按产品物理键（开始/停止） ->
                //       通过 BPA700 回调 status 序列与压力曲线验证结果
                ti = AddTI(modules, TestModuleIds.BpAccuracyTest, "产品充放气测试", "\uE95E",
                    "BP-001 物理按键启动测量 / BP-002 测量过程中手动停止（需连接储气瓶）");

                AddResult(ti, name: "产品问询",
                    description: "发送 0x72 读版本/MAC，产品返回 MAC 地址和嵌软版本",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "产品当前状态查询",
                    description: "发送 0x74 读测试模式和状态（有返回即通过）",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "BP-001物理按键启动测量",
                    description: "按产品[开始]键充气测量，BPA700 status 依次 Busy→Stable→Finish 为 PASS",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "BP-002测量中手动停止",
                    description: "充气中按产品[停止]键，立即停止充气并快速放气，放气时间 ≤配置上限（血压仪参数配置→充放气规格）",
                    specMin: 0, specMax: 10, unit: "s");

                //======================================M05-----------血压静态压力精度测试（BP-003）
                // 流程：逐点 BPARunStaticPressure（配置勾选的压力点） -> 稳压保持 ->
                //       读产品上报压力值（0x7E） -> |产品值-标准值|≤2mmHg
                ti = AddTI(modules, TestModuleIds.BpStaticAccuracyTest, "血压静态压力精度测试", "\uE95E",
                    "BP-003：逐点静态加压（压力点取配置），产品上报值与标准值误差 ≤2mmHg（含压力曲线）");

                AddResult(ti, name: "产品问询",
                    description: "发送 0x72 读版本/MAC，产品返回 MAC 地址和嵌软版本",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "产品当前状态查询",
                    description: "发送 0x74 读测试模式和状态（有返回即通过）",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "BP-003静态压力精度测试",
                    description: "各压力点 |产品上报值-BPA700标准值| 的最大误差 ≤2mmHg（测试值=最大绝对误差）",
                    specMin: 0, specMax: 2, unit: "mmHg");

                //======================================M06-----------血压动态压力精度测试（BP-004/BP-005）
                // 流程：BPARunBPTest 多组血压/心率参数 × 每组3次（需储气瓶产生脉搏波） ->
                //       产品上报收缩压/舒张压/心率（0x7B 推送） -> 极差与系统偏差判定
                ti = AddTI(modules, TestModuleIds.BpDynamicAccuracyTest, "血压动态压力精度测试", "\uE95E",
                    "BP-004 血压精度/重复性（极差≤2 偏差≤3） + BP-005 心率精度/重复性（需连接储气瓶）");

                AddResult(ti, name: "产品问询",
                    description: "发送 0x72 读版本/MAC，产品返回 MAC 地址和嵌软版本",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "产品当前状态查询",
                    description: "发送 0x74 读测试模式和状态（有返回即通过）",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "BP-004收缩压精度测试",
                    description: "4组血压×3次，|收缩压均值-设定值| 系统偏差 ≤3mmHg（测试值=最大偏差）",
                    specMin: 0, specMax: 3, unit: "mmHg");
                AddResult(ti, name: "BP-004收缩压重复性测试",
                    description: "4组血压×3次，收缩压极差 |最大-最小| ≤2mmHg（测试值=最大极差）",
                    specMin: 0, specMax: 2, unit: "mmHg");
                AddResult(ti, name: "BP-004舒张压精度测试",
                    description: "4组血压×3次，|舒张压均值-设定值| 系统偏差 ≤3mmHg（测试值=最大偏差）",
                    specMin: 0, specMax: 3, unit: "mmHg");
                AddResult(ti, name: "BP-004舒张压重复性测试",
                    description: "4组血压×3次，舒张压极差 |最大-最小| ≤2mmHg（测试值=最大极差）",
                    specMin: 0, specMax: 2, unit: "mmHg");
                AddResult(ti, name: "BP-005心率精度测试",
                    description: "4组心率×3次，|心率均值-设定值| ≤ max(5, 设定值×5%)（测试值=最大偏差）",
                    specMin: 0, specMax: 6, unit: "BPM");
                AddResult(ti, name: "BP-005心率重复性测试",
                    description: "4组心率×3次，心率极差 |最大-最小| ≤ max(5, 设定值×5%)（测试值=最大极差）",
                    specMin: 0, specMax: 6, unit: "BPM");

                //======================================M04-----------压力控制功能测试（BP-006~BP-009）
                // BP-006/007/008 仅验证 BPA700 自身性能（无需产品）；BP-009 需产品实时上报压力配合
                ti = AddTI(modules, TestModuleIds.PressureControlTest, "压力控制功能测试", "\uE713",
                    "BP-006~BP-009：最高充气压力/匀速放气控制/快释放气功能/过压保护");

                AddResult(ti, name: "产品问询",
                    description: "发送 0x72 读版本/MAC，产品返回 MAC 地址和嵌软版本",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "产品当前状态查询",
                    description: "发送 0x74 读测试模式和状态（有返回即通过）",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "BP-006最高充气压力",
                    description: "BPARunStaticPressure 充气至 350，稳压值应在 340~355mmHg（测试值=稳定压力）",
                    specMin: 340, specMax: 355, unit: "mmHg");
                AddResult(ti, name: "BP-007匀速放气控制",
                    description: "充气至 150 后匀速放气 60s，放气速率稳定在 2~6mmHg/s（测试值=平均速率）",
                    specMin: 2, specMax: 6, unit: "mmHg/s");
                AddResult(ti, name: "BP-008快释放气功能",
                    description: "充气至 250 后快释放气，压力应在 10s 内归零（测试值=归零耗时）",
                    specMin: 0, specMax: 10, unit: "s");
                AddResult(ti, name: "BP-009过压保护",
                    description: "过压测试充气，触发保护时压力应在 300~320mmHg（测试值=触发压力，含曲线）",
                    specMin: 300, specMax: 320, unit: "mmHg");

                //======================================M07-----------AECG100 初始化连接
                // 通过原生 SDK（AECG100x64.dll）初始化并连接 AECG100 多功能综合性能测试仪
                ti = AddTI(modules, TestModuleIds.Aecg100Init, "AECG100初始化连接", "\uE90F",
                    "初始化并连接鲸扬 AECG100 多功能综合性能测试仪（USB VCP + 原生 SDK）");

                AddResult(ti, name: "AECG100初始化连接",
                    description: "WTQInit + WTQConnect 连接成功",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "AECG100设备信息读取",
                    description: "连接成功后读取设备型号/固件/序列号信息",
                    specMin: 1, specMax: 1, unit: "");

                //======================================M08-----------ECG 采集与启动（ECG-001/002）
                // 产品问询/状态查询标记 TBD（默认 PASS，产品串口协议预留）；
                // ECG-001 测量启动：仪器输出 ECG 波形，验证 SDK 返回成功；
                // ECG-002 导联脱落检测：仪器侧预留导联脱落判断，脱落后不采集数据判 FAIL
                ti = AddTI(modules, TestModuleIds.EcgAcquisitionTest, "ECG采集与启动", "\uE95E",
                    "ECG-001 测量启动 / ECG-002 导联脱落检测（产品串口通讯预留，先控制仪器）");

                AddResult(ti, name: "产品问询",
                    description: "发送 0x72 读版本/MAC，产品返回 MAC 地址和嵌软版本",
                    specMin: 1, specMax: 1, unit: "",
                    det: false);
                AddResult(ti, name: "产品当前状态查询",
                    description: "发送 0x74 读测试模式和状态（有返回即通过）",
                    specMin: 1, specMax: 1, unit: "",
                    det: false);
                AddResult(ti, name: "ECG-001测量启动",
                    description: "[DET] AECG100 输出 ECG 波形（WTQOutputECG），SDK 返回成功即 PASS",
                    specMin: 1, specMax: 1, unit: "");
                AddResult(ti, name: "ECG-002导联脱落检测",
                    description: "[DET] 仪器侧判断导联是否脱落（预留，脱落不采集数据判 FAIL）",
                    specMin: 1, specMax: 1, unit: "");

                //======================================M09-----------ECG 心率监测（ECG-004）
                // 产品问询/状态查询标记 TBD；
                // ECG-004 心率检测精度：配置页设置心率点（默认 40/60/80/100/120/160 BPM），
                //   每档仪器输出对应频率 ECG 波形（BPM/60 Hz），重复 3 次取最大值，
                //   仅验证仪器设置下发成功（产品回传比对预留）
                ti = AddTI(modules, TestModuleIds.EcgHeartRateTest, "ECG心率监测", "\uE95E",
                    "ECG-004 心率检测精度（多档心率设置，误差 ≤±2BPM，产品回传预留）");

                AddResult(ti, name: "产品问询",
                    description: "发送 0x72 读版本/MAC，产品返回 MAC 地址和嵌软版本",
                    specMin: 1, specMax: 1, unit: "",
                    det: false);
                AddResult(ti, name: "产品当前状态查询",
                    description: "发送 0x74 读测试模式和状态（有返回即通过）",
                    specMin: 1, specMax: 1, unit: "",
                    det: false);
                AddResult(ti, name: "ECG-004心率检测精度",
                    description: "[DET] 各心率点仪器设置下发成功，最大偏差 ≤±2BPM（测试值=最大偏差，产品回传比对预留）",
                    specMin: 0, specMax: 2, unit: "BPM");

                //======================================M10-----------ECG 信号质量与波形（ECG-009~012）
                // 按 YY 9706.247-2021 标准条款，AECG100 发出标准测试信号，产品回传测量值后由上位机判定。
                // 当前阶段：产品回传比对逻辑预留（TBD），默认 PASS。
                // TODO: 产品串口协议定稿后，补充各测试项的产品回传读取与判定逻辑。
                ti = AddTI(modules, TestModuleIds.EcgSignalQualityTest, "ECG信号质量与波形", "\uE95E",
                    "ECG-009~012：按 YY 9706.247-2021 标准，AECG100 发出标准信号，产品回传判定（协议预留）");

                AddResult(ti, name: "ECG-009动态范围",
                    description: "[DET] 标准 201.12.4.4.101：输出 6mV 三角波 + ±300mV DC 偏置，DUT 回放幅度衰减 < 50μV（产品回传预留）",
                    specMin: 0, specMax: 50, unit: "μV");
                AddResult(ti, name: "ECG-010输入阻抗",
                    description: "[DET] 标准 201.12.4.4.102：输出 10Hz 5mV 正弦波，开关断开/闭合振幅变化 ≤ 6% → 输入阻抗 > 10MΩ（产品回传 + 外部开关预留）",
                    specMin: 0, specMax: 6, unit: "%");
                AddResult(ti, name: "ECG-011增益准确性",
                    description: "[DET] 标准 201.12.4.4.104：输出 5Hz 2mV 正弦波，各增益档幅度误差 ≤ ±10%（产品回传预留）",
                    specMin: 0, specMax: 10, unit: "%");
                AddResult(ti, name: "ECG-012频率响应",
                    description: "[DET] 标准 201.12.4.4.108：0.67Hz~40Hz 逐频点输出 2mV 正弦波，各频点响应在 5Hz 响应的 70%~140%（产品回传预留）",
                    specMin: 70, specMax: 140, unit: "%");

                return modules;
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

        #region 书写辅助（对应模板的 TIName[j]=... 与 ResultName[k]/SpecMin[k]/SpecMax[k] 赋值）

        /// <summary>新增一个测试模块（TI），返回模块对象供追加测试项。对应模板 TIName[j]/TIComments[j] 一行</summary>
        private static TestModuleModel AddTI(List<TestModuleModel> modules, string id, string name, string icon, string description)
        {
            var module = new TestModuleModel { Id = id, Name = name, Icon = icon, Description = description };
            modules.Add(module);
            return module;
        }

        /// <summary>
        /// 给模块追加一条测试项。对应模板 ResultName[k]/SpecMin[k]/SpecMax[k] 三行。
        /// det=true (DET，默认) = 正常执行；det=false (TBD) = 跳过该项，显示"未测试"。
        /// </summary>
        private static void AddResult(TestModuleModel ti, string name, string description, double specMin, double specMax, string unit, bool det = true)
        {
            ti.Items.Add(new TestItemModel
            {
                Name = name,
                Description = description,
                LowerLimit = specMin,
                UpperLimit = specMax,
                Unit = unit,
                Det = det
            });
        }

        #endregion
    }
}
