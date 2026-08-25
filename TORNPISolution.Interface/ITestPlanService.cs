using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 测试方案保存/加载服务接口
    /// </summary>
    public interface ITestPlanService
    {
        /// <summary>获取所有已保存的方案名称</summary>
        IReadOnlyList<string> GetPlanNames();

        /// <summary>按名称加载方案配置</summary>
        TestPlanConfig? LoadPlan(string planName);

        /// <summary>保存方案配置（同名覆盖）</summary>
        void SavePlan(TestPlanConfig config);

        /// <summary>删除方案</summary>
        void DeletePlan(string planName);
    }
}
