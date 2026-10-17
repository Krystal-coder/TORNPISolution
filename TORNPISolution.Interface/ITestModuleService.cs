using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 测试模块规格服务接口
    /// </summary>
    public interface ITestModuleService
    {
        /// <summary>获取所有可用的测试模块定义（含各测试项的规格 Min/Max）</summary>
        IReadOnlyList<TestModuleModel> GetAllModules();
    }
}
