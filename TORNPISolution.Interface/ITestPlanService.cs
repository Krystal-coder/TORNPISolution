using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>测试方案保存/加载服务接口</summary>
    public interface ITestPlanService
    {
        /// <summary>获取所有已保存的方案名称</summary>
        IReadOnlyList<string> GetPlanNames();

        /// <summary>一次读盘取回全部方案配置（键=方案名，即文件名）：供模块库按当前工位类别过滤方案列表；解析不了的文件跳过</summary>
        IReadOnlyDictionary<string, TestPlanConfig> GetAllPlans();

        /// <summary>按名称加载方案配置</summary>
        TestPlanConfig? LoadPlan(string planName);

        /// <summary>保存方案配置（同名覆盖）</summary>
        void SavePlan(TestPlanConfig config);

        /// <summary>删除方案</summary>
        void DeletePlan(string planName);
    }
}
