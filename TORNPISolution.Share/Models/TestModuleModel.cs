namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 测试模块定义（一个独立的 Test，内含若干测试项）
    /// </summary>
    public class TestModuleModel
    {
        /// <summary>模块唯一标识</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>模块名称，如 "电压测试"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>模块功能说明</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>模块图标（字符图标）</summary>
        public string Icon { get; set; } = string.Empty;

        /// <summary>本模块包含的测试项集合</summary>
        public List<TestItemModel> Items { get; set; } = new();
    }
}
