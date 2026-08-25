using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 测试模块规格服务接口（由 Spec 类实现）。
    /// 后续可替换为从配置文件 / 数据库加载的实现。
    /// </summary>
    public interface ITestModuleService
    {
        /// <summary>获取所有可用的测试模块定义（含各测试项的规格 Min/Max）</summary>
        IReadOnlyList<TestModuleModel> GetAllModules();
    }
}
