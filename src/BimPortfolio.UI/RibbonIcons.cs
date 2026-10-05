using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BimPortfolio.UI;

public static class RibbonIcons
{
    public static ImageSource Create(bool route, int size)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushTransform(new ScaleTransform(size / 32.0, size / 32.0));
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(24, 35, 46)), null, new Rect(0, 0, 32, 32), 6, 6);
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(85, 220, 194)), 2.5)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            if (route)
            {
                drawing.DrawLine(pen, new Point(7, 24), new Point(7, 9));
                drawing.DrawLine(pen, new Point(7, 9), new Point(24, 9));
                drawing.DrawLine(pen, new Point(24, 9), new Point(24, 23));
                drawing.DrawEllipse(Brushes.White, null, new Point(7, 24), 3, 3);
                drawing.DrawEllipse(Brushes.White, null, new Point(24, 23), 3, 3);
            }
            else
            {
                var shield = Geometry.Parse("M 16,5 L 26,9 L 24,21 L 16,27 L 8,21 L 6,9 Z");
                drawing.DrawGeometry(null, pen, shield);
                drawing.DrawLine(pen, new Point(11, 16), new Point(15, 20));
                drawing.DrawLine(pen, new Point(15, 20), new Point(22, 12));
            }
            drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
