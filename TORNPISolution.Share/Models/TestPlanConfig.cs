using System.Text.Json.Serialization;
using TORNPISolution.Share.Enum;

namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 测试方案配置（JSON 序列化用），保存方案名称、所属工位类别与模块排列顺序
    /// </summary>
    public class TestPlanConfig
    {
        /// <summary>方案名称，如 "方案A"</summary>
        public string PlanName { get; set; } = string.Empty;

        /// <summary>
        /// 方案所属工位类别：加载方案时据此把模块库翻到对应一面，方案列表也按它过滤。
        /// 可空=字段缺失（分类改造之前存的老文件），由 MainViewModel.LoadPlanWithName 按模块 Id 推断。
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ModuleCategory? Category { get; set; }

        /// <summary>按测试顺序排列的模块 Id 列表</summary>
        public List<string> ModuleIds { get; set; } = new();
    }
}
