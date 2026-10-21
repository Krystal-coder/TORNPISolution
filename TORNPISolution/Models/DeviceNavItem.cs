using MaterialDesignThemes.Wpf;

namespace TORNPISolution.Models
{
    /// <summary>仪器链接配置页左侧导航列表项</summary>
    public class DeviceNavItem
    {
        /// <summary>设备标识（与右侧详情面板的 DataTrigger 值对应）</summary>
        public required string Key { get; init; }

        /// <summary>左侧列表显示名称</summary>
        public required string Name { get; init; }

        /// <summary>所属类别（分组标题）</summary>
        public required string Category { get; init; }

        public PackIconKind Icon { get; init; }
    }
}
