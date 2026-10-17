using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：A16 产品通讯初始化（整机工位）。
    /// 全方案唯一执行产品通讯判定的模块；握手结果记在 _productCommOk，供后续模块走快速跳过或演示分支。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>
        /// A16 产品通讯初始化：打开产品串口 → 进入产测模式（防休眠，产品断电前不再休眠）→ 0x72 → 0x74。
        /// 全方案唯一执行产品通讯判定的模块，并记录本轮产品通讯状态供后续模块快速跳过/演示分支；
        /// 后续模块不再重发进入产测模式与 0x72/0x74。无仪器演示模式下本模块即“只读产品串口”的主体，照常执行。
        /// </summary>
        //测试项目**************************---A16---**************************产品通讯初始化
        public async Task ProductCommInitModule(TestItemResultCollector collector, CancellationToken token)
        {
            using var product = _productFactory();
            _productCommOk = await ProductHandshakeAsync(product, collector, token);
            if (_productCommOk.Value)
                _log.Info("产品通讯初始化成功：产品已进入产测模式，断电前不会再休眠；后续模块不再重发进入产测模式/0x72/0x74");
            else
                _log.Warn("产品通讯初始化失败：后续模块产品依赖项将快速跳过或按读回缺失判 FAIL");
        }

        /// <summary>
        /// 打开产品并执行“进入产测模式 + 产品信息获取（MAC/嵌软版本）+ 产品当前状态查询”（仅 A16 产品通讯初始化调用）。
        /// MAC 读回不为空即合格；嵌软版本与管控规格 Spec.ExpectedFirmwareVersion 比对（烧录老版本 FAIL）。
        /// 产品未连接/无响应时仅这三项 FAIL，不阻断仪器控制流程——
        /// 没接产品也能调试 BPA700 侧逻辑，无需在调试器里跳语句。
        /// 返回产品通讯是否可用。
        /// </summary>
        private async Task<bool> ProductHandshakeAsync(IProductController product, TestItemResultCollector collector, CancellationToken token)
        {
            try
            {
                await product.OpenAsync(token);

                // 进入产测模式（0x07/0x01）：打开串口后立即发送防产品休眠（约10秒无通讯即休眠）；
                // 协议要求：必须在 0x72/0x74 查询之前发送
                try
                {
                    await product.EnterProductionTestModeAsync(token);
                    _log.Info("产品进入产测模式发送成功（防休眠）");
                }
                catch (Exception ex)
                {
                    _log.Warn($"产品进入产测模式发送失败（继续尝试问询）：{ex.Message}");
                }

                // 产品信息获取（0x72）：回读 MAC + 嵌软版本，拆两项分别上报
                var (mac, version) = await product.QueryProductInfoAsync(token);

                // MAC 地址：读回不为空即合格
                if (mac.Length == 0)
                {
                    await collector.SetAsync(SpecItems.ProductMacAddress, TestItemResult.OfProductText(0, "无响应"));
                    _log.Warn("产品信息获取(MAC地址)无响应（串口已打开但产品无返回），判 FAIL");
                }
                else
                {
                    await collector.SetAsync(SpecItems.ProductMacAddress, TestItemResult.OfProductText(1, mac));
                    _log.Info($"产品信息获取(MAC地址)：{mac}");
                }

                // 嵌软版本：与管控规格比对（版本升级改 Spec.ExpectedFirmwareVersion 一处）
                var expected = Spec.ExpectedFirmwareVersion;
                if (version.Length == 0)
                {
                    await collector.SetAsync(SpecItems.ProductFirmwareVersion, TestItemResult.OfProductText(0, "无响应"));
                    _log.Warn("产品信息获取(嵌软版本)无响应（串口已打开但产品无返回），判 FAIL");
                }
                else
                {
                    var match = string.Equals(version, expected, StringComparison.OrdinalIgnoreCase);
                    if (match)
                        _log.Info($"产品信息获取(嵌软版本)：{version}，与管控规格一致");
                    else
                        _log.Warn($"嵌软版本不一致：读回 {version}，管控规格 {expected}，判 FAIL（请确认是否烧录了老版本）");
                    await collector.SetAsync(SpecItems.ProductFirmwareVersion, TestItemResult.OfProductText(match ? 1 : 0, version));
                }

                // 产品当前状态查询（返回内容预留待定：有返回即通过，读回内容原样显示在产品列）
                var status = await product.QueryProductStatusAsync(token);
                if (!string.IsNullOrWhiteSpace(status))
                {
                    await collector.SetAsync(SpecItems.ProductStatusQuery, TestItemResult.OfProductText(1, status));
                }
                else
                {
                    await collector.SetAsync(SpecItems.ProductStatusQuery, TestItemResult.OfProductText(0, "无响应"));
                    _log.Warn("产品当前状态查询无响应（串口已打开但产品无返回），判 FAIL");
                }
                // TODO: 状态返回格式确定后，改为按状态内容判定

                // 三项里读到任何一项真实响应才算通讯可用：串口开着但产品全程无响应时不能报“可用”，
                // 否则后续模块都按产品在线走（白跑一轮又一轮测量轮次），且它们的产品项会拖到超时才 FAIL
                var commAlive = mac.Length > 0 || version.Length > 0 || !string.IsNullOrWhiteSpace(status);
                if (!commAlive)
                    _log.Warn("产品串口已打开但 0x72/0x74 全程无响应，本轮按“产品通讯不可用”记录，后续模块产品依赖项快速跳过");
                return commAlive;
            }
            catch (OperationCanceledException)
            {
                // 用户点“停止”：向上层传递，不当作测试异常
                throw;
            }
            catch (Exception ex)
            {
                // 产品串口不存在（如 Could not find file 'COM16'）是最常见根因，
                // 附上系统当前实际存在的串口清单，便于直接核对仪器链接配置里的产品端口
                var existPorts = string.Join(", ", System.IO.Ports.SerialPort.GetPortNames());
                _log.Warn($"产品通讯不可用（打开/问询/状态查询失败），[产品信息获取(MAC地址)]/[产品信息获取(嵌软版本)]/[产品当前状态查询] 判 FAIL，继续仪器控制流程：{ex.Message}；系统当前存在串口：[{existPorts}]，请核对仪器链接配置的产品串口号与物理接线");
                var failResult = TestItemResult.OfProductText(0, "通讯失败");
                await collector.SetAsync(SpecItems.ProductMacAddress, failResult);
                await collector.SetAsync(SpecItems.ProductFirmwareVersion, failResult);
                await collector.SetAsync(SpecItems.ProductStatusQuery, failResult);
                return false;
            }
        }
    }
}
