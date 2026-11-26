using Baseera.App.Services;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Baseera.App;

public sealed partial class MainWindow
{
    private bool restorePaletteFocus;

    private FrameworkElement ColorPalettePicker()
    {
        var panel = Stack();
        panel.Margin = new Thickness(0, 13, 0, 13);
        var heading = Text(I18n.T("colorPalette"), 14);
        heading.FontWeight = HeadingWeight;
        heading.Margin = new Thickness(0, 0, 0, 9);
        panel.Children.Add(heading);

        if (UsesHighContrast)
            panel.Children.Add(Text(I18n.T("paletteHighContrast"), 12, true));

        var choices = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -8, 0), IsEnabled = !UsesHighContrast };
        DeviceId(choices, "ColorPaletteChoices");
        choices.SizeChanged += (_, args) =>
        {
            if (args.WidthChanged) choices.Columns = args.NewSize.Width >= 670 ? 4 : 2;
        };
        var selected = WorkspacePalette.Resolve(Settings.Current.ColorPalette);
        foreach (var palette in WorkspacePalette.Choices)
        {
            string label = I18n.T(palette.Label);
            var option = new System.Windows.Controls.RadioButton
            {
                Style = (Style)FindResource("PaletteChoice"),
                Content = ColorPaletteSample(label, palette.Colors(Settings.Current.Dark)),
                GroupName = "BaseeraColorPalette",
                IsChecked = palette.Id == selected.Id
            };
            DeviceId(option, "Palette-" + palette.Id);
            AutomationProperties.SetName(option, label);
            option.Checked += (_, _) => SelectColorPalette(option, palette);
            choices.Children.Add(option);
            if (restorePaletteFocus && palette.Id == selected.Id)
            {
                restorePaletteFocus = false;
                _ = Dispatcher.InvokeAsync(() => { if (option.IsVisible && option.IsEnabled) option.Focus(); }, DispatcherPriority.Loaded);
            }
        }
        panel.Children.Add(choices);
        return panel;
    }

    private FrameworkElement ColorPaletteSample(string label, WorkspaceColors colors)
    {
        var content = new DockPanel();
        var swatch = new Border
        {
            Width = 41, Height = 26, CornerRadius = new CornerRadius(4),
            Background = UsesHighContrast ? Resource("Workspace") : WorkspaceColors.Brush(colors.Workspace),
            BorderBrush = UsesHighContrast ? Resource("Line") : WorkspaceColors.Brush(colors.Line),
            BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0)
        };
        var sample = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0),
            Fill = UsesHighContrast ? Resource("Accent") : WorkspaceColors.Brush(colors.Accent)
        };
        DockPanel.SetDock(dot, Dock.Left);
        sample.Children.Add(dot);
        var letters = Text(Arabic ? "أب" : "Aa", 11);
        letters.Margin = new Thickness(0);
        letters.VerticalAlignment = VerticalAlignment.Center;
        letters.TextWrapping = TextWrapping.NoWrap;
        letters.Foreground = UsesHighContrast ? Resource("Ink") : WorkspaceColors.Brush(colors.Ink);
        sample.Children.Add(letters);
        swatch.Child = sample;
        DockPanel.SetDock(swatch, Dock.Left);
        content.Children.Add(swatch);
        var name = Text(label, 13);
        name.Margin = new Thickness(0);
        name.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(name);
        return content;
    }

    private void SelectColorPalette(System.Windows.Controls.RadioButton option, WorkspacePalette palette)
    {
        string previous = Settings.Current.ColorPalette;
        if (palette.Id == previous) return;
        restorePaletteFocus = option.IsKeyboardFocusWithin;
        Settings.Current.ColorPalette = palette.Id;
        try { Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Settings.Current.ColorPalette = previous;
            SetOperationStatus("error");
        }
        ApplyTheme();
        Render(true);
    }
}
