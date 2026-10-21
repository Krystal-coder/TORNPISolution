using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace TORNPISolution.Common
{
    /// <summary>
    /// 所有 ViewModel 的基类，提供属性通知
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 设置字段值并在值变化时触发通知，返回是否发生变化
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// 让 WPF 重新计算所有命令的 CanExecute（按钮该不该亮）：RelayCommand 只挂在 CommandManager.RequerySuggested 上，WPF 只在有输入事件（点击、切焦点）时才触发它。
        /// 一轮跑完是后台回调落回 UI 线程，没有输入事件，“开始测试”“清空结果”会一直灰着，等用户点一下别处才亮。
        /// 凡是把命令从能按变成不能按（或反过来）的状态，都在它的 setter 里补一句这个。
        /// </summary>
        protected static void RequeryCommands()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            // 不保证调用方就站在 UI 线程上（收轮那条路径多处从采集线程回来），而 CommandManager 只认当前线程的调度器
            if (dispatcher.CheckAccess()) CommandManager.InvalidateRequerySuggested();
            else dispatcher.BeginInvoke(new Action(CommandManager.InvalidateRequerySuggested));
        }
    }
}
