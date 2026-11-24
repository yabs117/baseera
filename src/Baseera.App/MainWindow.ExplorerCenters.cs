using System.Globalization;
using System.Text;
using System.Windows.Automation;
using Baseera.App.Services;
using Baseera.Core;
using Microsoft.Win32;

namespace Baseera.App;

public sealed partial class MainWindow
{
    private string explorerStorageRoot = "", explorerSearchRoot = "", explorerSearchQuery = "", explorerUsbFile = "", explorerOnset = "";
    private bool explorerFindDuplicates, explorerDuplicatesMeasured, explorerIndexContents, explorerCleanupAcknowledged, explorerCleanupPreviewed, explorerUsbReadAcknowledged;
    private int explorerTimelineDays = 7, explorerMeasuredTimelineDays;
    private StorageReport? explorerStorage;
    private SearchIndex? explorerIndex;
    private TimelineSnapshot? explorerTimeline;
    private InvestigationReport? explorerUsb, explorerUsbRead;
    private IReadOnlyList<StorageFile> explorerCleanup = [];
    private readonly HashSet<string> explorerCleanupSelection = new(StringComparer.OrdinalIgnoreCase);
    private string explorerCleanupStatus = "";
    private CancellationTokenSource? explorerQueryRequest;
    private int explorerQueryGeneration;

    // The smoke suite injects fixtures; normal application sessions cannot select these paths.
    internal Func<string?>? ExplorerFolderPickerForSmoke { get; set; }
    internal Func<string?>? ExplorerFilePickerForSmoke { get; set; }
    internal Func<string, bool, CancellationToken, Task<StorageReport>>? StorageInspectionForSmoke { get; set; }
    internal Func<CancellationToken, Task<IReadOnlyList<StorageFile>>>? CleanupPreviewForSmoke { get; set; }
    internal Func<IReadOnlyList<StorageFile>, bool>? CleanupConfirmationForSmoke { get; set; }
    internal Func<CleanupApproval, (int Removed, bool Aborted)>? CleanupRecycleForSmoke { get; set; }
    internal Func<string, bool, CancellationToken, Task<SearchIndex>>? SearchBuildForSmoke { get; set; }
    internal Func<int, CancellationToken, Task<TimelineSnapshot>>? TimelineSourceForSmoke { get; set; }
    internal Func<CancellationToken, Task<InvestigationReport>>? UsbSourceForSmoke { get; set; }
    internal Func<string, CancellationToken, Task<InvestigationReport>>? UsbReadForSmoke { get; set; }

    private string? ExplorerPickFolder()
    {
        if (smokeMode) return ExplorerFolderPickerForSmoke?.Invoke();
        var picker = new OpenFolderDialog { Title = I18n.T("hubChooseFolder"), Multiselect = false };
        return picker.ShowDialog(this) == true ? picker.FolderName : null;
    }
    private string? ExplorerPickFile()
    {
        if (smokeMode) return ExplorerFilePickerForSmoke?.Invoke();
        var picker = new OpenFileDialog { Title = I18n.T("hubSelectExternalFile"), CheckFileExists = true, Multiselect = false, Filter = I18n.T("hubAllFiles") + "|*.*" };
        return picker.ShowDialog(this) == true ? picker.FileName : null;
    }
    private static string ExplorerBytes(long bytes) => bytes >= 1073741824 ? (bytes / 1073741824d).ToString("0.00", CultureInfo.InvariantCulture) + " GiB" :
        bytes >= 1048576 ? (bytes / 1048576d).ToString("0.00", CultureInfo.InvariantCulture) + " MiB" : bytes >= 1024 ? (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KiB" : bytes + " B";
    private static void ExplorerName(DependencyObject element, string key) => AutomationProperties.SetName(element, I18n.T(key));
    private CheckBox ExplorerCheck(string key, string id, bool value, Action<bool> change)
    {
        var check = Toggle(key, value, change); DeviceId(check, id); return check;
    }
    private TextBox ExplorerPath(string id, string value, string label)
    {
        var text = HubTextBox(id, value, true); text.FlowDirection = FlowDirection.LeftToRight; text.TextWrapping = TextWrapping.Wrap; ExplorerName(text, label); return text;
    }
    private void StoragePage(StackPanel body)
    {
        if (!Guard(body, "storage")) return;
        Help(body, "hubStorageScope");
        var source = Stack(); source.Children.Add(Text(I18n.T("hubFolderScope"), 13));
        source.Children.Add(ExplorerPath("HubStorageRoot", explorerStorageRoot, "hubFolderScope"));
        source.Children.Add(ExplorerCheck("hubFindDuplicates", "HubStorageDuplicates", explorerFindDuplicates, value => explorerFindDuplicates = value));
        var actions = new WrapPanel();
        actions.Children.Add(HubButton("hubChooseFolder", _ =>
        {
            string? selected = ExplorerPickFolder(); if (selected is null) return Task.CompletedTask;
            explorerStorageRoot = LocalFileScope.Root(selected); explorerStorage = null; explorerDuplicatesMeasured = false;
            if (page == "storage") Render(true); return Task.CompletedTask;
        }));
        var inspect = HubButton("hubInspectStorage", async ct =>
        {
            if (string.IsNullOrWhiteSpace(explorerStorageRoot)) return;
            string root = explorerStorageRoot; bool duplicates = explorerFindDuplicates;
            var collected = smokeMode && StorageInspectionForSmoke is not null ? await StorageInspectionForSmoke(root, duplicates, ct) :
                await Task.Run(() => StorageExplorer.Inspect(root, duplicates, ct), ct);
            ct.ThrowIfCancellationRequested(); if (root != explorerStorageRoot) return;
            explorerStorage = collected; explorerDuplicatesMeasured = duplicates; if (page == "storage") Render(true);
        }, true); inspect.IsEnabled = explorerStorageRoot.Length > 0; actions.Children.Add(inspect); source.Children.Add(actions); body.Children.Add(Card(source));
        if (explorerStorage is { } report)
        {
            body.Children.Add(Text(HubTime(report.Time), 11, true));
            body.Children.Add(Text(string.Format(I18n.T("hubStorageSummary"), report.Entries, ExplorerBytes(report.Bytes)), 16));
            foreach (string notice in report.Notices) body.Children.Add(Text(I18n.T(notice), 12, true));
            SectionHeading(body, "hubFolderBreakdown");
            foreach (var folder in report.Folders)
            {
                var row = Stack(); row.Children.Add(ExplorerPath("HubStorageFolder-" + body.Children.Count, folder.Path, "hubFolderScope"));
                row.Children.Add(Text(ExplorerBytes(folder.Bytes), 13));
                if (report.Bytes > 0) row.Children.Add(new System.Windows.Controls.ProgressBar { Height = 3, Minimum = 0, Maximum = 100, Value = Math.Clamp(folder.Bytes * 100d / report.Bytes, 0, 100), Foreground = Resource("Accent"), Background = Resource("Line"), IsHitTestVisible = false, Focusable = false });
                body.Children.Add(Card(row, 12));
            }
            SectionHeading(body, "hubLargestFiles");
            foreach (var file in report.Largest.Take(20))
            {
                var row = Stack(); row.Children.Add(ExplorerPath("HubStorageFile-" + body.Children.Count, file.Path, "hubFilePath")); row.Children.Add(Text(ExplorerBytes(file.Bytes), 12, true)); body.Children.Add(Card(row, 12));
            }
            SectionHeading(body, "hubDuplicateSets");
            if (!explorerDuplicatesMeasured) body.Children.Add(Text(I18n.T("hubDuplicatesNotScanned"), 12, true));
            else if (report.Duplicates.Count == 0) body.Children.Add(Text(I18n.T("hubNoDuplicatesInSample"), 12, true));
            foreach (var duplicates in report.Duplicates)
            {
                var row = Stack(); row.Children.Add(Text(string.Format(I18n.T("hubDuplicateSummary"), duplicates.Files.Count, ExplorerBytes(duplicates.Bytes)), 14));
                row.Children.Add(ExplorerPath("HubDuplicateHash-" + body.Children.Count, "SHA-256  " + duplicates.Sha256, "hubSha256"));
                foreach (var file in duplicates.Files) row.Children.Add(ExplorerPath("HubDuplicatePath-" + row.Children.Count, file.Path, "hubFilePath")); body.Children.Add(Card(row, 12));
            }
            Help(body, "hubDuplicatesLimits");
            var summary = new InvestigationReport("storage", report.Time,
                [new("hubFolderScope", report.Root), new("hubInspectedFiles", report.Entries.ToString()), new("hubInspectedBytes", report.Bytes.ToString()),
                    .. report.Folders.Take(10).Select(f => new InvestigationFact("hubFolderBreakdown", f.Path, ExplorerBytes(f.Bytes))),
                    .. report.Duplicates.Take(10).Select(d => new InvestigationFact("hubDuplicateSets", d.Files.Count + " × " + ExplorerBytes(d.Bytes), d.Sha256))],
                ["hubStorageScope", "hubDuplicatesLimits", .. report.Notices]);
            HubShare(body, HubReportText(summary));
        }
        CleanupSection(body);
    }
    private void CleanupSection(StackPanel body)
    {
        SectionHeading(body, "hubCleanup"); Help(body, "hubCleanupLimits");
        var group = Stack();
        var preview = HubButton("hubPreviewCleanup", async ct =>
        {
            var files = smokeMode && CleanupPreviewForSmoke is not null ? await CleanupPreviewForSmoke(ct) :
                await Task.Run(() => CleanupApproval.Candidates(StorageActions.TempRoot, ct), ct);
            ct.ThrowIfCancellationRequested();
            explorerCleanup = files.Where(f => !LocalFileScope.Contains(AppContext.BaseDirectory, f.Path) && !LocalFileScope.Contains(Settings.Folder, f.Path)).Take(200).ToArray();
            explorerCleanupSelection.Clear(); explorerCleanupAcknowledged = false; explorerCleanupPreviewed = true; explorerCleanupStatus = ""; if (page == "storage") Render(true);
        }); group.Children.Add(preview);
        if (explorerCleanup.Count > 0)
        {
            var choices = Stack();
            foreach (var file in explorerCleanup)
            {
                var row = new CheckBox { Content = new TextBlock { Text = file.Path + "\n" + ExplorerBytes(file.Bytes) + " · " + HubTime(new DateTimeOffset(file.LastWriteUtc)), TextWrapping = TextWrapping.Wrap }, IsChecked = explorerCleanupSelection.Contains(file.Path), Margin = new Thickness(0, 0, 0, 12), FlowDirection = FlowDirection.LeftToRight };
                AutomationProperties.SetName(row, file.Path + " · " + ExplorerBytes(file.Bytes)); DeviceId(row, "HubCleanupCandidate-" + choices.Children.Count);
                row.Checked += (_, _) => explorerCleanupSelection.Add(file.Path); row.Unchecked += (_, _) => explorerCleanupSelection.Remove(file.Path); choices.Children.Add(row);
            }
            group.Children.Add(new ScrollViewer { Content = choices, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            group.Children.Add(ExplorerCheck("hubCleanupAcknowledge", "HubCleanupAcknowledge", explorerCleanupAcknowledged, value => explorerCleanupAcknowledged = value));
            var recycle = Action("hubRecycleSelected", () =>
            {
                if (operation is not null || !explorerCleanupAcknowledged || explorerCleanupSelection.Count == 0) { SetOperationStatus("hubCleanupChoose"); return; }
                if (explorerCleanupSelection.Count > 10) { SetOperationStatus("hubCleanupBatchLimit"); return; }
                var selected = explorerCleanup.Where(f => explorerCleanupSelection.Contains(f.Path)).ToArray();
                string exact = string.Join("\n", selected.Select(f => f.Path));
                bool confirmed = smokeMode ? CleanupConfirmationForSmoke?.Invoke(selected) == true : MessageBox.Show(this,
                    I18n.T("hubCleanupConfirm") + "\n\n" + exact, I18n.T("hubCleanup"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
                if (!confirmed) return;
                using var job = Services.Tasks.Begin("storage", false);
                try
                {
                    var approval = CleanupApproval.Confirm(selected, true);
                    var result = smokeMode ? CleanupRecycleForSmoke?.Invoke(approval) ?? throw new InvalidOperationException("Fixture recycler is required in smoke mode.") : StorageActions.Recycle(this, approval);
                    if (result.Aborted) job.Cancelled(); else if (result.Removed == selected.Length) job.Complete(); else job.Failed();
                    explorerCleanupStatus = string.Format(I18n.T("hubCleanupResult"), result.Removed, selected.Length) + " " + I18n.T(result.Aborted ? "hubCleanupAborted" : "hubCleanupRepreview");
                    explorerCleanup = []; explorerCleanupSelection.Clear(); explorerCleanupAcknowledged = false; explorerCleanupPreviewed = false; if (page == "storage") Render(true);
                }
                catch (Exception) { job.Failed(); explorerCleanupStatus = I18n.T("hubCleanupFailed"); if (page == "storage") Render(true); }
            }); DeviceId(recycle, "Hub-storage-hubRecycleSelected"); group.Children.Add(recycle);
        }
        else group.Children.Add(Text(I18n.T(explorerCleanupPreviewed ? "hubNoCleanupCandidates" : "hubCleanupNoSelection"), 12, true));
        var status = Text(explorerCleanupStatus, 12, true); DeviceId(status, "HubCleanupStatus"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); group.Children.Add(status); body.Children.Add(Card(group));
    }
    private void SearchPage(StackPanel body)
    {
        if (!Guard(body, "search")) return;
        Help(body, "hubSearchPrivacy");
        var source = Stack(); source.Children.Add(Text(I18n.T("hubFolderScope"), 13)); source.Children.Add(ExplorerPath("HubSearchRoot", explorerSearchRoot, "hubFolderScope"));
        source.Children.Add(ExplorerCheck("hubIndexContentConsent", "HubSearchContentConsent", explorerIndexContents, value => explorerIndexContents = value));
        var actions = new WrapPanel();
        actions.Children.Add(HubButton("hubChooseFolder", _ =>
        {
            string? selected = ExplorerPickFolder(); if (selected is null) return Task.CompletedTask;
            explorerSearchRoot = LocalFileScope.Root(selected); explorerIndexContents = false;
            if (explorerIndex?.Root != explorerSearchRoot) explorerIndex = null;
            if (page == "search") Render(true); return Task.CompletedTask;
        }));
        var build = HubButton("hubBuildIndex", async ct =>
        {
            if (explorerSearchRoot.Length == 0) return;
            string root = explorerSearchRoot; bool contents = explorerIndexContents;
            var progress = new Progress<int>(count => { if (page == "search") SetOperationStatus("hubIndexProgress", count.ToString()); });
            var index = smokeMode && SearchBuildForSmoke is not null ? await SearchBuildForSmoke(root, contents, ct) : await SearchIndexer.Build(root, contents, ct, progress);
            ct.ThrowIfCancellationRequested(); SearchIndexer.Validate(index); explorerIndex = index; if (page == "search") Render(true);
        }, true); build.IsEnabled = explorerSearchRoot.Length > 0; actions.Children.Add(build);
        actions.Children.Add(HubButton("hubLoadIndex", async ct =>
        {
            var index = await InvestigationStore.Load<SearchIndex>("search", ct); if (index is null) { SetOperationStatus("hubNoSavedIndex"); return; }
            SearchIndexer.Validate(index); ct.ThrowIfCancellationRequested(); explorerIndex = index; explorerSearchRoot = index.Root; if (page == "search") Render(true);
        })); source.Children.Add(actions); body.Children.Add(Card(source)); Help(body, "hubSearchLimits");
        if (explorerIndex is { } current)
        {
            body.Children.Add(Text(string.Format(I18n.T("hubIndexSummary"), current.Documents.Count, HubTime(current.Built)), 12, true));
            body.Children.Add(ExplorerPath("HubIndexedRoot", current.Root, "hubFolderScope")); foreach (string notice in current.Notices) body.Children.Add(Text(I18n.T(notice), 12, true));
        }
        SectionHeading(body, "hubQuery"); var query = HubTextBox("HubSearchQuery", explorerSearchQuery); query.MaxLength = 500; query.FlowDirection = Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight; ExplorerName(query, "hubQuery"); body.Children.Add(query);
        body.Children.Add(Text(I18n.T("hubQueryExamples"), 12, true));
        var interpretation = Text("", 12, true); DeviceId(interpretation, "HubSearchInterpretation"); body.Children.Add(interpretation);
        var countLabel = Text("", 12, true); DeviceId(countLabel, "HubSearchCount"); AutomationProperties.SetLiveSetting(countLabel, AutomationLiveSetting.Polite); body.Children.Add(countLabel);
        var results = Stack(); DeviceId(results, "HubSearchResults"); body.Children.Add(results);
        SectionHeading(body, "hubStoredPreview"); var selectedPath = ExplorerPath("HubSearchSelectedPath", "", "hubFilePath"); body.Children.Add(selectedPath);
        var previewText = HubTextBox("HubSearchPreview", "", true, 240); previewText.AcceptsReturn = true; previewText.FlowDirection = Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight; previewText.TextWrapping = TextWrapping.Wrap; previewText.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; ExplorerName(previewText, "hubStoredPreview"); body.Children.Add(previewText);
        async Task DrawQuery()
        {
            explorerQueryRequest?.Cancel(); var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); explorerQueryRequest = request; int revision = ++explorerQueryGeneration;
            try
            {
                await Task.Delay(180, request.Token); var index = explorerIndex;
                if (index is null) { if (page == "search") { results.Children.Clear(); countLabel.Text = I18n.T("hubNoIndexLoaded"); } return; }
                string text = explorerSearchQuery; var filter = LocalSearch.Parse(text, DateTime.UtcNow);
                var matches = await Task.Run(() => LocalSearch.Find(index, filter, request.Token), request.Token);
                request.Token.ThrowIfCancellationRequested(); if (page != "search" || revision != explorerQueryGeneration || !ReferenceEquals(explorerQueryRequest, request)) return;
                interpretation.Text = string.Format(I18n.T("hubQueryInterpretation"), filter.Extension ?? I18n.T("hubAnyType"), filter.SinceUtc?.ToString("u", CultureInfo.InvariantCulture) ?? I18n.T("hubAnyTime"), string.Join(" · ", filter.Terms));
                results.Children.Clear(); countLabel.Text = string.Format(I18n.T("hubSearchResultCount"), matches.Count); selectedPath.Text = ""; previewText.Clear();
                foreach (var document in matches)
                {
                    var row = Stack(); row.Children.Add(Text(document.Name, 14)); row.Children.Add(Text(document.Path, 11, true)); row.Children.Add(Text(ExplorerBytes(document.Bytes) + " · " + I18n.T(document.Coverage), 11, true));
                    var button = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), ToolTip = I18n.T("hubPreviewOnly") };
                    DeviceId(button, "HubSearchResult-" + results.Children.Count); AutomationProperties.SetName(button, document.Name + ": " + I18n.T("hubPreviewOnly"));
                    button.Click += (_, _) => { selectedPath.Text = document.Path; previewText.Text = document.Text.Length > 0 ? document.Text : I18n.T(document.Coverage); }; results.Children.Add(button);
                }
            }
            catch (OperationCanceledException) { }
            catch (ArgumentException) { if (page == "search" && revision == explorerQueryGeneration) countLabel.Text = I18n.T("hubQueryInvalid"); }
            finally { if (ReferenceEquals(explorerQueryRequest, request)) explorerQueryRequest = null; request.Dispose(); }
        }
        query.TextChanged += async (_, _) => { explorerSearchQuery = query.Text; await DrawQuery(); }; _ = DrawQuery();
    }
    private void TimelinePage(StackPanel body)
    {
        if (!Guard(body, "timeline")) return;
        Help(body, "hubTimelinePrivacy");
        var controls = Stack(); var range = new ComboBox { MinWidth = 180 };
        foreach (int days in new[] { 1, 7, 30 }) range.Items.Add(new ComboBoxItem { Content = string.Format(I18n.T("hubLookbackDays"), days), Tag = days });
        range.SelectedIndex = Array.IndexOf(new[] { 1, 7, 30 }, explorerTimelineDays); DeviceId(range, "HubTimelineRange"); ExplorerName(range, "hubTimelineRange");
        range.SelectionChanged += (_, _) => { if (range.SelectedItem is ComboBoxItem { Tag: int days }) explorerTimelineDays = days; }; controls.Children.Add(range);
        var actions = new WrapPanel();
        actions.Children.Add(HubButton("hubCollectTimeline", async ct =>
        {
            int days = explorerTimelineDays;
            var snapshot = smokeMode && TimelineSourceForSmoke is not null ? await TimelineSourceForSmoke(days, ct) : await ProbeRunner.Read<TimelineSnapshot>("timeline", ct, days: days);
            ValidateExplorerTimeline(snapshot); ct.ThrowIfCancellationRequested(); explorerTimeline = snapshot; explorerMeasuredTimelineDays = days; if (page == "timeline") Render(true);
        }, true));
        var save = HubButton("hubSaveTimeline", async ct => { if (explorerTimeline is not null) { await InvestigationStore.Save("timeline", explorerTimeline, ct); SetOperationStatus("hubSavedLocally"); } }); save.IsEnabled = explorerTimeline is not null; actions.Children.Add(save);
        actions.Children.Add(HubButton("hubLoadTimeline", async ct =>
        {
            var saved = await InvestigationStore.Load<TimelineSnapshot>("timeline", ct); if (saved is null) { SetOperationStatus("hubNoSavedTimeline"); return; }
            ValidateExplorerTimeline(saved); ct.ThrowIfCancellationRequested(); explorerTimeline = saved; explorerMeasuredTimelineDays = 0; if (page == "timeline") Render(true);
        })); controls.Children.Add(actions); body.Children.Add(Card(controls));
        body.Children.Add(Text(I18n.T("hubProblemOnset"), 13)); var onset = HubTextBox("HubTimelineOnset", explorerOnset); onset.MaxLength = 16; onset.FlowDirection = FlowDirection.LeftToRight; onset.Tag = "yyyy-MM-dd HH:mm"; ExplorerName(onset, "hubProblemOnset"); body.Children.Add(onset);
        var status = Text("", 12, true); DeviceId(status, "HubTimelineStatus"); body.Children.Add(status); var rows = Stack(); DeviceId(rows, "HubTimelineRows"); body.Children.Add(rows);
        void Draw()
        {
            rows.Children.Clear(); var snapshot = explorerTimeline; if (snapshot is null) { status.Text = I18n.T("hubNoReadings"); return; }
            DateTimeOffset? problem = null;
            if (explorerOnset.Trim().Length > 0)
            {
                if (!DateTime.TryParseExact(explorerOnset.Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) || date > DateTime.Now.AddMinutes(1) || TimeZoneInfo.Local.IsInvalidTime(date)) { status.Text = I18n.T("caseDateInvalid"); return; }
                problem = new DateTimeOffset(date).ToUniversalTime();
            }
            var visible = snapshot.Entries.Where(entry => problem is null || entry.Time is { } time && time <= problem.Value && time >= problem.Value.AddHours(-48)).OrderByDescending(entry => entry.Time).ToArray();
            status.Text = HubTime(snapshot.Collected) + " · " + string.Format(I18n.T("hubTimelineShown"), Math.Min(100, visible.Length), visible.Length) +
                "\n" + (explorerMeasuredTimelineDays > 0 ? string.Format(I18n.T("hubLookbackDays"), explorerMeasuredTimelineDays) : I18n.T("hubLookbackUnavailable"));
            if (problem is not null) rows.Children.Add(Text(I18n.T("hubChronologyOnly"), 12, true));
            foreach (string note in snapshot.Notices) rows.Children.Add(Text(I18n.T(note), 12, true));
            foreach (var entry in visible.Take(100))
            {
                var row = Stack(); row.Children.Add(Text((entry.Time is { } time ? HubTime(time) : I18n.T("hubUnavailable")) + " · " + I18n.T(entry.Kind), 11, true)); row.Children.Add(Text(entry.Name, 14)); row.Children.Add(Text(I18n.Words.ContainsKey(entry.Detail) ? I18n.T(entry.Detail) : entry.Detail, 12, true)); rows.Children.Add(Card(row, 12));
            }
            var report = new InvestigationReport("timeline", snapshot.Collected, visible.Take(40).Select(entry => new InvestigationFact(entry.Kind, entry.Name, (entry.Time?.ToString("u", CultureInfo.InvariantCulture) ?? "unavailable") + " · " + (I18n.Words.ContainsKey(entry.Detail) ? I18n.T(entry.Detail) : entry.Detail))).ToArray(), ["hubChronologyOnly", .. snapshot.Notices]);
            HubShare(rows, HubReportText(report));
        }
        onset.TextChanged += (_, _) => { explorerOnset = onset.Text; Draw(); }; Draw();
    }
    private static void ValidateExplorerTimeline(TimelineSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Entries is null || snapshot.Notices is null || snapshot.Entries.Count > 400 || snapshot.Notices.Count > 30 ||
            snapshot.Entries.Any(e => e is null || e.Name is null || e.Name.Length > 2000 || e.Detail is null || e.Detail.Length > 3000 || e.Kind is not ("hubCrashEvent" or "hubSystemEvent" or "hubUpdateEvent" or "hubDriverEvent" or "hubProgramDate")) || snapshot.Notices.Any(n => n is null || n.Length > 100))
            throw new InvalidDataException("Invalid local timeline.");
    }
    private void UsbPage(StackPanel body)
    {
        if (!Guard(body, "usb")) return;
        Help(body, "hubUsbLimits");
        body.Children.Add(HubButton("hubRefreshUsb", async ct =>
        {
            var collected = smokeMode && UsbSourceForSmoke is not null ? await UsbSourceForSmoke(ct) : await ProbeRunner.Read<InvestigationReport>("usb", ct);
            ct.ThrowIfCancellationRequested(); explorerUsb = collected; if (page == "usb") Render(true);
        }, true));
        if (explorerUsb is { } report) { HubReport(body, report); HubShare(body, HubReportText(report)); }
        else body.Children.Add(Text(I18n.T("hubNoReadings"), 12, true));
        SectionHeading(body, "hubReadTest"); Help(body, "hubReadTestLimits");
        var test = Stack(); test.Children.Add(ExplorerPath("HubUsbReadPath", explorerUsbFile, "hubUsbTestFile"));
        test.Children.Add(HubButton("hubSelectExternalFile", _ =>
        {
            string? selected = ExplorerPickFile(); if (selected is null) return Task.CompletedTask;
            LocalFileScope.RequireFile(Path.GetDirectoryName(Path.GetFullPath(selected))!, selected); explorerUsbFile = Path.GetFullPath(selected); explorerUsbRead = null; explorerUsbReadAcknowledged = false; if (page == "usb") Render(true); return Task.CompletedTask;
        }));
        var consent = ExplorerCheck("hubReadConsent", "HubUsbReadConsent", explorerUsbReadAcknowledged, value => explorerUsbReadAcknowledged = value); test.Children.Add(consent);
        var read = HubButton("hubRunReadTest", async ct =>
        {
            if (!explorerUsbReadAcknowledged || explorerUsbFile.Length == 0) { SetOperationStatus("hubReadConsentRequired"); return; }
            string file = explorerUsbFile;
            var result = smokeMode && UsbReadForSmoke is not null ? await UsbReadForSmoke(file, ct) : await StorageActions.ReadTest(file, ct);
            ct.ThrowIfCancellationRequested(); if (file != explorerUsbFile) return; explorerUsbRead = result; if (page == "usb") Render(true);
        }); read.IsEnabled = explorerUsbFile.Length > 0 && explorerUsbReadAcknowledged; test.Children.Add(read);
        consent.Checked += (_, _) => read.IsEnabled = explorerUsbFile.Length > 0; consent.Unchecked += (_, _) => read.IsEnabled = false; body.Children.Add(Card(test));
        if (explorerUsbRead is { } reading) { HubReport(body, reading); HubShare(body, HubReportText(reading), suffix: "-read"); }
    }
}
