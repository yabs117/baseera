using System.Windows.Media.Imaging;

namespace Baseera.App;

internal static class Brand
{
    internal static FontFamily ArabicFont { get; } = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Elgharib-");
    internal static FontFamily InterfaceFont { get; } = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Baseera UI");
    internal static BitmapImage Logo { get; } = new(new Uri("pack://application:,,,/Assets/baseera-logo.png"));

    internal static FrameworkElement CreateLogo(double size = 44)
    {
        var image = new System.Windows.Controls.Image { Source = Logo, Width = size, Height = size, Stretch = Stretch.Uniform };
        image.Clip = new RectangleGeometry(new Rect(0, 0, size, size), 9, 9);
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        System.Windows.Automation.AutomationProperties.SetName(image, "Baseera");
        return image;
    }
}
