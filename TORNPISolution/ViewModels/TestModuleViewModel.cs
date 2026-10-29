using System.Collections.ObjectModel;
using TORNPISolution.Common;
using TORNPISolution.Share.Enum;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 测试模块 ViewModel：模块信息 + 测试项集合 + 模块级判定结果
    /// </summary>
    public class TestModuleViewModel : ViewModelBase
    {
        private readonly TestModuleModel _model;
        private int _sequence;
        private double? _runDurationSeconds;

        public TestModuleViewModel(TestModuleModel model)
        {
            _model = model;
            Items = new ObservableCollection<TestItemViewModel>(
                model.Items.Select(item => new TestItemViewModel(item)));

            foreach (var item in Items)
            {
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(TestItemViewModel.Result))
                    {
                        // 任一测试项结果变化时刷新模块级结果
                        OnPropertyChanged(nameof(ModuleResult));
                        OnPropertyChanged(nameof(ModuleResultText));
                    }
                };
            }
        }

        /// <summary>模块唯一标识</summary>
        public string Id => _model.Id;

        /// <summary>所属工位类别（整机/单板）：模块库按它决定这个模块显示在哪一面</summary>
        public ModuleCategory Category => _model.Category;

        /// <summary>底层模块定义（供 TestItem 按方案序号执行整个模块时使用）</summary>
        public TestModuleModel Model => _model;

        /// <summary>在测试方案中的执行序号（从 1 开始，由 MainViewModel 在方案变化时统一刷新）</summary>
        public int Sequence
        {
            get => _sequence;
            set => SetProperty(ref _sequence, value);
        }

        /// <summary>本轮大项用时（秒）：模块派发到收尾回填的实测墙钟，由 MainViewModel 在模块跑完后写入；null = 本轮没跑到</summary>
        public double? RunDurationSeconds
        {
            get => _runDurationSeconds;
            set => SetProperty(ref _runDurationSeconds, value);
        }

        /// <summary>模块名称</summary>
        public string Name => _model.Name;

        /// <summary>模块说明</summary>
        public string Description => _model.Description;

        /// <summary>本模块的测试项集合</summary>
        public ObservableCollection<TestItemViewModel> Items { get; }

        /// <summary>测试项数量显示</summary>
        public string ItemCountText => $"{Items.Count} 项";

        public TestResultStatus ModuleResult
        {
            get
            {
                var judgedItems = Items.Where(i => !i.IsReportedNotJudged && !i.IsDisplayOnly).ToList();
                if (judgedItems.Count == 0) return TestResultStatus.NotTested;

                if (judgedItems.Any(i => i.Result == TestResultStatus.Fail))
                {
                    return TestResultStatus.Fail;
                }

                return judgedItems.All(i => i.Result == TestResultStatus.Pass)
                    ? TestResultStatus.Pass
                    : TestResultStatus.NotTested;
            }
        }

        public string ModuleResultText => ModuleResult switch
        {
            TestResultStatus.Pass => "PASS",
            TestResultStatus.Fail => "FAIL",
            _ => "未测试"
        };

        /// <summary>清除本模块所有测试项的测试值</summary>
        public void Reset()
        {
            RunDurationSeconds = null;
            foreach (var item in Items)
            {
                item.Reset();
            }
        }

        /// <summary>基于同一模块定义创建新实例（加入测试方案时使用，保证方案内模块与模块库模板互不影响）</summary>
        public TestModuleViewModel CreateInstance() => new(_model);
    }
}
