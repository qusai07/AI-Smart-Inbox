using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MimeKit;
using SmartInbox.Application;
using UglyToad.PdfPig;

namespace SmartInbox.Infrastructure.TextExtraction;

/// <summary>Native (digital) PDFs. Returns null for scanned PDFs so the AI reads them with vision.</summary>
public class PdfTextExtractor : ITextExtractor
{
    // Fewer characters than this per page means there is no real text layer.
    private const int MinCharsPerPage = 40;

    public bool CanHandle(string contentType, string fileName) => contentType == "application/pdf";

    public async Task<string?> ExtractAsync(Stream content, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);

        using var pdf = PdfDocument.Open(ms.ToArray());
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            sb.AppendLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page));
        }

        var text = sb.ToString().Trim();
        return text.Length < MinCharsPerPage * Math.Max(1, pdf.NumberOfPages) ? null : text;
    }
}

/// <summary>Reads paragraphs straight from word/document.xml — no Office dependency needed.</summary>
public class DocxTextExtractor : ITextExtractor
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public bool CanHandle(string contentType, string fileName) =>
        contentType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public async Task<string?> ExtractAsync(Stream content, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null) return null;

        await using var xml = entry.Open();
        var doc = await XDocument.LoadAsync(xml, LoadOptions.None, ct);
        var paragraphs = doc.Descendants(W + "p")
            .Select(p => string.Concat(p.Descendants().Select(e =>
                e.Name == W + "t" ? e.Value :
                e.Name == W + "tab" ? "\t" :
                e.Name == W + "br" ? "\n" : "")));
        var text = string.Join("\n", paragraphs).Trim();
        return text.Length == 0 ? null : text;
    }
}

public class EmlTextExtractor : ITextExtractor
{
    public bool CanHandle(string contentType, string fileName) => contentType == "message/rfc822";

    public async Task<string?> ExtractAsync(Stream content, CancellationToken ct = default)
    {
        var message = await MimeMessage.LoadAsync(content, ct);
        var sb = new StringBuilder();
        sb.AppendLine($"From: {message.From}");
        sb.AppendLine($"To: {message.To}");
        if (message.Date != DateTimeOffset.MinValue) sb.AppendLine($"Date: {message.Date:yyyy-MM-dd}");
        sb.AppendLine($"Subject: {message.Subject}");
        sb.AppendLine();
        sb.AppendLine(message.TextBody ?? StripHtml(message.HtmlBody) ?? "");

        var attachments = message.Attachments.Select(a => a.ContentDisposition?.FileName ?? a.ContentType.Name).Where(n => n is not null).ToList();
        if (attachments.Count > 0) sb.AppendLine($"\nAttachments: {string.Join(", ", attachments)}");
        return sb.ToString().Trim();
    }

    private static string? StripHtml(string? html) =>
        html is null ? null : System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
}

public class PlainTextExtractor : ITextExtractor
{
    public bool CanHandle(string contentType, string fileName) => contentType == "text/plain";

    public async Task<string?> ExtractAsync(Stream content, CancellationToken ct = default)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = (await reader.ReadToEndAsync(ct)).Trim();
        return text.Length == 0 ? null : text;
    }
}
