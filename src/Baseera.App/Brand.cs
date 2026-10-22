namespace Baseera.App;

internal static class Brand
{
    internal static FontFamily ArabicFont { get; } = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Elgharib-");
    internal static FontFamily InterfaceFont { get; } = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Baseera UI");
    internal static DrawingImage WindowIcon { get; } = CreateWindowIcon();

    private static DrawingImage CreateWindowIcon()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(42, 48, 54)), null,
            new RectangleGeometry(new Rect(0, 0, 32, 32), 6, 6)));
        drawing.Children.Add(new GeometryDrawing(null, new System.Windows.Media.Pen(Brushes.White, 1.6),
            Geometry.Parse("M5,16 Q16,4 27,16 Q16,28 5,16 Z M20,16 A4,4 0 1 1 12,16 A4,4 0 1 1 20,16")));
        var icon = new DrawingImage(drawing); icon.Freeze(); return icon;
    }
}
