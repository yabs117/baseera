using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

internal sealed class UpdateWindow : Window
{
    private readonly SelfRelease release;
    private readonly TextBlock message;
    private readonly System.Windows.Controls.ProgressBar progress;
    private readonly Button install, later;
    private CancellationTokenSource? download;
    private bool handingOff;

    internal UpdateWindow(MainWindow owner, SelfRelease release)
    {
        this.release = release; Owner = owner; Title = I18n.T("selfTitle");
        Width = Math.Min(680, SystemParameters.WorkArea.Width - 32); Height = Math.Min(560, SystemParameters.WorkArea.Height - 32);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        FontFamily = Brand.InterfaceFont; FontSize = 13; Icon = Brand.WindowIcon; UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Ink");
        SourceInitialized += (_, _) => WindowAppearance.Apply(this, Settings.Current.Dark);
        var frame = new Grid { Background = B("Bg"), FlowDirection = FlowDirection.LeftToRight }; Content = frame;
        var layout = new Grid { FlowDirection = Settings.Current.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); frame.Children.Add(layout);
        var header = new StackPanel { Margin = new Thickness(28, 24, 28, 18) };
        var label = Copy("Baseera", 13, true); label.Margin = new Thickness(0, 0, 0, 12); header.Children.Add(label);
        var heading = Copy(I18n.T("selfAvailable"), 27); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 12); header.Children.Add(heading);
        var version = Copy(SelfUpdater.CurrentVersion + "  →  " + release.Tag.TrimStart('v') + "   ·   " + (release.Size / 1048576d).ToString("0.0") + " MB", 13, true);
        version.FlowDirection = FlowDirection.LeftToRight; version.TextAlignment = Settings.Current.Language == "ar" ? TextAlignment.Right : TextAlignment.Left; header.Children.Add(version); layout.Children.Add(header);
        var body = new StackPanel { Margin = new Thickness(28, 0, 28, 12) };
        body.Children.Add(Copy(I18n.T("selfNotes"), 14));
        var notes = Copy(string.IsNullOrWhiteSpace(release.Notes) ? I18n.T("selfNoNotes") : release.Notes, 12, true); notes.LineHeight = 20; notes.Margin = new Thickness(16);
        body.Children.Add(new Border { Background = B("Surface"), BorderBrush = B("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 10, 0, 16), Child = new ScrollViewer { Content = notes, MaxHeight = 174, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        body.Children.Add(Copy(I18n.T("selfExplanation"), 12, true));
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var footer = new StackPanel { Margin = new Thickness(28, 16, 28, 20) };
        message = Copy(I18n.T("selfReady"), 12, true); message.Margin = new Thickness(0, 0, 0, 10); footer.Children.Add(message);
        progress = new System.Windows.Controls.ProgressBar { Minimum = 0, Maximum = release.Size, Foreground = B("Accent"), Background = B("Line"), Height = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 16), FlowDirection = FlowDirection.LeftToRight }; footer.Children.Add(progress);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        later = new Button { Content = I18n.T("selfLater"), MinWidth = 96 }; later.Click += (_, _) => { if (download is not null) download.Cancel(); else Close(); }; actions.Children.Add(later);
        install = new Button { Content = new TextBlock { Text = I18n.T("selfInstall"), Foreground = B("OnAccent") }, Background = B("Accent"), BorderBrush = B("Accent"), MinWidth = 180, IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        install.Click += async (_, _) => await Install(); actions.Children.Add(install); footer.Children.Add(actions);
        var border = new Border { Background = B("Surface"), BorderBrush = B("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Child = footer }; Grid.SetRow(border, 2); layout.Children.Add(border);
        Closing += (_, e) => { if (handingOff) { e.Cancel = true; return; } if (download is not null) { download.Cancel(); e.Cancel = true; } };
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && !handingOff) { e.Handled = true; if (download is not null) download.Cancel(); else Close(); } };
    }
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static TextBlock Copy(string value, double size, bool muted = false) => new() { Text = value, FontSize = size, Foreground = B(muted ? "Muted" : "Ink"), TextWrapping = TextWrapping.Wrap };
    private async Task Install()
    {
        if (download is not null) return;
        download = new CancellationTokenSource(); install.IsEnabled = false; later.Content = I18n.T("cancel"); progress.Visibility = Visibility.Visible; progress.Value = 0;
        string? folder = null; bool launched = false;
        try
        {
            message.Text = I18n.T("selfDownloading");
            folder = await SelfUpdater.Prepare(release, new Progress<long>(bytes =>
            {
                progress.Value = bytes;
                message.Text = bytes == release.Size ? I18n.T("selfVerifying") : I18n.T("selfDownloading") + "  " + (bytes * 100d / release.Size).ToString("0") + "%";
            }), download.Token);
            download.Token.ThrowIfCancellationRequested(); handingOff = true; later.IsEnabled = false; message.Text = I18n.T("selfRestarting");
            await SelfUpdater.StartHelper(folder);
            launched = true;
            download.Dispose(); download = null;
            // The helper has validated the payload and is waiting for this process to exit.
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) { message.Text = I18n.T("selfCanceled"); }
        catch (Exception ex) { message.Text = I18n.T("selfFailed") + "\n" + ex.Message; }
        finally
        {
            handingOff = false;
            if (download is not null) { download.Dispose(); download = null; }
            install.IsEnabled = true; later.IsEnabled = true; later.Content = I18n.T("selfLater");
            if (folder is not null && !launched)
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
