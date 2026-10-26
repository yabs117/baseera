using System.IO.Compression;
using System.Net;
using System.Net.Http;
using Baseera.App.Services;
using Baseera.Core;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Baseera.App;

internal static class InvestigationServiceSmokeChecks
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ReputationStub : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Require(request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri ==
                "https://www.virustotal.com/api/v3/files/" + new string('a', 64), "Reputation sent more than the selected hash.");
            Require(request.Content is null && request.Headers.GetValues("x-apikey").Single() == "fixture-key", "Reputation request was malformed.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"data\":{\"attributes\":{\"last_analysis_stats\":{\"malicious\":1,\"suspicious\":0,\"undetected\":9},\"last_analysis_date\":1700000000}}}") });
        }
    }

    internal static async Task Run(string output)
    {
        string root = Path.GetFullPath(Path.Combine(output, "investigation-fixtures-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var fixture = await ProbeRunner.Read<InvestigationReport>("fixture", CancellationToken.None);
        Require(fixture.Kind == "doctor" && fixture.Facts.Single().Value == "fixture-only", "Isolated local probe handshake failed.");

        string plain = Path.Combine(root, "sample.txt");
        await File.WriteAllTextAsync(plain, "Baseera local search fixture");
        var text = await ProbeRunner.Read<ExtractedText>("extract", CancellationToken.None, plain, root);
        Require(text.Text.Contains("local search fixture") && text.Coverage == "hubTextLimited", "Text extraction from isolated probe failed.");

        string office = Path.Combine(root, "sample.docx");
        using (var zip = ZipFile.Open(office, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            await writer.WriteAsync("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Office fixture</w:t></w:r></w:p></w:body></w:document>");
        var officeText = await ProbeRunner.Read<ExtractedText>("extract", CancellationToken.None, office, root);
        Require(officeText.Text.Contains("Office fixture"), "Bounded Office extraction failed.");

        string pdf = Path.Combine(root, "sample.pdf");
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(300, 300).AddText("PDF fixture", 12, new PdfPoint(20, 260), font);
        await File.WriteAllBytesAsync(pdf, builder.Build());
        var pdfText = await ProbeRunner.Read<ExtractedText>("extract", CancellationToken.None, pdf, root);
        Require(pdfText.Text.Contains("PDF fixture"), "Isolated PDF extraction failed.");
        await File.WriteAllTextAsync(pdf, "%PDF-1.7 invalid fixture");
        bool corruptRejected = false;
        try { await ProbeRunner.Read<ExtractedText>("extract", CancellationToken.None, pdf, root); }
        catch (IOException ex) { corruptRejected = !ex.Message.Contains(root, StringComparison.OrdinalIgnoreCase); }
        Require(corruptRejected, "Corrupt PDF was not rejected without exposing its path.");

        var signature = await ProbeRunner.Read<InvestigationReport>("signature", CancellationToken.None, plain, root);
        Require(signature.Facts.Any(f => f.Label == "hubSha256" && f.Value.Length == 64) &&
            signature.Facts.Any(f => f.Label == "hubAuthenticode"), "Offline Authenticode inspection failed.");
        bool writeBlocked = false, replacementBlocked = false;
        string replacement = Path.Combine(root, "replacement.txt"); await File.WriteAllTextAsync(replacement, "replacement");
        var stableSignature = await FileTrust.Inspect(plain, () =>
        {
            try { File.WriteAllText(plain, "changed"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { writeBlocked = true; }
            try { File.Move(replacement, plain, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { replacementBlocked = true; }
        });
        Require(writeBlocked && replacementBlocked && stableSignature.Facts.Single(f => f.Label == "hubSha256").Value == signature.Facts.Single(f => f.Label == "hubSha256").Value,
            "Signature and hash did not retain one file identity across verification.");

        var report = new InvestigationReport("network", DateTimeOffset.UtcNow,
            [new("hubFixture", "private fixture")], ["hubNetworkLimits"]);
        await InvestigationStore.Save("network", report, CancellationToken.None);
        var loaded = await InvestigationStore.Load<InvestigationReport>("network", CancellationToken.None);
        Require(loaded?.Facts.Single().Value == "private fixture", "Protected local history failed to round-trip.");
        string protectedPath = Path.Combine(Settings.Folder, "Investigations", "network.data");
        Require(!File.ReadAllText(protectedPath).Contains("private fixture"), "Protected history leaked plaintext.");

        var handler = new ReputationStub(); using var transport = new HttpClient(handler);
        var reputation = await FileTrust.Reputation(new string('a', 64), "fixture-key", CancellationToken.None, transport);
        Require(handler.Calls == 1 && reputation.Facts.Any(f => f.Label == "hubReputation" && f.Value == "1 / 10"), "Opt-in hash reputation failed.");
        File.WriteAllText(Path.Combine(output, "investigation-services-result.txt"),
            "PASS: bounded child handshake; text, Office and PDF extraction; corrupt PDF rejection; offline fixture signature; concurrent write/replacement refused across hash and signature; DPAPI history; fake hash-only reputation request. No live device/event/network reads or cleanup.");
    }
}
