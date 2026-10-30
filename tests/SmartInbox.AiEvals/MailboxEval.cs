using System.Text;
using MimeKit;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Mailbox;

/// <summary>
/// Do letters that arrive by email get the same analysis as uploads? Each sample goes three ways: uploaded directly,
/// attached to an email, and pasted into an email body (text samples) — through the real MIME parser and extractors.
/// </summary>
static class MailboxEval
{
    public record Way(string Name, string? Category, string? Priority, int? DepartmentId, string? Error);

    public record Row(string File, string ExpectedCategory, string ExpectedPriority, int ExpectedDepartment, IReadOnlyList<Way> Ways);

    public static async Task<IReadOnlyList<Row>> RunAsync(
        IEnumerable<(string File, string Category, string Priority, int DepartmentId)> labels, string samplesDir,
        IAiProvider ai, IReadOnlyList<ITextExtractor> extractors, IReadOnlyList<Department> departments, DateOnly sentOn)
    {
        // Realistic: the email is sent on the eval's "today", like a letter emailed the day it's dealt with.
        _sentOn = new DateTimeOffset(sentOn.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(4));
        var rows = new List<Row>();
        foreach (var l in labels)
        {
            var path = Path.Combine(samplesDir, l.File);
            var bytes = await File.ReadAllBytesAsync(path);
            var ways = new List<Way>
            {
                await AnalyseAsync("Upload", l.File, bytes, ai, extractors, departments),
                await AnalyseAttachedAsync(l.File, bytes, ai, extractors, departments),
            };
            if (Path.GetExtension(l.File) == ".txt")
                ways.Add(await AnalyseInBodyAsync(Encoding.UTF8.GetString(bytes), ai, extractors, departments));
            rows.Add(new Row(l.File, l.Category, l.Priority, l.DepartmentId, ways));
        }
        return rows;
    }

    private static async Task<Way> AnalyseAttachedAsync(string file, byte[] bytes, IAiProvider ai, IReadOnlyList<ITextExtractor> extractors, IReadOnlyList<Department> departments)
    {
        var body = new BodyBuilder { TextBody = "Dear Sir/Madam,\n\nPlease find our letter attached.\n\nRegards" };
        body.Attachments.Add(file, bytes);
        var parsed = ImapMailboxClient.Parse(1, Envelope(body.ToMessageBody()));
        var attachment = parsed.Attachments.Single();
        return await AnalyseAsync("Email attachment", attachment.FileName, attachment.Content, ai, extractors, departments);
    }

    private static async Task<Way> AnalyseInBodyAsync(string text, IAiProvider ai, IReadOnlyList<ITextExtractor> extractors, IReadOnlyList<Department> departments)
    {
        var parsed = ImapMailboxClient.Parse(1, Envelope(new TextPart("plain") { Text = text }));
        return await AnalyseAsync("Email body", "letter.eml", parsed.Raw, ai, extractors, departments);
    }

    private static DateTimeOffset _sentOn;

    private static MimeMessage Envelope(MimeEntity body)
    {
        var m = new MimeMessage { Date = _sentOn };
        m.From.Add(new MailboxAddress("Sender", "sender@partner.gov"));
        m.To.Add(new MailboxAddress("Correspondence", "correspondence@mds.demo"));
        m.Subject = "Correspondence";
        m.Body = body;
        return m;
    }

    private static async Task<Way> AnalyseAsync(string name, string file, byte[] bytes, IAiProvider ai, IReadOnlyList<ITextExtractor> extractors, IReadOnlyList<Department> departments)
    {
        try
        {
            var type = UploadRules.ResolveContentType(file) ?? "text/plain";
            var text = await extractors.First(e => e.CanHandle(type, file)).ExtractAsync(new MemoryStream(bytes));
            var r = await ai.AnalyzeAsync(AnalysisInput.FromText(text!), departments);
            return new Way(name, r.Category.ToString(), r.Priority.ToString(), r.SuggestedDepartmentId, null);
        }
        catch (Exception ex)
        {
            return new Way(name, null, null, null, ex.Message);
        }
    }

    public static string Report(IReadOnlyList<Row> rows, string engine, DateOnly today)
    {
        var names = new[] { "Upload", "Email attachment", "Email body" };
        string Pct(string way, Func<Row, Way, bool> ok)
        {
            var pairs = rows.Select(r => (r, w: r.Ways.FirstOrDefault(w => w.Name == way))).Where(p => p.w is not null).ToList();
            return pairs.Count == 0 ? "–" : $"{pairs.Count(p => ok(p.r, p.w!)) * 100.0 / pairs.Count:0}% ({pairs.Count})";
        }
        bool Same(Row r, Way w) => r.Ways[0] is var u && u.Category == w.Category && u.Priority == w.Priority && u.DepartmentId == w.DepartmentId;

        var md = new StringBuilder();
        md.AppendLine("# Mailbox Evals");
        md.AppendLine();
        md.AppendLine($"Do letters that arrive by email get the same analysis as uploads? Engine: **{engine}** · {rows.Count} synthetic letters · run {DateTime.UtcNow:yyyy-MM-dd} · \"today\" pinned to {today:yyyy-MM-dd}.");
        md.AppendLine();
        md.AppendLine("Each letter goes through the real MIME parser and text extractors three ways: uploaded, attached to an email, and pasted into an email body (text letters only; the email's From/To/Date/Subject lines come along). Test emails are dated on the pinned \"today\".");
        md.AppendLine();
        md.AppendLine("Known behaviour: for a letter pasted into an email body, the email's Date line is taken as the document date, so relative deadlines (\"within 30 days\") count from when the email was sent. Attach letters as files to keep their own date.");
        md.AppendLine();
        md.AppendLine("| Way in | Department | Category | Priority | Same result as upload |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var n in names)
            md.AppendLine($"| {n} | {Pct(n, (r, w) => w.DepartmentId == r.ExpectedDepartment)} | {Pct(n, (r, w) => w.Category == r.ExpectedCategory)} " +
                          $"| {Pct(n, (r, w) => w.Priority == r.ExpectedPriority)} | {(n == "Upload" ? "—" : Pct(n, Same))} |");
        md.AppendLine();
        md.AppendLine("Percentages are over the letters each way applies to (count in brackets).");
        md.AppendLine();
        md.AppendLine("<details><summary>Letters whose result changed</summary>");
        md.AppendLine();
        var changed = rows.SelectMany(r => r.Ways.Skip(1).Where(w => !Same(r, w)).Select(w => (r, w))).ToList();
        if (changed.Count == 0) md.AppendLine("None.");
        else
        {
            md.AppendLine("| Letter | Way in | Upload → this way (department / category / priority) |");
            md.AppendLine("|---|---|---|");
            foreach (var (r, w) in changed)
            {
                var u = r.Ways[0];
                md.AppendLine($"| {r.File} | {w.Name} | {u.DepartmentId}/{u.Category}/{u.Priority} → {w.DepartmentId}/{w.Category}/{w.Priority}{(w.Error is null ? "" : $" ❌ {w.Error}")} |");
            }
        }
        md.AppendLine();
        md.AppendLine("</details>");
        return md.ToString();
    }
}
