using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// ECG Signal窗口的打开/刷新请求：一个模块一条长图，显示参数来自该模块在 Spec 目录里声明的
    /// <see cref="WaveformOverviewProfile"/>。同一模块多次加入方案时，各次加入的档位项已在
    /// <see cref="Items"/> 里按方案顺序合并好。
    /// </summary>
    public sealed class WaveformOverviewRequest
    {
        public WaveformOverviewRequest(string moduleId, string moduleTitle,
            WaveformOverviewProfile profile, IReadOnlyList<TestItemViewModel> items)
        {
            ModuleId = moduleId;
            ModuleTitle = moduleTitle;
            Profile = profile;
            Items = items;
        }

        /// <summary>模块标识：窗口按它各保一个实例，两个模块都用总览时两个窗口并存互不覆盖</summary>
        public string ModuleId { get; }

        /// <summary>模块名：profile 未给标题时用来生成窗口标题</summary>
        public string ModuleTitle { get; }

        /// <summary>显示参数（单位 / 横轴倍率 / 量程档 / 曲线名 / 段标注截断标记）</summary>
        public WaveformOverviewProfile Profile { get; }

        /// <summary>参与拼接的测试项（目录里声明为 Overview 模式的项，按执行顺序）</summary>
        public IReadOnlyList<TestItemViewModel> Items { get; }
    }
}
