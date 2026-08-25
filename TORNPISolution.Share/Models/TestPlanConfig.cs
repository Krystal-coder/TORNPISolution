namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 测试方案配置（JSON 序列化用），保存方案名称与模块排列顺序
    /// </summary>
    public class TestPlanConfig
    {
        /// <summary>方案名称，如 "方案A"</summary>
        public string PlanName { get; set; } = string.Empty;

        /// <summary>按测试顺序排列的模块 Id 列表</summary>
        public List<string> ModuleIds { get; set; } = new();
    }
}
