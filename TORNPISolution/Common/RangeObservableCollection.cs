using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace TORNPISolution.Common
{
    /// <summary>
    /// 支持整批替换的 ObservableCollection：ReplaceRange 期间压掉逐条通知，整表只发一次 Reset。
    /// 波形显示序列每帧重建一次（40fps 下逐条通知会把界面压死），绑 ItemsControl 的其余用法不受影响。
    /// </summary>
    public class RangeObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppress;

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppress) base.OnCollectionChanged(e);
        }

        /// <summary>用一批新点整表替换，只发一次 Reset 通知</summary>
        public void ReplaceRange(IEnumerable<T> items)
        {
            _suppress = true;
            Clear();
            foreach (var item in items) Add(item);
            _suppress = false;

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
