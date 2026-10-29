using System.IO.Compression;
using System.Text;
using SmartInbox.Infrastructure.TextExtraction;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace SmartInbox.UnitTests;

public class TextExtractorTests
{
    [Fact]
    public async Task PlainText_ReadsUtf8Arabic()
    {
        var text = "الموضوع: شكوى بخصوص تأخر إصدار شهادة";
        var result = await new PlainTextExtractor().ExtractAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(text, result);
    }

    [Fact]
    public async Task Eml_IncludesHeadersAndBody()
    {
        const string eml = """
            From: "Service Desk" <desk@customs.demo>
            To: itops@mds.demo
            Subject: Payment gateway errors
            Date: Mon, 15 Feb 2027 09:12:00 +0400
            Content-Type: text/plain; charset=utf-8

            Payments fail with error PG-502.
            """;
        var result = await new EmlTextExtractor().ExtractAsync(new MemoryStream(Encoding.UTF8.GetBytes(eml)));

        Assert.Contains("Subject: Payment gateway errors", result);
        Assert.Contains("desk@customs.demo", result);
        Assert.Contains("Date: 2027-02-15", result);
        Assert.Contains("PG-502", result);
    }

    [Fact]
    public async Task Docx_ReadsParagraphs()
    {
        var result = await new DocxTextExtractor().ExtractAsync(BuildDocx("Subject: Secondment request", "Please nominate two engineers."));
        Assert.Equal("Subject: Secondment request\nPlease nominate two engineers.", result);
    }

    [Fact]
    public async Task Pdf_WithTextLayer_ReturnsText()
    {
        var pdf = BuildPdf("Notice of hearing. The claimant has filed a claim for breach of contract relating to Contract MDS-C-2024-019.");
        var result = await new PdfTextExtractor().ExtractAsync(new MemoryStream(pdf));
        Assert.NotNull(result);
        Assert.Contains("MDS-C-2024-019", result);
    }

    [Fact]
    public async Task Pdf_WithoutTextLayer_ReturnsNullSoVisionIsUsed()
    {
        var result = await new PdfTextExtractor().ExtractAsync(new MemoryStream(BuildPdf(null)));
        Assert.Null(result);
    }

    private static MemoryStream BuildDocx(params string[] paragraphs)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var body = string.Concat(paragraphs.Select(p => $"<w:p><w:r><w:t>{p}</w:t></w:r></w:p>"));
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            writer.Write($"""<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{body}</w:body></w:document>""");
        }
        ms.Position = 0;
        return ms;
    }

    private static byte[] BuildPdf(string? text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        if (text is not null)
        {
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText(text, 10, new UglyToad.PdfPig.Core.PdfPoint(25, 700), font);
        }
        return builder.Build();
    }
}
