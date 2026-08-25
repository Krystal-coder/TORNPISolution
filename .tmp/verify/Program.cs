using System.Text;
using TORNPISolution.Service;
using TORNPISolution.Share.Models;

Console.OutputEncoding = Encoding.UTF8;

var spec = new Spec();
var modules = spec.GetAllModules();
var ti = new TestItem();
var totalFail = 0;

Console.WriteLine("========== 1. Spec 全部模块与测试项 ==========");
foreach (var m in modules)
{
    Console.WriteLine($"[{m.Id}] {m.Name}（{m.Items.Count} 项）");
    foreach (var it in m.Items)
        Console.WriteLine($"    {it.Name}  规格 {it.LowerLimit} ~ {it.UpperLimit} {it.Unit}");
}

// ---- 2. M01 血压仪初始化连接：ActiveSimulator 默认 None -> "未选择模拟仪"分支，不碰硬件 ----
Console.WriteLine("\n========== 2. TestItemxx(2, 血压仪初始化连接) 分派 + 名称一致性 ==========");
var m01 = modules.First(m => m.Id == TestModuleIds.BpDeviceInit);
var bpInit = await ti.TestItemxx(2, m01, CancellationToken.None);
totalFail += Check("M01", m01, bpInit);

// ---- 3. M02 NFC功能测试：串口打开失败 -> catch 分支，不碰真实设备 ----
Console.WriteLine("\n========== 3. TestItemxx(3, NFC功能测试) 分派 + 名称一致性 ==========");
var m02 = modules.First(m => m.Id == TestModuleIds.NfcFunctionTest);
var nfc = await ti.TestItemxx(3, m02, CancellationToken.None);
totalFail += Check("M02", m02, nfc);

// ---- 4. M03 血压测量精度检测：ActiveSimulator 默认 None -> "需选 BPA700"分支，不碰硬件 ----
Console.WriteLine("\n========== 4. TestItemxx(4, 血压测量精度检测) 分派 + 名称一致性 ==========");
var m03 = modules.First(m => m.Id == TestModuleIds.BpAccuracyTest);
var bpAccuracy = await ti.TestItemxx(4, m03, CancellationToken.None);
totalFail += Check("M03", m03, bpAccuracy);

// ---- 5. M04 压力控制功能测试（预留）：无硬件依赖，自动 PASS ----
Console.WriteLine("\n========== 5. TestItemxx(5, 压力控制功能测试) 分派 + 名称一致性 ==========");
var m04 = modules.First(m => m.Id == TestModuleIds.PressureControlTest);
var pressure = await ti.TestItemxx(5, m04, CancellationToken.None);
totalFail += Check("M04", m04, pressure);

// ---- 6. M00 初始化仪器：需要固高硬件不能实跑，静态核对键名 ----
Console.WriteLine("\n========== 6. M00 初始化仪器 键名静态核对 ==========");
var m00 = modules.First(m => m.Id == TestModuleIds.InitInstruments);
var m00ExpectedKey = "固高IO模块初始化";   // TestItem.InitInstrumentsModule 中的结果键
foreach (var it in m00.Items)
{
    var ok = it.Name == m00ExpectedKey;
    Console.WriteLine($"  Spec 项 \"{it.Name}\" {(ok ? "==" : "!=")} TestItem 键 \"{m00ExpectedKey}\"  -> {(ok ? "OK" : "FAIL")}");
    if (!ok) totalFail++;
}

// ---- 7. 动态规格机制演示：WithDynamicSpec 按仪器值 ± 容差现算（机制保留，后续测量项可用） ----
Console.WriteLine("\n========== 7. 动态规格机制演示 ==========");
var demoInstrument = 121.4;   // 假设仪器读回标准值
var demoTolerance = 3.0;
var demo = TestItemResult.WithDynamicSpec(123.0, demoInstrument - demoTolerance, demoInstrument + demoTolerance);
Console.WriteLine($"  仪器读回 {demoInstrument}，容差 ±{demoTolerance} -> 动态规格 {demo.SpecMin} ~ {demo.SpecMax}，测试值 {demo.Value}");
var pass = demo.Value >= demo.SpecMin && demo.Value <= demo.SpecMax;
Console.WriteLine($"  判定：{(pass ? "PASS" : "FAIL")}（测试值落在动态规格内即 PASS）");

Console.WriteLine($"\n========== 总结：{(totalFail == 0 ? "全部通过，无规格/序号/名称不一致问题" : $"{totalFail} 处不一致")} ==========");
return totalFail;

// 比对 Spec 项与 TestItemxx 返回结果键（双向），并展示动态规格
static int Check(string tag, TestModuleModel module, Dictionary<string, TestItemResult> results)
{
    var fail = 0;
    foreach (var it in module.Items)
    {
        if (!results.TryGetValue(it.Name, out var v))
        {
            Console.WriteLine($"  FAIL：{tag} Spec 项 \"{it.Name}\" 在 TestItem 结果中不存在");
            fail++;
        }
        else
        {
            var specText = v.SpecMin.HasValue
                ? $"动态规格 {v.SpecMin} ~ {v.SpecMax}"
                : $"静态规格 {it.LowerLimit} ~ {it.UpperLimit}";
            Console.WriteLine($"  OK：{tag} \"{it.Name}\" 测试值 = {v.Value}（{specText} {it.Unit}）");
        }
    }
    foreach (var k in results.Keys.Where(k => module.Items.All(i => i.Name != k)))
    {
        Console.WriteLine($"  FAIL：{tag} TestItem 结果键 \"{k}\" 在 Spec 中不存在（孤立项）");
        fail++;
    }
    return fail;
}
