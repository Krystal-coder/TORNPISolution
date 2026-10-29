using TORNPISolution.Interface;
using TORNPISolution.Service.Hardware;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：单板工位（B 前缀）模块的执行体总入口；单板与整机是两个独立工位，一份方案只跑一类的模块。
    /// 执行内容与整机相同的（如 B01 与 A00 都是初始化固高IO）直接委托 WholeMachine 那边的方法体
    /// </summary>
    public partial class TestItem
    {
        /// <summary>单板初始化仪器（B01）：内容同整机 A00；固高打开本身有幂等守卫（已打开直接返回成功），两工位各自执行互不干扰。</summary>
        //测试项目**************************---B01---**************************初始化仪器（单板，内容同 A00）
        public Task BoardInitInstrumentsModule(TestItemResultCollector collector, CancellationToken token)
            => InitInstrumentsModule(collector, token);
    }
}
