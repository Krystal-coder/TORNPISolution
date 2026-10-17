namespace TORNPISolution.Share.Enum
{
    /// <summary>
    /// 测试模块所属工位类别：整机与单板各有独立治具与电脑，一份测试方案只属于一类，不混装。
    /// 类别由 Spec 目录逐模块声明（见 TestModuleModel.Category），UI 只按本字段过滤，不按模块 Id 前缀猜。
    /// </summary>
    public enum ModuleCategory
    {
        /// <summary>整机工位模块</summary>
        WholeMachine = 0,

        /// <summary>单板工位模块</summary>
        Board = 1,
    }

    /// <summary>类别显示名：界面标题、确认提示、日志都用这一处，免得中文文案散成两份</summary>
    public static class ModuleCategoryExtensions
    {
        public static string ToDisplay(this ModuleCategory category) => category switch
        {
            ModuleCategory.Board => "单板模块",
            _ => "整机模块",
        };

        /// <summary>简短名（用于拼接提示语，如“切到单板”）</summary>
        public static string ToShortDisplay(this ModuleCategory category) => category switch
        {
            ModuleCategory.Board => "单板",
            _ => "整机",
        };
    }
}
