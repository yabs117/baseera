using Baseera.Core;

namespace Baseera.App.Services;

internal static class SearchIndexer
{
    internal static async Task<SearchIndex> Build(string root, bool contents, CancellationToken token, IProgress<int>? progress = null)
    {
        root = LocalFileScope.Root(root); var notices = new List<string>();
        var files = await Task.Run(() => LocalFileScope.Walk(root, new(10000, 15), notices, token), token); var documents = new List<IndexedDocument>();
        int attempted = 0; long textBytes = 0;
        foreach (var file in files.Take(1000))
        {
            token.ThrowIfCancellationRequested(); string text = "", coverage = "hubNameOnly"; string ext = Path.GetExtension(file.Path).ToLowerInvariant();
            if (contents && textBytes < 4000000 && attempted < 200 && new[] { ".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".md", ".json", ".xml", ".log", ".csv" }.Contains(ext))
            {
                attempted++;
                try
                {
                    var content = await ProbeRunner.Read<ExtractedText>("extract", token, file.Path, root); token.ThrowIfCancellationRequested();
                    if (!LocalFileScope.Unchanged(file)) throw new IOException("File changed.");
                    text = content.Text; coverage = content.Coverage;
                    if (text.Length > 8000) throw new InvalidDataException("Extraction limit."); textBytes += text.Length;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { coverage = "hubExtractorLimit"; }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { coverage = "hubUnreadable"; }
            }
            documents.Add(new(file.Path, Path.GetFileName(file.Path), ext, file.Bytes, file.LastWriteUtc, text, coverage)); progress?.Report(documents.Count);
        }
        if (files.Count > 1000 || attempted >= 200 || textBytes >= 4000000) notices.Add("hubPartial");
        var result = new SearchIndex(DateTimeOffset.UtcNow, root, documents, notices.Distinct().ToArray()); await InvestigationStore.Save("search", result, token); return result;
    }
    internal static void Validate(SearchIndex index)
    {
        if (index is null || index.Root is null || index.Documents is null || index.Notices is null || index.Documents.Count > 1000 || index.Notices.Count > 30) throw new InvalidDataException("Invalid local index.");
        string root = Path.GetFullPath(index.Root);
        foreach (var d in index.Documents)
            if (d is null || !LocalFileScope.Contains(root, d.Path) || d.Text is null || d.Text.Length > 8000 || d.Name is null || d.Name.Length > 500 || d.Extension is null || d.Extension.Length > 100 || d.Bytes < 0 || d.Coverage is null || d.Coverage.Length > 100) throw new InvalidDataException("Invalid index document.");
    }
}
