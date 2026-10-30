using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SmartInbox.Application;
using ReplyDraft = SmartInbox.Domain.ReplyDraft;

namespace SmartInbox.Infrastructure.Drafting;

/// <summary>
/// Word file of a reply: organisation letterhead, the English letter, then the Arabic letter on its own page
/// (right-to-left). Built with the open-source Open XML SDK, no Word installation needed.
/// </summary>
public class DocxDraftExporter(DraftingOptions options) : IDraftExporter
{
    public byte[] Export(ReplyDraft draft)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();

            Letter(body, options.OrganisationNameEn, draft.SubjectEn, draft.BodyEn, rtl: false);
            body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
            Letter(body, options.OrganisationNameAr, draft.SubjectAr, draft.BodyAr, rtl: true);

            body.Append(new SectionProperties(
                new PageSize { Width = 11906, Height = 16838 },                                       // A4
                new PageMargin { Top = 1440, Bottom = 1440, Left = 1300, Right = 1300, Header = 708, Footer = 708 }));
            main.Document = new Document(body);
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static void Letter(Body body, string organisation, string? subject, string? text, bool rtl)
    {
        body.Append(Paragraph(organisation, rtl, bold: true, size: 32, align: JustificationValues.Center, after: 360));
        if (!string.IsNullOrWhiteSpace(subject))
            body.Append(Paragraph($"{(rtl ? "الموضوع: " : "Subject: ")}{subject}", rtl, bold: true, after: 240));
        foreach (var line in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            body.Append(Paragraph(line, rtl, after: 120));
    }

    private static Paragraph Paragraph(string text, bool rtl, bool bold = false, int size = 24, JustificationValues? align = null, int after = 0)
    {
        var props = new ParagraphProperties(new SpacingBetweenLines { After = after.ToString(), Line = "300", LineRule = LineSpacingRuleValues.Auto });
        if (rtl) props.Append(new BiDi());
        props.Append(new Justification { Val = align ?? (rtl ? JustificationValues.Right : JustificationValues.Left) });

        var runProps = new RunProperties(
            new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial" },
            new FontSize { Val = size.ToString() }, new FontSizeComplexScript { Val = size.ToString() });
        if (bold) runProps.Append(new Bold(), new BoldComplexScript());
        if (rtl) runProps.Append(new RightToLeftText());

        return new Paragraph(props, new Run(runProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }
}
