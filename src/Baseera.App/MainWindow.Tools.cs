using System.Diagnostics;
using System.Text.Json;
using Baseera.App.Services;
using Baseera.Core;
using Microsoft.Win32;

namespace Baseera.App;

public sealed partial class MainWindow
{
    private int selectedImageIndex = -1;
    private string conversionFormat = "PNG";
    private int jpegQuality = 90;

    private void ChooseInspectionPath(bool folder)
    {
        string? selected = null;
        if (folder) { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) selected = picker.FolderName; }
        else { var picker = new OpenFileDialog(); if (picker.ShowDialog(this) == true) selected = picker.FileName; }
        if (selected is null) return;
        Settings.Current.ScanPath = selected; report = null; Settings.Save(); Render();
    }
    private void ScanPage(StackPanel body)
    {
        if (!Guard(body, "scan")) return;
        var source = Stack(); source.Children.Add(Text(I18n.T("inspectionSource"), 14));
        if (Settings.Current.ScanPath.Length > 0) source.Children.Add(new TextBox { Text = Settings.Current.ScanPath, Height = double.NaN, FlowDirection = FlowDirection.LeftToRight, IsReadOnly = true, TextWrapping = TextWrapping.Wrap });
        var actions = new WrapPanel(); actions.Children.Add(Action("choose", () => ChooseInspectionPath(false))); actions.Children.Add(Action("chooseFolder", () => ChooseInspectionPath(true)));
        var inspect = AsyncAction("inspect", async ct =>
        {
            if (!Settings.Current.ScanEnabled) return;
            report = await Task.Run(() => new ModScanner().Scan(Settings.Current.ScanPath, ct), ct); Render();
        }, true);
        inspect.IsEnabled = File.Exists(Settings.Current.ScanPath) || Directory.Exists(Settings.Current.ScanPath); actions.Children.Add(inspect); source.Children.Add(actions); body.Children.Add(Card(source));
        Help(body, "scanNote");
        if (report is null) { var empty = Stack(); EmptyState(empty, "scan", "inspectionReady", "inspectionHint"); body.Children.Add(Card(empty, 0)); return; }
        var result = Stack(); var title = Text(I18n.T(report.Verdict), 17); title.FontWeight = HeadingWeight; result.Children.Add(title);
        result.Children.Add(Text($"{I18n.T("files")}: {report.Files.Count}   ·   {I18n.T("findings")}: {report.Findings.Count}", 12, true));
        var export = AsyncAction("export", async ct =>
        {
            var save = new SaveFileDialog { Filter = "JSON|*.json", FileName = "baseera-report.json" };
            if (save.ShowDialog(this) == true) await File.WriteAllTextAsync(save.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
        }); export.HorizontalAlignment = HorizontalAlignment.Left; result.Children.Add(export); body.Children.Add(Card(result));
        foreach (var finding in report.Findings.Take(100))
        {
            var evidence = Stack(); evidence.FlowDirection = FlowDirection.LeftToRight; evidence.Children.Add(Text(finding.Code, 14)); evidence.Children.Add(Text(finding.Path, 12, true)); evidence.Children.Add(Text(finding.Evidence)); body.Children.Add(Card(evidence));
        }
    }

    private void TranslationPage(StackPanel body)
    {
        if (!Guard(body, "translate")) return;
        var capture = Stack(); capture.Children.Add(Text(I18n.T("selectedWindow"), 14));
        var windows = new ComboBox { ItemsSource = WindowTranslator.ListWindows(), DisplayMemberPath = "Title", Tag = I18n.T("selectWindow"), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 5, 8, 16) };
        var choice = new Grid(); choice.ColumnDefinitions.Add(new ColumnDefinition()); choice.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); choice.Children.Add(windows);
        var refresh = Action("refresh", () => windows.ItemsSource = WindowTranslator.ListWindows()); refresh.Margin = new Thickness(0, 5, 0, 16); refresh.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(refresh, 1); choice.Children.Add(refresh); capture.Children.Add(choice);
        capture.Children.Add(Text(I18n.T("target")));
        var target = new ComboBox { ItemsSource = new[] { "Arabic", "English", "French", "Spanish" }, SelectedItem = Settings.Current.TargetLanguage, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        target.SelectionChanged += (_, _) => { Settings.Current.TargetLanguage = target.SelectedItem?.ToString() ?? "Arabic"; Settings.Save(); }; capture.Children.Add(target);
        if (!File.Exists(Path.Combine(Settings.Folder, "secrets.bin")) || string.IsNullOrWhiteSpace(Settings.Current.GeminiModel))
        {
            capture.Children.Add(Text(I18n.T("needsKey"), 12, true)); var settings = Action("settings", () => Navigate("settings")); settings.HorizontalAlignment = HorizontalAlignment.Left; capture.Children.Add(settings);
        }
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(AsyncAction("translateNow", async ct =>
        {
            if (!Settings.Current.TranslationEnabled) return;
            if (windows.SelectedItem is not WindowChoice window) throw new InvalidOperationException(I18n.T("selectWindow"));
            if (string.IsNullOrWhiteSpace(Settings.GetKey()) || string.IsNullOrWhiteSpace(Settings.Current.GeminiModel)) throw new InvalidOperationException(I18n.T("needsKey"));
            overlay?.Close(); var image = await WindowTranslator.CaptureWindow(window.Handle, ct); var blocks = await WindowTranslator.Translate(image, ct);
            ct.ThrowIfCancellationRequested(); if (!Settings.Current.TranslationEnabled) return;
            if (blocks.Count == 0) throw new InvalidOperationException(I18n.T("noText"));
            overlay = new TranslationOverlay(image, blocks); overlay.Show(); overlay.Activate();
        }, true));
        actions.Children.Add(Action("hideOverlay", () => overlay?.Close())); capture.Children.Add(actions); body.Children.Add(Card(capture)); Help(body, "translateNote");
    }

    private void UpdatesPage(StackPanel body)
    {
        if (!Guard(body, "updates")) return;
        SectionHeading(body, "installedApps"); var apps = Stack(); var packages = Stack(); packages.Children.Add(Text(I18n.T("refreshHint"), 12, true));
        var actions = new WrapPanel(); actions.Children.Add(AsyncAction("refresh", async ct =>
        {
            var list = await Updater.List(ct); packages.Children.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var package = list[i]; var entry = Stack(); entry.Margin = new Thickness(0, 8, 0, 8);
                var automatic = Toggle("auto", Settings.Current.AutoUpdateIds.Contains(package.Id), value =>
                {
                    if (value && !Settings.Current.AutoUpdateIds.Contains(package.Id)) Settings.Current.AutoUpdateIds.Add(package.Id);
                    else if (!value) Settings.Current.AutoUpdateIds.Remove(package.Id); Settings.Save();
                }); automatic.Content = null;
                var title = new Grid(); title.ColumnDefinitions.Add(new ColumnDefinition()); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = Stack(); name.Children.Add(new TextBlock { Text = package.Id, FlowDirection = FlowDirection.LeftToRight, FontSize = 14, TextWrapping = TextWrapping.Wrap }); name.Children.Add(Text(package.Version, 12, true)); title.Children.Add(name);
                automatic.ToolTip = I18n.T("auto"); automatic.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(automatic, 1); title.Children.Add(automatic); entry.Children.Add(title);
                var custom = new TextBox { Text = Settings.Current.ManualSources.GetValueOrDefault(package.Id, ""), Tag = "https://github.com/owner/project/releases", FlowDirection = FlowDirection.LeftToRight, FontSize = 12 };
                var manual = Toggle("manualPerApp", Settings.Current.ManualSources.ContainsKey(package.Id), value =>
                {
                    custom.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                    if (value) Settings.Current.ManualSources[package.Id] = custom.Text.Trim(); else Settings.Current.ManualSources.Remove(package.Id); Settings.Save();
                });
                custom.Visibility = manual.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                custom.LostFocus += (_, _) => { if (manual.IsChecked == true) { Settings.Current.ManualSources[package.Id] = custom.Text.Trim(); Settings.Save(); } };
                entry.Children.Add(manual); entry.Children.Add(custom); packages.Children.Add(entry); if (i < list.Count - 1) Divider(packages);
            }
            if (list.Count == 0) packages.Children.Add(Text(I18n.T("empty"), 12, true));
        }));
        actions.Children.Add(AsyncAction("updateNow", async ct => { updateLog = await Updater.UpdateSelected(ct); Render(); }, true));
        apps.Children.Add(actions); apps.Children.Add(new Border { Height = 12 }); apps.Children.Add(packages); body.Children.Add(Card(apps)); Help(body, "updateNote");
        if (updateLog.Length > 0) body.Children.Add(new TextBox { Text = updateLog, Height = double.NaN, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FlowDirection = FlowDirection.LeftToRight });
        SectionHeading(body, "releasePage"); var source = Stack();
        var url = new TextBox { Text = Settings.Current.ReleasePage, Tag = "https://github.com/owner/project/releases", FlowDirection = FlowDirection.LeftToRight }; source.Children.Add(url);
        var releases = Stack(); var check = AsyncAction("checkRelease", async ct =>
        {
            var release = await Updater.CheckRelease(url.Text.Trim(), ct); Settings.Current.ReleasePage = url.Text.Trim(); Settings.Save(); releases.Children.Clear(); releases.Children.Add(Text(release.Version, 17));
            foreach (var asset in release.Assets)
            {
                var content = Stack(); content.Children.Add(Text(asset.Name)); content.Children.Add(Text($"{asset.Size / 1024 / 1024} MB · {asset.Download.Host}", 12, true));
                var download = Action("openDownload", () => Process.Start(new ProcessStartInfo(asset.Download.AbsoluteUri) { UseShellExecute = true })); download.HorizontalAlignment = HorizontalAlignment.Left; content.Children.Add(download); releases.Children.Add(Card(content));
            }
        }); check.HorizontalAlignment = HorizontalAlignment.Left; source.Children.Add(check); body.Children.Add(Card(source)); Help(body, "releaseNote"); body.Children.Add(releases);
    }

    private void ChooseImages()
    {
        var picker = new OpenFileDialog { Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" };
        if (picker.ShowDialog(this) != true) return;
        selectedImageIndex = images.Count; images.AddRange(picker.FileNames); Render();
    }
    private void ConvertPage(StackPanel body)
    {
        if (!Guard(body, "convert")) return;
        SectionHeading(body, "imageFiles"); var files = Stack();
        var list = new ListBox { ItemsSource = images, Height = 150, FlowDirection = FlowDirection.LeftToRight, SelectedIndex = selectedImageIndex };
        if (images.Count > 0) files.Children.Add(list); else EmptyState(files, "convert", "imagesEmpty", "imagesHint");
        var controls = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; controls.Children.Add(Action("chooseImages", ChooseImages));
        var up = Action("up", () => { int index = list.SelectedIndex; if (index > 0) { (images[index - 1], images[index]) = (images[index], images[index - 1]); selectedImageIndex = index - 1; Render(); } });
        var down = Action("down", () => { int index = list.SelectedIndex; if (index >= 0 && index < images.Count - 1) { (images[index + 1], images[index]) = (images[index], images[index + 1]); selectedImageIndex = index + 1; Render(); } });
        var remove = Action("remove", () => { int index = list.SelectedIndex; if (index >= 0) { images.RemoveAt(index); selectedImageIndex = Math.Min(index, images.Count - 1); Render(); } });
        void SelectionState() { selectedImageIndex = list.SelectedIndex; up.IsEnabled = list.SelectedIndex > 0; down.IsEnabled = list.SelectedIndex >= 0 && list.SelectedIndex < images.Count - 1; remove.IsEnabled = list.SelectedIndex >= 0; }
        list.SelectionChanged += (_, _) => SelectionState(); SelectionState(); controls.Children.Add(up); controls.Children.Add(down); controls.Children.Add(remove); files.Children.Add(controls); body.Children.Add(Card(files));
        SectionHeading(body, "conversionOptions"); var options = Stack();
        var format = new ComboBox { ItemsSource = new[] { "PNG", "JPEG", "BMP", "TIFF", "PDF" }, SelectedItem = conversionFormat, Width = 180, Margin = new Thickness(0) };
        var quality = new Slider { Minimum = 30, Maximum = 100, Value = jpegQuality, TickFrequency = 10, IsSnapToTickEnabled = true, Width = 180, VerticalAlignment = VerticalAlignment.Center, IsEnabled = conversionFormat is "JPEG" or "PDF" };
        var qualityRow = new StackPanel { Orientation = Orientation.Horizontal }; qualityRow.Children.Add(quality); var percent = Text(jpegQuality + "%", 12, true); percent.Margin = new Thickness(12, 0, 0, 0); percent.VerticalAlignment = VerticalAlignment.Center; percent.Width = 34; qualityRow.Children.Add(percent);
        format.SelectionChanged += (_, _) => { conversionFormat = (string)format.SelectedItem; quality.IsEnabled = conversionFormat is "JPEG" or "PDF"; };
        quality.ValueChanged += (_, _) => { jpegQuality = (int)quality.Value; percent.Text = jpegQuality + "%"; };
        options.Children.Add(SettingRow("output", format)); Divider(options); options.Children.Add(SettingRow("quality", qualityRow)); body.Children.Add(Card(options));
        var convert = AsyncAction("convertNow", _ =>
        {
            if (!Settings.Current.ConvertEnabled || images.Count == 0) throw new InvalidOperationException(I18n.T("chooseImages"));
            if (conversionFormat == "PDF")
            {
                var save = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = "Baseera-images.pdf" }; if (save.ShowDialog(this) == true) ImageConverter.ToPdf(images, save.FileName, jpegQuality);
            }
            else
            {
                var folder = new OpenFolderDialog(); if (folder.ShowDialog(this) == true) foreach (var image in images) ImageConverter.ConvertImage(image, folder.FolderName, conversionFormat, jpegQuality);
            }
            return Task.CompletedTask;
        }, true); convert.IsEnabled = images.Count > 0; convert.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(convert); Help(body, "convertNote");
    }
}
