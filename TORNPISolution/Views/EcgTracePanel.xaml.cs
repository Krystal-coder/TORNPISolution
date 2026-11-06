using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiveChartsCore.Drawing;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 波形面板：一路通道的绘图区与覆盖层（框选矩形、十字标）。轴设置与统计文案绑在 EcgTraceChannel 上，
    /// 采样与算法都在页面的同一份缓冲里，所以 ADC 与电压两路各自实例化一个本控件即可，不再有第二套鼠标处理。
    /// 覆盖层按数据坐标钉住（绝对采样序号），窗口滚动时矩形和十字标跟着走，不是钉在像素上。
    /// </summary>
    public partial class EcgTracePanel : UserControl
    {
        /// <summary>框选最短跨度（采样点）：比这还窄多半是手抖点了一下，按清除处理</summary>
        private const double MinSelectionSamples = 2;

        private EcgTraceChannel? _channel;
        private bool _isSelecting;
        private double _anchorIndex;
        private double _moveIndex;
        private double _selectionStart = double.NaN;
        private double _selectionEnd = double.NaN;

        public EcgTracePanel()
        {
            InitializeComponent();

            DataContextChanged += (_, _) => AttachChannel();
            AttachChannel();

            // 覆盖层跟着布局走：滚轮缩放、拖动平移、波形贴着尾部滚动都会改像素位置，比定时器准也不空转
            LayoutUpdated += (_, _) => RepaintOverlay();
        }

        private void AttachChannel()
        {
            if (ReferenceEquals(_channel, DataContext as EcgTraceChannel)) return;

            if (_channel != null) _channel.PropertyChanged -= OnChannelPropertyChanged;
            _channel = DataContext as EcgTraceChannel;
            if (_channel != null) _channel.PropertyChanged += OnChannelPropertyChanged;

            ClearOverlay();
        }

        private void OnChannelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 「清除框选」「换段」「清空」都在通道上把 HasSelection 拨回去，矩形由这里同步收掉
            if (e.PropertyName == nameof(EcgTraceChannel.HasSelection) && _channel?.HasSelection != true)
                _selectionStart = double.NaN;
        }

        // ==================== 框选 ====================

        private void Chart_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 不按 Shift 的左键留给图表平移，否则缩放过就再也拖不动波形
            if (_channel == null || (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.None) return;

            _anchorIndex = _moveIndex = IndexAt(e.GetPosition(Chart));
            _isSelecting = true;
            _channel.ShowCursor(null, null);
            Chart.CaptureMouse();
            e.Handled = true;
            RepaintOverlay();
        }

        private void Chart_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndSelection();
                return;
            }

            _moveIndex = IndexAt(e.GetPosition(Chart));
            e.Handled = true;
            RepaintOverlay();
        }

        private void Chart_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting) return;

            EndSelection();
            e.Handled = true;
        }

        private void EndSelection()
        {
            _isSelecting = false;
            Chart.ReleaseMouseCapture();
            if (_channel == null) return;

            var start = Math.Min(_anchorIndex, _moveIndex);
            var end = Math.Max(_anchorIndex, _moveIndex);
            if (end - start < MinSelectionSamples)
            {
                _channel.ClearSelection();
                return;
            }

            _selectionStart = start;
            _selectionEnd = end;
            _channel.Host.ApplyRegionSelection(_channel, start, end);
            RepaintOverlay();
        }

        // ==================== 十字标 ====================

        private void Chart_MouseMove(object sender, MouseEventArgs e)
        {
            if (_channel == null || _isSelecting || !IsChartMeasurable) return;

            var pos = e.GetPosition(Chart);
            var p0 = Chart.ScaleDataToPixels(new LvcPointD(0, 0));
            var p1 = Chart.ScaleDataToPixels(new LvcPointD(1, 1));
            var pxPerX = Math.Abs(p1.X - p0.X);
            var pxPerY = Math.Abs(p1.Y - p0.Y);
            if (pxPerX <= 0 || pxPerY <= 0) return;

            var data = Chart.ScalePixelsToData(new LvcPointD(pos.X, pos.Y));
            // 横向先按 4 px 邻域取候选，再在候选里比二维像素距离：500 SPS 上一格挨着好几个点，只比横会读出离波形很远的值
            _channel.Host.UpdateCursor(_channel, data.X, data.Y, pxPerX, pxPerY, Math.Min(4.0 / pxPerX, 2000));
            RepaintOverlay();
        }

        private void Chart_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isSelecting || _channel == null) return;

            _channel.ShowCursor(null, null);
            RepaintOverlay();
        }

        // ==================== 覆盖层 ====================

        private bool IsChartMeasurable => Chart.ActualWidth > 0 && Chart.ActualHeight > 0;

        private double IndexAt(Point pos) => Chart.ScalePixelsToData(new LvcPointD(pos.X, pos.Y)).X;

        private void RepaintOverlay()
        {
            var channel = _channel;
            if (channel == null || !IsChartMeasurable) return;
            if (!_isSelecting && double.IsNaN(_selectionStart) && channel.CursorSample == null) return;

            RepaintSelection(channel);
            RepaintCursor(channel);
        }

        private void RepaintSelection(EcgTraceChannel channel)
        {
            var start = _isSelecting ? Math.Min(_anchorIndex, _moveIndex) : _selectionStart;
            var end = _isSelecting ? Math.Max(_anchorIndex, _moveIndex) : _selectionEnd;

            if (double.IsNaN(start) || double.IsNaN(end)
                || (_isSelecting ? end - start < 0.01 : !channel.HasSelection))
            {
                SelectionRect.Visibility = Visibility.Collapsed;
                return;
            }

            var left = Chart.ScaleDataToPixels(new LvcPointD(start, 0)).X;
            var right = Chart.ScaleDataToPixels(new LvcPointD(end, 0)).X;
            Canvas.SetLeft(SelectionRect, left);
            SelectionRect.Width = Math.Max(Math.Abs(right - left), 1);
            Canvas.SetTop(SelectionRect, 0);
            SelectionRect.Height = Chart.ActualHeight;
            SelectionRect.Visibility = Visibility.Visible;
        }

        private void RepaintCursor(EcgTraceChannel channel)
        {
            if (channel.CursorSample == null || channel.CursorValue == null)
            {
                CursorVLine.Visibility = Visibility.Collapsed;
                CursorHLine.Visibility = Visibility.Collapsed;
                CursorMarker.Visibility = Visibility.Collapsed;
                return;
            }

            var px = Chart.ScaleDataToPixels(new LvcPointD(channel.CursorSample.Value, channel.CursorValue.Value));
            CursorVLine.X1 = px.X;
            CursorVLine.X2 = px.X;
            CursorVLine.Y1 = 0;
            CursorVLine.Y2 = Chart.ActualHeight;
            CursorHLine.X1 = 0;
            CursorHLine.X2 = Chart.ActualWidth;
            CursorHLine.Y1 = px.Y;
            CursorHLine.Y2 = px.Y;
            Canvas.SetLeft(CursorMarker, px.X - CursorMarker.Width / 2);
            Canvas.SetTop(CursorMarker, px.Y - CursorMarker.Height / 2);
            CursorVLine.Visibility = Visibility.Visible;
            CursorHLine.Visibility = Visibility.Visible;
            CursorMarker.Visibility = Visibility.Visible;
        }

        private void ClearOverlay()
        {
            _selectionStart = double.NaN;
            _selectionEnd = double.NaN;
            SelectionRect.Visibility = Visibility.Collapsed;
            CursorVLine.Visibility = Visibility.Collapsed;
            CursorHLine.Visibility = Visibility.Collapsed;
            CursorMarker.Visibility = Visibility.Collapsed;
        }
    }
}
