using System.Runtime.CompilerServices;
using TORNPISolution.Common;
using TORNPISolution.Share.Models;

namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// Oscillation Shape（包络曲线）编辑 ViewModel：
    /// x 轴八点（含收缩压/舒张压）+ y 轴五点比率共 13 个参数以桥接属性实时写回 BpaDynamicBpParams
    /// （滑块与输入框双向联动），打开时快照一份，取消时还原；
    /// 参数变化触发 ParamsChanged 供预览曲线重绘。
    /// </summary>
    public class OscillationShapeViewModel : ViewModelBase
    {
        private readonly BpaDynamicBpParams _model;

        // 打开时的快照（取消还原用）
        private double _snapSettingPoint2, _snapStartPressure, _snapSystolic,
            _snapMeanArterialPressure2, _snapMeanArterialPressure1, _snapDiastolic,
            _snapEndPressure, _snapSettingPoint1;
        private int _snapSystolicRate, _snapMeanAriterialRate1, _snapMeanAriterialRate2,
            _snapDiastolicRate, _snapOffsetRate;

        public OscillationShapeViewModel(BpaDynamicBpParams model)
        {
            _model = model;
            TakeSnapshot();
        }

        /// <summary>底层包络参数（曲线预览控件绑定）</summary>
        public BpaDynamicBpParams Model => _model;

        /// <summary>任一参数变化（窗口订阅后刷新包络曲线预览）</summary>
        public event Action? ParamsChanged;

        // ===== x 轴八点（mmHg 0~299，须满足 SP2 > PS > SYS > MAP2 > MAP1 > DIAS > PD > SP1） =====

        /// <summary>包络点 2（SP2）</summary>
        public double SettingPoint2
        {
            get => _model.SettingPoint2;
            set { _model.SettingPoint2 = value; Notify(); }
        }

        /// <summary>起始压力（PS）</summary>
        public double StartPressure
        {
            get => _model.StartPressure;
            set { _model.StartPressure = value; Notify(); }
        }

        /// <summary>收缩压（SYS，即 BPCommon Header 的 Systolic，同步动态血压页）</summary>
        public double Systolic
        {
            get => _model.Systolic;
            set { _model.Systolic = value; Notify(); }
        }

        /// <summary>平均动脉压 2（MAP2）</summary>
        public double MeanArterialPressure2
        {
            get => _model.MeanArterialPressure2;
            set { _model.MeanArterialPressure2 = value; Notify(); }
        }

        /// <summary>平均动脉压 1（MAP1）</summary>
        public double MeanArterialPressure1
        {
            get => _model.MeanArterialPressure1;
            set { _model.MeanArterialPressure1 = value; Notify(); }
        }

        /// <summary>舒张压（DIAS，即 BPCommon Header 的 Diastolic，同步动态血压页）</summary>
        public double Diastolic
        {
            get => _model.Diastolic;
            set { _model.Diastolic = value; Notify(); }
        }

        /// <summary>结束压力（PD）</summary>
        public double EndPressure
        {
            get => _model.EndPressure;
            set { _model.EndPressure = value; Notify(); }
        }

        /// <summary>包络点 1（SP1）</summary>
        public double SettingPoint1
        {
            get => _model.SettingPoint1;
            set { _model.SettingPoint1 = value; Notify(); }
        }

        // ===== y 轴五点比率（% 0~100） =====

        /// <summary>收缩压比率（SYS Rate）</summary>
        public int SystolicRate
        {
            get => _model.SystolicRate;
            set { _model.SystolicRate = value; Notify(); }
        }

        /// <summary>平均动脉压比率 1</summary>
        public int MeanAriterialRate1
        {
            get => _model.MeanAriterialRate1;
            set { _model.MeanAriterialRate1 = value; Notify(); }
        }

        /// <summary>平均动脉压比率 2</summary>
        public int MeanAriterialRate2
        {
            get => _model.MeanAriterialRate2;
            set { _model.MeanAriterialRate2 = value; Notify(); }
        }

        /// <summary>舒张压比率（DIAS Rate）</summary>
        public int DiastolicRate
        {
            get => _model.DiastolicRate;
            set { _model.DiastolicRate = value; Notify(); }
        }

        /// <summary>偏移比率（Offset）</summary>
        public int OffsetRate
        {
            get => _model.OffsetRate;
            set { _model.OffsetRate = value; Notify(); }
        }

        /// <summary>x 轴顺序校验提示（空串 = 满足，与官方头文件 BPEnvelopeCustomized 约束一致）</summary>
        public string OrderWarning =>
            SettingPoint2 > StartPressure && StartPressure > Systolic && Systolic > MeanArterialPressure2
            && MeanArterialPressure2 > MeanArterialPressure1 && MeanArterialPressure1 > Diastolic
            && Diastolic > EndPressure && EndPressure > SettingPoint1
                ? string.Empty
                : "⚠ 顺序未满足：SP2 > PS > SYS > MAP2 > MAP1 > DIAS > PD > SP1，仪器可能拒绝该包络参数";

        /// <summary>还原到打开时的快照（取消时调用）</summary>
        public void RestoreSnapshot()
        {
            _model.SettingPoint2 = _snapSettingPoint2;
            _model.StartPressure = _snapStartPressure;
            _model.Systolic = _snapSystolic;
            _model.MeanArterialPressure2 = _snapMeanArterialPressure2;
            _model.MeanArterialPressure1 = _snapMeanArterialPressure1;
            _model.Diastolic = _snapDiastolic;
            _model.EndPressure = _snapEndPressure;
            _model.SettingPoint1 = _snapSettingPoint1;
            _model.SystolicRate = _snapSystolicRate;
            _model.MeanAriterialRate1 = _snapMeanAriterialRate1;
            _model.MeanAriterialRate2 = _snapMeanAriterialRate2;
            _model.DiastolicRate = _snapDiastolicRate;
            _model.OffsetRate = _snapOffsetRate;
        }

        private void TakeSnapshot()
        {
            _snapSettingPoint2 = _model.SettingPoint2;
            _snapStartPressure = _model.StartPressure;
            _snapSystolic = _model.Systolic;
            _snapMeanArterialPressure2 = _model.MeanArterialPressure2;
            _snapMeanArterialPressure1 = _model.MeanArterialPressure1;
            _snapDiastolic = _model.Diastolic;
            _snapEndPressure = _model.EndPressure;
            _snapSettingPoint1 = _model.SettingPoint1;
            _snapSystolicRate = _model.SystolicRate;
            _snapMeanAriterialRate1 = _model.MeanAriterialRate1;
            _snapMeanAriterialRate2 = _model.MeanAriterialRate2;
            _snapDiastolicRate = _model.DiastolicRate;
            _snapOffsetRate = _model.OffsetRate;
        }

        private void Notify([CallerMemberName] string? name = null)
        {
            OnPropertyChanged(name);
            OnPropertyChanged(nameof(OrderWarning));
            ParamsChanged?.Invoke();
        }
    }
}
