namespace Baseera.App;

internal static class Brand
{
    private static readonly SolidColorBrush EyeTile = new(Color.FromRgb(42, 48, 54));
    private static readonly System.Windows.Media.Pen EyeStroke = new(Brushes.White, 1.6);
    private static readonly Geometry OpenLids = BuildLids(1);

    internal static Brush EyeBackground => EyeTile;
    internal static FontFamily ArabicFont { get; } = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Elgharib-");
    private static readonly FontFamily DefaultInterfaceFont = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Baseera UI");
    private static readonly FontFamily SimplifiedInterfaceFont = new("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
    private static readonly FontFamily TraditionalInterfaceFont = new("Microsoft JhengHei UI, Microsoft JhengHei, Segoe UI");
    internal static FontFamily InterfaceFont => FontFor(Services.Settings.Current.Language);
    internal static FontFamily FontFor(string language) => language switch
    {
        "zh-CN" => SimplifiedInterfaceFont,
        "zh-TW" => TraditionalInterfaceFont,
        _ => DefaultInterfaceFont
    };
    internal static DrawingImage WindowIcon { get; } = CreateWindowIcon();

    private static DrawingImage CreateWindowIcon()
    {
        EyeTile.Freeze();
        EyeStroke.Freeze();
        OpenLids.Freeze();
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) DrawEye(context, 1);
        var icon = new DrawingImage(drawing); icon.Freeze(); return icon;
    }

    internal static void DrawEye(DrawingContext context, double openness)
    {
        context.DrawRoundedRectangle(EyeTile, null, new Rect(0, 0, 32, 32), 6, 6);
        var lids = openness >= 1 ? OpenLids : BuildLids(openness);
        // The lids cover the iris as they close, while the stroke and tile stay the same size.
        context.PushClip(lids);
        context.DrawEllipse(null, EyeStroke, new System.Windows.Point(16, 16), 4, 4);
        context.Pop();
        context.DrawGeometry(null, EyeStroke, lids);
    }

    private static Geometry BuildLids(double openness)
    {
        const double center = 16;
        double rise = 12 * openness;
        var outline = new StreamGeometry();
        using (var path = outline.Open())
        {
            path.BeginFigure(new System.Windows.Point(5, center), isFilled: true, isClosed: true);
            path.QuadraticBezierTo(new System.Windows.Point(center, center - rise), new System.Windows.Point(27, center), true, false);
            path.QuadraticBezierTo(new System.Windows.Point(center, center + rise), new System.Windows.Point(5, center), true, false);
        }
        return outline;
    }
}
