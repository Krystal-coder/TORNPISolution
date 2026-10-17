using TORNPISolution.Share.Enum;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 应用工位状态服务接口
    /// </summary>
    public interface IAppStateService
    {
        /// <summary>上次停留的工位类别</summary>
        ModuleCategory LoadLastCategory();

        /// <summary>记住当前停留的工位类别</summary>
        void SaveLastCategory(ModuleCategory category);

        /// <summary>上次停留的工位页面</summary>
        WorkbenchPage LoadLastPage();

        /// <summary>记住当前停留的工位页面</summary>
        void SaveLastPage(WorkbenchPage page);
    }
}
