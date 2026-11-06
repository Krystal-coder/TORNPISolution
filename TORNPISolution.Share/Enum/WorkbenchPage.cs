namespace TORNPISolution.Share.Enum
{
    /// <summary>
    /// 主窗口的页面：启动落在工位选择页，点哪个进哪个，进去了就是整屏那个工位的界面。
    /// 烧录根本不走测试方案那套（另一套治具、另一份配置、另一条执行链），所以与两个测试工位平级成一层页面，
    /// 既不并入模块库的正面/背面，也不做顶部并排页
    /// </summary>
    public enum WorkbenchPage
    {
        /// <summary>整机测试工位</summary>
        WholeMachine = 0,

        /// <summary>单板测试工位</summary>
        Board = 1,

        /// <summary>烧录工位</summary>
        Flashing = 2,

        /// <summary>工位选择页：三个工位入口摆在这一页，启动就落在这里，各工位页的返回键也回到这里</summary>
        Entry = 3,

        /// <summary>加速度充电检查：整机工位下的辅助功能页，不拖模块、不进方案、不产测试项</summary>
        AccelCheck = 4,

        /// <summary>ECG 波形采集：与加速度充电检查同层的辅助功能页，只看波形与导 CSV，不进方案、不产测试项</summary>
        EcgCapture = 5,
    }

    /// <summary>页面显示名与类别映射：菜单标题、确认提示、日志都从这里取，免得中文文案散成几份</summary>
    public static class WorkbenchPageExtensions
    {
        public static string ToDisplay(this WorkbenchPage page) => page switch
        {
            WorkbenchPage.Board => "单板测试",
            WorkbenchPage.Flashing => "烧录",
            WorkbenchPage.Entry => "工位选择",
            WorkbenchPage.AccelCheck => "加速度充电检查",
            WorkbenchPage.EcgCapture => "ECG 波形采集",
            _ => "整机测试",
        };

        /// <summary>
        /// 这一页对应的模块类别：烧录与选择页不列测试模块、没有类别
        /// </summary>
        public static bool TryCategory(this WorkbenchPage page, out ModuleCategory category)
        {
            category = page switch
            {
                WorkbenchPage.Board => ModuleCategory.Board,
                WorkbenchPage.WholeMachine => ModuleCategory.WholeMachine,
                _ => ModuleCategory.WholeMachine,
            };
            return page is WorkbenchPage.Board or WorkbenchPage.WholeMachine;
        }

        /// <summary>测试模块类别落在哪一页：加载到单板方案时要连页面一起换过去</summary>
        public static WorkbenchPage ToPage(this ModuleCategory category) => category switch
        {
            ModuleCategory.Board => WorkbenchPage.Board,
            _ => WorkbenchPage.WholeMachine,
        };
    }
}
