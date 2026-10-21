using System.Collections.ObjectModel;
using TORNPISolution.Common;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 单板页通道卡片：一行 = 一项测试 + 实测值 + 规格（与烧录卡列语义无交集，别抽公共基类）。
    /// </summary>
    public class BoardChannelCardViewModel : ViewModelBase
    {
        private readonly BoardChannelSettings _settings;
        private readonly List<string> _trace = new();

        private bool _enabled;
        private bool _inRound = true;
        private bool _isBusy;
        private bool _selected = true;
        private string _deviceText = string.Empty;
        private bool? _success;
        private string _elapsedText = string.Empty;
        private IReadOnlyList<string> _traceView = Array.Empty<string>();

        public BoardChannelCardViewModel(int channel, BoardChannelSettings settings)
        {
            Channel = channel;
            _settings = settings;
            _enabled = settings.Enabled;
        }

        /// <summary>通道号就是测试槽位号（第一路是槽位 1），与 B02/B03/B04 上报的"-槽位号"后缀同一口径</summary>
        public int Channel { get; }

        public string Title => $"通道 {Channel}";

        /// <summary>位号（在菜单「配置 → 单板配置」的通道表里改，随 board-test.json 落盘）</summary>
        public string Name
        {
            get => _settings.Name;
            set
            {
                if (_settings.Name == value) return;

                _settings.Name = value;
                OnPropertyChanged();
            }
        }

        /// <summary>本通道是否参与整批：关掉的那一路整批时跳过，单独点名仍可跑</summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (!SetProperty(ref _enabled, value)) return;
                _settings.Enabled = value;
                // 灰不灰看的就是这个开关（见 Visual），不补一句通知改完配置卡片不会跟着变
                OnPropertyChanged(nameof(Visual));
            }
        }

        /// <summary>本轮要不要跑这一路。与 <see cref="Enabled"/> 是两件事：那个是这条道接没接（落盘），这个是本轮跑不跑（不落盘，跑完还是默认全跑）；与烧录页同一套双开关约定。</summary>
        public bool Selected
        {
            get => _selected;
            set
            {
                if (!SetProperty(ref _selected, value)) return;

                // 卡片灰不灰现在就看这个勾：没勾上的路才该灰下去
                OnPropertyChanged(nameof(Visual));
            }
        }

        /// <summary>这一路落在哪几台设备上：只读，由页 VM 按仪器配置算好填进来（配置页的通道表逐行念它）</summary>
        public string DeviceText
        {
            get => _deviceText;
            set => SetProperty(ref _deviceText, value);
        }

        /// <summary>这一路在不在本轮点名的范围里（右键单跑一路时其余几路就不在）：只决下方详情区那句空态提示怎么说（“没点名”与“点名了但没可对上的设备”是两回事），不参与卡片配色。</summary>
        public bool InRound
        {
            get => _inRound;
            set => SetProperty(ref _inRound, value);
        }

        /// <summary>这一轮的测试项清单：开轮前按"勾了哪些项 + 目录里的项名"重建，一轮一个对象</summary>
        public ObservableCollection<TestItemViewModel> Items { get; } = new();

        /// <summary>本轮这一路有没有落定（决定角标显不显示 PASS/FAIL）</summary>
        public bool? Success
        {
            get => _success;
            private set
            {
                if (!SetProperty(ref _success, value)) return;

                OnPropertyChanged(nameof(ResultText));
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(Visual));
            }
        }

        public string ResultText => Success switch
        {
            true => "PASS",
            false => "FAIL",
            _ => IsBusy ? "测试中" : "未测试",
        };

        /// <summary>角标只在落定后出现：没结论时"测试中/未测试"下面那几行已经写过</summary>
        public bool HasResult => Success != null;

        /// <summary>正在跑这一路（跑起来锁住勾选与配置）</summary>
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public string ElapsedText
        {
            get => _elapsedText;
            private set => SetProperty(ref _elapsedText, value);
        }

        /// <summary>这一路的往来明细（每项一行：项名、实测值、规格与结论），详情页吃它</summary>
        public IReadOnlyList<string> Trace
        {
            get => _traceView;
            private set
            {
                if (!SetProperty(ref _traceView, value)) return;

                OnPropertyChanged(nameof(HasTrace));
            }
        }

        /// <summary>这一路有没有往来明细：没跑过时一句都没有，那条折叠框就不该占着位置</summary>
        public bool HasTrace => _trace.Count > 0;

        /// <summary>卡片配色语义：灰只讲这一路本轮不跑（这条道没接，或卡片上「本轮跑」被取消了）</summary>
        public ChannelVisual Visual => Success switch
        {
            true => ChannelVisual.Success,
            false => ChannelVisual.Failed,
            _ when IsBusy => ChannelVisual.Running,
            _ when !Enabled || !Selected => ChannelVisual.Skipped,
            _ => ChannelVisual.Idle,
        };

        /// <summary>开轮：清掉上一轮结论，勾上"测试中"</summary>
        public void BeginRun()
        {
            _trace.Clear();
            Success = null;
            ElapsedText = string.Empty;
            Trace = Array.Empty<string>();
            foreach (var item in Items) item.Reset();
            IsBusy = true;
            AddTrace($"{Items.Count} 项开始");
        }

        /// <summary>这一路没参与本轮（或已退回未开跑）：卡片留白并说清是没参与而不是没测出来，不标假 PASS；耗时一并抹掉，否则上一轮的秒数挂在重新建好的行旁边会让人以为这一轮已经跑过。</summary>
        public void MarkIdle()
        {
            IsBusy = false;
            Success = null;
            ElapsedText = string.Empty;
            foreach (var item in Items) item.Reset();
            _trace.Clear();
            Trace = Array.Empty<string>();
            OnPropertyChanged(nameof(Visual));
        }

        /// <summary>收这一路：没上报的项按未上报判不过，再按各项结论给整路定 PASS/FAIL</summary>
        public void FinishRun(bool forceFail = false, string? reason = null)
        {
            if (!string.IsNullOrWhiteSpace(reason)) AddTrace(reason);

            // 一项都没跑成时不能算过：执行端整个模块抛异常、或这一路连不上设备，都会走到这里
            foreach (var item in Items.Where(i => i.Result == TestResultStatus.NotTested))
            {
                item.MarkNotReportedAsFail();
                AddTrace($"{item.Name} 未上报结果，判 FAIL");
            }

            var passed = !forceFail && Items.Count > 0 && Items.All(i => i.Result == TestResultStatus.Pass);
            Success = passed;
            IsBusy = false;
            AddTrace(passed ? "本轮 PASS" : "本轮 FAIL");
            OnPropertyChanged(nameof(Visual));
        }

        /// <summary>追加一行明细。整份换掉而不是增量通知：详情页的 ItemsControl 换引用最省事，也不会漏行</summary>
        public void AddTrace(string line)
        {
            _trace.Add($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
            Trace = _trace.ToArray();
        }

        /// <summary>刷这一路的耗时文字</summary>
        public void SetElapsed(TimeSpan elapsed)
            => ElapsedText = elapsed.TotalSeconds >= 0.1 ? $"{elapsed.TotalSeconds:0.#}s" : string.Empty;
    }
}
