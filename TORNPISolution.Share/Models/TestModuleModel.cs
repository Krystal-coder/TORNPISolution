using TORNPISolution.Share.Enum;

namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 测试模块定义
    /// </summary>
    public class TestModuleModel
    {
        /// <summary>模块唯一标识</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>所属工位类别（整机/单板）：由 Spec 目录声明，模块库页面按它过滤，一份方案不跨类混装</summary>
        public ModuleCategory Category { get; set; } = ModuleCategory.WholeMachine;

        /// <summary>模块名称，如 "电压测试"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>模块功能说明</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>本模块包含的测试项集合</summary>
        public List<TestItemModel> Items { get; set; } = new();

        /// <summary>
        /// ECG Signal窗口的显示参数
        /// </summary>
        public WaveformOverviewProfile? Overview { get; set; }
    }
}
