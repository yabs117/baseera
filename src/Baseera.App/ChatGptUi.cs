using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using Baseera.App.Services;

namespace Baseera.App;

internal static class ChatGptUi
{
    internal static void BrandSignInButton(Button button)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Brand/chatgpt-logo-white.svg")).Stream;
        using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(xml); var path = document.Descendants().Single(element => element.Name.LocalName == "path");
        var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse(path.Attribute("d")!.Value), Fill = Brushes.White, Stretch = Stretch.Uniform, Width = 21, Height = 21, Margin = new Thickness(0, 0, 9, 0) };
        content.Children.Add(icon); content.Children.Add(new TextBlock { Text = I18n.T("chatGptContinue"), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = Settings.Current.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight });
        button.Content = content; button.Background = Brushes.Black; button.BorderBrush = Brushes.Black; button.MinHeight = 38;
    }
    internal static void OpenUsage()
    {
        try { Process.Start(new ProcessStartInfo("https://chatgpt.com/settings/usage") { UseShellExecute = true }); }
        catch { MessageBox.Show(I18n.T("chatGptUsageOpenFailed"), "Baseera"); }
    }
    internal static void ShowWelcome(Window? owner) => Show(owner, "chatGptWelcomeTitle", "chatGptWelcomeBody", false);
    internal static void ShowUsageLimit(Window? owner) => Show(owner, "chatGptLimitTitle", "chatGptUsageLimit", true);
    private static void Show(Window? owner, string title, string body, bool limit) => CreateDialog(owner, title, body, limit).ShowDialog();
    internal static Window CreateDialog(Window? owner, string title, string body, bool limit)
    {
        var dialog = new Window
        {
            Title = "Baseera · ChatGPT", Owner = owner, WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            FontFamily = Brand.InterfaceFont, FlowDirection = FlowDirection.LeftToRight,
            Icon = Brand.WindowIcon
        };
        dialog.SetResourceReference(Window.BackgroundProperty, "Bg"); dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        var panel = new StackPanel { Margin = new Thickness(26), FlowDirection = Settings.Current.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        panel.Children.Add(new TextBlock { Text = I18n.T(title), FontSize = 22, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = I18n.T(body), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        var actions = new WrapPanel();
        var primary = new Button { Content = I18n.T(limit ? "chatGptManageUsage" : "chatGptGotIt"), MinWidth = 110 };
        primary.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Accent");
        primary.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Accent");
        primary.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "OnAccent");
        primary.Click += (_, _) => { if (limit) OpenUsage(); dialog.Close(); }; actions.Children.Add(primary);
        var secondary = new Button { Content = I18n.T(limit ? "close" : "chatGptManageUsage"), MinWidth = 110 };
        secondary.Click += (_, _) => { if (limit) dialog.Close(); else OpenUsage(); }; actions.Children.Add(secondary);
        panel.Children.Add(actions); dialog.Content = panel;
        dialog.SourceInitialized += (_, _) => WindowAppearance.Apply(dialog, Settings.Current.Dark); return dialog;
    }
    internal static StackPanel PlanNotice()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(new TextBlock { Text = I18n.T("chatGptUsingPlan"), TextWrapping = TextWrapping.Wrap });
        var usage = new Button { Content = I18n.T("chatGptManageUsage"), HorizontalAlignment = HorizontalAlignment.Left };
        usage.Click += (_, _) => OpenUsage(); panel.Children.Add(usage); return panel;
    }
}
