using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Views
{
    /// <summary>
    /// Oscillation Shape 包络曲线预览 ：横轴 Manometer(mmHg) 0~360、纵轴 Pulse(%) 0~100。
    /// 由 Customized 方法的 x 轴八点（SP2/PS/SYS/MAP2/MAP1/DIAS/PD/SP1）与 y 轴五点比率生成钟形包络：
    /// SP2/SP1 取 0%，PS/PD 取 Offset 比率，SYS/MAP2/MAP1/DIAS 各取对应比率；
    /// 标记按官方配色（SYS Rate 蓝 / Max Pulse 2 紫 / Max Pulse 1 品红 / DIAS Rate 绿 / Offset 橙 / SP 灰圈），
    /// 参数变化时调用 Refresh 重绘。
    /// </summary>
    public partial class OscillationShapeChart : UserControl
    {
        /// <summary>包络参数数据源（BpaDynamicBpParams）</summary>
        public static readonly DependencyProperty ParamsProperty = DependencyProperty.Register(
            nameof(Params), typeof(BpaDynamicBpParams), typeof(OscillationShapeChart),
            new PropertyMetadata(null, (d, _) => ((OscillationShapeChart)d).Refresh()));

        // 绘图区内边距：左侧/底部放刻度数字，顶部放点名标签
        private const double PadLeft = 34, PadBottom = 24, PadTop = 32, PadRight = 14;

        /// <summary>横轴上限（官方图示 0~360；参数取值范围仍是 0~299）</summary>
        private const double XAxisMax = 360;

        // 标记配色（与官方图例一致）
        private static readonly Brush CurveBrush = Frozen(0x15, 0x65, 0xC0);   // 蓝：曲线与 SYS Rate
        private static readonly Brush Map2Brush = Frozen(0x8E, 0x24, 0xAA);    // 紫：Max Pulse 2（MAP2 比率）
        private static readonly Brush Map1Brush = Frozen(0xD8, 0x1B, 0x60);    // 品红：Max Pulse 1（MAP1 比率）
        private static readonly Brush DiasBrush = Frozen(0x43, 0xA0, 0x47);    // 绿：DIAS Rate
        private static readonly Brush OffsetBrush = Frozen(0xFF, 0x98, 0x00);  // 橙：Offset（PS/PD 处）
        private static readonly Brush RingBrush = Frozen(0x9E, 0x9E, 0x9E);    // 灰：SP2/SP1 空心圈
        private static readonly Brush AxisBrush = Frozen(0xBD, 0xBD, 0xBD);    // 坐标轴/刻度/引导线
        private static readonly Brush LabelBrush = Frozen(0x75, 0x75, 0x75);   // 刻度数字

        public OscillationShapeChart()
        {
            InitializeComponent();
            SizeChanged += (_, _) => Refresh();
        }

        public BpaDynamicBpParams? Params
        {
            get => (BpaDynamicBpParams?)GetValue(ParamsProperty);
            set => SetValue(ParamsProperty, value);
        }

        /// <summary>按当前包络参数与控件尺寸重绘坐标轴、曲线与彩色标记</summary>
        public void Refresh()
        {
            if (RootCanvas == null)
            {
                return;
            }

            RootCanvas.Children.Clear();

            var p = Params;
            var w = ActualWidth;
            var h = ActualHeight;
            if (p == null || w <= PadLeft + PadRight || h <= PadTop + PadBottom)
            {
                return;
            }

            var x0 = PadLeft;
            var y0 = PadTop;
            var drawW = w - PadLeft - PadRight;
            var drawH = h - PadTop - PadBottom;
            var yBase = y0 + drawH;

            double MapX(double mmHg) => x0 + Math.Clamp(mmHg, 0, XAxisMax) / XAxisMax * drawW;
            double MapY(double rate) => y0 + (1 - Math.Clamp(rate, 0, 100) / 100) * drawH;

            // ① 坐标轴 + 刻度（横轴每 50 mmHg，纵轴每 20 %）
            AddLine(x0, y0, x0, yBase, AxisBrush, 1);
            AddLine(x0, yBase, x0 + drawW, yBase, AxisBrush, 1);
            for (var t = 0; t <= XAxisMax; t += 50)
            {
                var tx = MapX(t);
                AddLine(tx, yBase, tx, yBase + 4, AxisBrush, 1);
                AddLabel(t.ToString(), tx, yBase + 6, LabelBrush, Horizontal.Center);
            }
            for (var t = 0; t <= 100; t += 20)
            {
                AddLabel(t.ToString(), x0 - 6, MapY(t) - 6, LabelBrush, Horizontal.Right);
            }

            // ② 八个控制点：SP2/SP1 落 0%，PS/PD 取 Offset 比率，中间四点各取对应比率
            var controls = new (string Name, double MmHg, double Rate, Brush Brush, bool Ring)[]
            {
                ("SP2",  p.SettingPoint2,         0,                    RingBrush,   true),
                ("PS",   p.StartPressure,         p.OffsetRate,         OffsetBrush, false),
                ("SYS",  p.Systolic,              p.SystolicRate,       CurveBrush,  false),
                ("MAP2", p.MeanArterialPressure2, p.MeanAriterialRate2, Map2Brush,   false),
                ("MAP1", p.MeanArterialPressure1, p.MeanAriterialRate1, Map1Brush,   false),
                ("DIAS", p.Diastolic,             p.DiastolicRate,      DiasBrush,   false),
                ("PD",   p.EndPressure,           p.OffsetRate,         OffsetBrush, false),
                ("SP1",  p.SettingPoint1,         0,                    RingBrush,   true)
            };

            // 按压力升序绘制（参数乱序时曲线仍成立）
            var sorted = controls.OrderBy(c => c.MmHg).ToArray();

            // ③ 标记到横轴的竖向虚线引导线（官方样式）
            foreach (var c in sorted)
            {
                var px = MapX(c.MmHg);
                AddLine(px, MapY(c.Rate), px, yBase, AxisBrush, 1, dash: true);
            }

            // ④ 包络曲线
            var polyline = new Polyline { Stroke = CurveBrush, StrokeThickness = 1.8 };
            foreach (var c in sorted)
            {
                polyline.Points.Add(new Point(MapX(c.MmHg), MapY(c.Rate)));
            }
            RootCanvas.Children.Add(polyline);

            // ⑤ 彩色标记 + 顶部点名标签（奇偶错两行，避免相邻点重叠）
            for (var i = 0; i < sorted.Length; i++)
            {
                var c = sorted[i];
                var px = MapX(c.MmHg);
                var py = MapY(c.Rate);

                var dot = new Ellipse { Width = 8, Height = 8 };
                if (c.Ring)
                {
                    dot.Fill = Brushes.White;
                    dot.Stroke = c.Brush;
                    dot.StrokeThickness = 1.6;
                }
                else
                {
                    dot.Fill = c.Brush;
                }
                Canvas.SetLeft(dot, px - 4);
                Canvas.SetTop(dot, py - 4);
                RootCanvas.Children.Add(dot);

                AddLabel(c.Name, px, i % 2 == 0 ? 2 : 16, c.Ring ? RingBrush : c.Brush, Horizontal.Center);
            }
        }

        private void AddLine(double x1, double y1, double x2, double y2, Brush brush, double thickness, bool dash = false)
        {
            var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = thickness };
            if (dash)
            {
                line.StrokeDashArray = new DoubleCollection { 2, 3 };
            }
            RootCanvas.Children.Add(line);
        }

        /// <summary>画文本标签：Center 以 x 为中心、Right 以 x 为右缘对齐</summary>
        private void AddLabel(string text, double x, double y, Brush color, Horizontal align)
        {
            var tb = new TextBlock { Text = text, FontSize = 10, Foreground = color };
            RootCanvas.Children.Add(tb);
            if (align != Horizontal.Left)
            {
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(tb, align == Horizontal.Center ? x - tb.DesiredSize.Width / 2 : x - tb.DesiredSize.Width);
            }
            else
            {
                Canvas.SetLeft(tb, x);
            }
            Canvas.SetTop(tb, y);
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private enum Horizontal { Left, Center, Right }
    }
}
