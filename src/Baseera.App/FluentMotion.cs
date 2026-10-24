using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace Baseera.App;

// WPF implementation of the interaction states observed in Windows Settings.
// Timings are Baseera's own; no Microsoft binary or private XAML is copied.
internal static class FluentMotion
{
    internal static bool Allowed => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(FluentMotion), new PropertyMetadata(false, Attach));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(State), typeof(FluentMotion));

    private static void Attach(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not System.Windows.Controls.Control control || args.NewValue is not true) return;
        if (control.GetValue(StateProperty) is null) control.SetValue(StateProperty, new State(control));
    }
    internal static void To(Animatable target, DependencyProperty property, double value, int milliseconds)
    {
        double from = (double)target.GetValue(property);
        target.BeginAnimation(property, null); target.SetValue(property, value);
        if (Allowed && milliseconds > 0 && Math.Abs(from - value) > 0.0001)
            target.BeginAnimation(property, new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(milliseconds))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    internal static void Opacity(UIElement target, double value, int milliseconds)
    {
        double from = target.Opacity; target.BeginAnimation(UIElement.OpacityProperty, null); target.Opacity = value;
        if (Allowed && milliseconds > 0 && Math.Abs(from - value) > 0.0001)
            target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(milliseconds)));
    }
    private sealed class State
    {
        private readonly System.Windows.Controls.Control owner;
        private bool hover, pressed;
        internal State(System.Windows.Controls.Control owner)
        {
            this.owner = owner;
            owner.Loaded += (_, _) => Update(false);
            owner.Unloaded += (_, _) => { hover = pressed = false; Update(false); };
            owner.MouseEnter += (_, _) => { hover = true; Update(); };
            owner.MouseLeave += (_, _) => { hover = pressed = false; Update(); };
            owner.PreviewMouseLeftButtonDown += (_, _) => { pressed = true; Update(); };
            owner.PreviewMouseLeftButtonUp += (_, _) => { pressed = false; Update(); };
            owner.LostMouseCapture += (_, _) => { pressed = false; Update(); };
            owner.PreviewKeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Space or System.Windows.Input.Key.Return) { pressed = true; Update(); } };
            owner.PreviewKeyUp += (_, _) => { pressed = false; Update(); };
            owner.IsEnabledChanged += (_, _) => { if (!owner.IsEnabled) pressed = hover = false; Update(); };
            if (owner is CheckBox check) { check.Checked += (_, _) => Update(); check.Unchecked += (_, _) => Update(); }
            if (owner is ComboBox combo)
            { combo.DropDownOpened += (_, _) => Dropdown(true); combo.DropDownClosed += (_, _) => Dropdown(false); }
        }
        private T? Part<T>(string name) where T : DependencyObject => owner.Template?.FindName(name, owner) as T;
        private static T? MutableTransform<T>(FrameworkElement? element) where T : Transform
        {
            if (element?.RenderTransform is not T value) return null;
            if (!value.IsFrozen) return value;
            var mutable = (T)value.CloneCurrentValue();
            element.RenderTransform = mutable; return mutable;
        }
        private void Update(bool animate = true)
        {
            int time = animate ? pressed ? 70 : hover ? 100 : 160 : 0;
            if (Part<Border>("HoverOverlay") is { } overlay) Opacity(overlay, owner.IsEnabled ? pressed ? .075 : hover ? .04 : 0 : 0, time);
            if (MutableTransform<ScaleTransform>(Part<FrameworkElement>("ContentLayer")) is { } scale)
            {
                To(scale, ScaleTransform.ScaleXProperty, pressed && owner.IsEnabled ? .985 : 1, time);
                To(scale, ScaleTransform.ScaleYProperty, pressed && owner.IsEnabled ? .985 : 1, time);
            }
            if (owner is CheckBox check && MutableTransform<TranslateTransform>(Part<FrameworkElement>("Dot")) is { } thumb)
                To(thumb, TranslateTransform.XProperty, check.IsChecked == true ? 18 : 0, animate ? 140 : 0);
            if (Part<Border>("ScrollThumb") is { } scroll)
            { Opacity(scroll, hover || pressed ? .65 : .3, time); AnimateWidth(scroll, hover || pressed ? 7 : 3, time); }
        }
        private static void AnimateWidth(FrameworkElement target, double value, int milliseconds)
        {
            double from = double.IsNaN(target.Width) ? value : target.Width;
            target.BeginAnimation(FrameworkElement.WidthProperty, null); target.Width = value;
            if (Allowed && milliseconds > 0 && from != value)
                target.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(milliseconds)));
        }
        private void Dropdown(bool open)
        {
            var toggle = Part<ToggleButton>("DropDownToggle");
            if (MutableTransform<RotateTransform>(toggle?.Template?.FindName("Arrow", toggle) as FrameworkElement) is { } rotation)
                To(rotation, RotateTransform.AngleProperty, open ? 180 : 0, 120);
            if (!open || !Allowed || Part<Popup>("PART_Popup")?.Child is not FrameworkElement panel) return;
            panel.Opacity = 0; Opacity(panel, 1, 130);
            var transform = new TranslateTransform(0, -5); panel.RenderTransform = transform;
            To(transform, TranslateTransform.YProperty, 0, 160);
        }
    }
}
