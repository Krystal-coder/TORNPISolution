using TORNPISolution.Common;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>卡片配色语义：界面按它决定底色与角标，VM 不直接碰画笔资源</summary>
    public enum ChannelVisual
    {
        /// <summary>还没开始</summary>
        Idle,

        /// <summary>正在跑（下载、自检、擦除、写入、校验）</summary>
        Running,

        /// <summary>在等人做动作（按复位键），需要提示操作人员</summary>
        Waiting,

        /// <summary>本轮通过</summary>
        Success,

        /// <summary>本轮没通过</summary>
        Failed,

        /// <summary>没参与或被取消</summary>
        Skipped,
    }

    /// <summary>
    /// 烧录页的一张通道卡片：既是配置行（这一路用哪个串口），也是状态行（进度与结果）。
    /// 两者放同一张卡片、且开烧后锁死串口下拉：现场最容易出的错是盯着通道 3 的结果、实际烧的是配错串口的另一路，挨在一起才能当场看出错位。
    /// </summary>
    public class FlashChannelCardViewModel : ViewModelBase
    {
        private readonly FlashChannelSettings _settings;

        private bool _enabled;
        private bool _selected = true;
        private bool _isBusy;
        private bool _useWizProPort;
        private FlashStage _stage = FlashStage.Idle;
        private int _percent = -1;
        private string _detail = "等待开始";
        private bool? _success;
        private IReadOnlyList<string> _trace = Array.Empty<string>();
        private string _elapsedText = string.Empty;

        public FlashChannelCardViewModel(int channel, FlashChannelSettings settings)
        {
            Channel = channel;
            _settings = settings;
            _enabled = settings.Enabled;
            // 本轮默认跟着"参与整批"走：关掉的那一路没接板子，开烧时也就不替它勾上
            _selected = settings.Enabled;
            _wizProPortName = settings.WizProPortName;
            _freqchipPortName = settings.FreqchipPortName;
        }

        /// <summary>通道号，就是清单里的行号（第一行是通道 1）</summary>
        public int Channel { get; }

        public string Title => $"通道 {Channel}";

        /// <summary>位号（配置区可直接改，改完随 flashing.json 一起落盘）</summary>
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

        /// <summary>这一路是否参与整批（关掉后整批跳过它，单独点名仍可烧）</summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (!SetProperty(ref _enabled, value)) return;
                _settings.Enabled = value;
            }
        }

        /// <summary>本轮要不要烧这一路</summary>
        public bool Selected
        {
            get => _selected;
            set => SetProperty(ref _selected, value);
        }

        private string _wizProPortName;

        /// <summary>本通道烧录器串口（SPI Flash 用）</summary>
        public string WizProPortName
        {
            get => _wizProPortName;
            set
            {
                if (!SetProperty(ref _wizProPortName, value)) return;
                _settings.WizProPortName = value;
                OnPropertyChanged(nameof(PortLabel));
            }
        }

        private string _freqchipPortName;

        /// <summary>本通道主控烧录串口</summary>
        public string FreqchipPortName
        {
            get => _freqchipPortName;
            set
            {
                if (!SetProperty(ref _freqchipPortName, value)) return;
                _settings.FreqchipPortName = value;
                OnPropertyChanged(nameof(PortLabel));
            }
        }

        /// <summary>当前动作下这一路实际用哪个口（卡片上直接显示，不必再对照配置页）</summary>
        public string PortLabel => _useWizProPort
            ? (string.IsNullOrWhiteSpace(WizProPortName) ? "未配烧录器串口" : WizProPortName)
            : (string.IsNullOrWhiteSpace(FreqchipPortName) ? "未配主控串口" : FreqchipPortName);

        /// <summary>本轮跑的是哪个动作，决定卡片显示哪个串口</summary>
        public bool UseWizProPort
        {
            get => _useWizProPort;
            set
            {
                if (SetProperty(ref _useWizProPort, value)) OnPropertyChanged(nameof(PortLabel));
            }
        }

        /// <summary>开烧中：锁住串口下拉与勾选，避免跑一半改配置</summary>
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(IsIdle));
            }
        }

        /// <summary>没在跑（界面用它控制可编辑性）</summary>
        public bool IsIdle => !IsBusy;

        public FlashStage Stage
        {
            get => _stage;
            private set
            {
                if (!SetProperty(ref _stage, value)) return;

                OnPropertyChanged(nameof(StageText));
                OnPropertyChanged(nameof(ResultText));
                OnPropertyChanged(nameof(DetailText));
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(Visual));
            }
        }

        public int Percent
        {
            get => _percent;
            private set
            {
                if (!SetProperty(ref _percent, value)) return;

                OnPropertyChanged(nameof(PercentText));
                OnPropertyChanged(nameof(PercentValue));
                OnPropertyChanged(nameof(ShowProgress));
            }
        }

        public string Detail
        {
            get => _detail;
            private set
            {
                if (!SetProperty(ref _detail, value)) return;

                OnPropertyChanged(nameof(DetailText));
            }
        }

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

        /// <summary>这一路的明细时间线（详情页显示它）</summary>
        public IReadOnlyList<string> Trace
        {
            get => _trace;
            private set => SetProperty(ref _trace, value);
        }

        public string ElapsedText
        {
            get => _elapsedText;
            private set => SetProperty(ref _elapsedText, value);
        }

        public string StageText => Stage.ToDisplayText();

        public string PercentText => Percent >= 0 ? $"{Percent}%" : string.Empty;

        /// <summary>进度条用的数值：没给比例时按 0 画，靠 ShowProgress 隐藏整条</summary>
        public double PercentValue => Percent >= 0 ? Math.Clamp(Percent, 0, 100) : 0;

        /// <summary>设备这一歩有没有给出可算的比例</summary>
        public bool ShowProgress => Percent >= 0;

        /// <summary>结果角标：没结论时显示当前阶段，避免卡片上一片空白</summary>
        public string ResultText => Success switch
        {
            true => "PASS",
            false => "FAIL",
            _ => StageText,
        };

        /// <summary>是否已有结论：角标只在落定时显示，没落定时阶段文字已经在下面一行写过</summary>
        public bool HasResult => Success != null;

        /// <summary>明细行：与阶段同名的那句不重复写，否则开局"等待开始"会在卡片上重复三遍</summary>
        public string DetailText => Detail == StageText ? string.Empty : Detail;

        public ChannelVisual Visual => Stage switch
        {
            FlashStage.Succeeded => ChannelVisual.Success,
            FlashStage.Failed => Success == true ? ChannelVisual.Success : ChannelVisual.Failed,
            FlashStage.WaitingReset => ChannelVisual.Waiting,
            FlashStage.Skipped or FlashStage.Canceled => ChannelVisual.Skipped,
            FlashStage.Idle => ChannelVisual.Idle,
            _ => ChannelVisual.Running,
        };

        /// <summary>把编排发来的一路快照刷到卡片上</summary>
        public void Apply(ChannelFlashReport report)
        {
            Percent = report.Percent;
            Detail = report.Detail;
            Trace = report.Trace;
            ElapsedText = report.Elapsed.TotalSeconds >= 0.1 ? $"{report.Elapsed.TotalSeconds:0.#}s" : string.Empty;
            Success = report.Success;
            Stage = report.Stage;      // 放最后：它要触发的派生属性依赖上面几项已经就位
        }

        /// <summary>清成未开始（换动作、或新一轮开始前）</summary>
        public void Reset()
        {
            Percent = -1;
            Detail = "等待开始";
            Trace = Array.Empty<string>();
            ElapsedText = string.Empty;
            Success = null;
            Stage = FlashStage.Idle;
        }

        /// <summary>界面侧取消收尾：单路取消会由编排自己发一份 Canceled 快照，但“工程数据下载”被打断时整轮直接抛回来、单路没有快照，这里补上免得卡片停在“下载中”。</summary>
        public void MarkCanceled()
        {
            if (Stage is FlashStage.Idle or FlashStage.Succeeded or FlashStage.Failed
                or FlashStage.Skipped or FlashStage.Canceled) return;

            IsBusy = false;
            Success = false;
            Detail = "已取消";
            Stage = FlashStage.Canceled;
        }
    }
}
