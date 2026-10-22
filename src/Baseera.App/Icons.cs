using System.Windows.Shapes;

namespace Baseera.App;

public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["minimize"] = "M2,12 L22,12",
        ["maximize"] = "M3,3 L21,3 L21,21 L3,21 Z",
        ["close"] = "M3,3 L21,21 M21,3 L3,21",
        ["moon"] = "M20,15 A9,9 0 1 1 9,3 A8,8 0 0 0 20,15 Z",
        ["sun"] = "M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12 M12,1 L12,4 M12,20 L12,23 M1,12 L4,12 M20,12 L23,12 M4,4 L6,6 M18,18 L20,20 M4,20 L6,18 M18,6 L20,4",
        ["menu"] = "M3,6 L21,6 M3,12 L21,12 M3,18 L21,18",
        ["home"] = "M3,10 L12,3 L21,10 L21,21 L15,21 L15,14 L9,14 L9,21 L3,21 Z",
        ["scan"] = "M12,2 L21,6 L20,14 Q18,20 12,23 Q6,20 4,14 L3,6 Z M8,12 L11,15 L16,9",
        ["translate"] = "M3,5 L16,5 M9,2 L9,5 M6,5 Q7,14 15,17 M14,5 Q12,13 3,17 M14,22 L18,12 L22,22 M15.5,18 L20.5,18",
        ["updates"] = "M20,8 A9,9 0 0 0 4,7 M4,2 L4,7 L9,7 M4,16 A9,9 0 0 0 20,17 M15,17 L20,17 L20,22",
        ["convert"] = "M3,3 L17,3 L17,17 L3,17 Z M3,14 L8,9 L12,13 L14,11 L17,14 M7,7 L7.1,7 M8,21 L21,21 L21,8",
        ["settings"] = "M10,2 L14,2 L15,5 L18,6 L21,5 L23,9 L20,11 L20,14 L23,16 L21,20 L18,19 L15,20 L14,23 L10,23 L9,20 L6,19 L3,20 L1,16 L4,14 L4,11 L1,9 L3,5 L6,6 L9,5 Z M16,12.5 A4,4 0 1 1 8,12.5 A4,4 0 1 1 16,12.5",
        ["mark"] = "M2,12 Q12,0 22,12 Q12,24 2,12 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12"
    };
    public static FrameworkElement Create(string name, double size = 22, string color = "Ink")
    {
        var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(Paths[name]), StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform, Width = size, Height = size, Margin = new Thickness(2) };
        path.SetResourceReference(Shape.StrokeProperty, color); return path;
    }
}
