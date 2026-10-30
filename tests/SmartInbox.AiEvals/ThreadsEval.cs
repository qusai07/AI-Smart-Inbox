using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Persistence;

/// <summary>
/// Does Smart Inbox link the right letters, and only those? Each sample letter is the start of labelled cases, run
/// through the real upload → analysis → matching pipeline on a fresh database: the same letter sent twice, retyped,
/// a reminder in the same case, a reply quoting our outgoing number, and a look-alike (same reference, another
/// sender) that must NOT be linked. The truth comes from how each case is built, so a miss by the analysis engine
/// (e.g. a reference it didn't read) counts as a miss.
/// </summary>
static partial class ThreadsEval
{
    public record Case(string Sample, string Name, LinkKind? Expected, LinkKind? Found, bool RightLetter, string? Evidence);

    public static async Task<IReadOnlyList<Case>> RunAsync(IReadOnlyList<string> files, string samplesDir, IAiProvider ai, IReadOnlyList<ITextExtractor> extractors)
    {
        var letters = new List<(string File, byte[] Bytes, string Text)>();
        foreach (var f in files)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(samplesDir, f));
            var type = UploadRules.ResolveContentType(f) ?? "text/plain";
            var text = await extractors.First(e => e.CanHandle(type, f)).ExtractAsync(new MemoryStream(bytes), default) ?? "";
            letters.Add((f, bytes, text));
        }

        var cases = new List<Case>();
        for (var i = 0; i < letters.Count; i++)
        {
            var (file, bytes, text) = letters[i];
            var arabic = file.StartsWith("ar-", StringComparison.Ordinal);

            cases.Add(await RunCaseAsync(file, "Sent twice", LinkKind.Duplicate, ai, extractors, bytes, file, bytes, file));
            cases.Add(await RunCaseAsync(file, "Retyped", LinkKind.Duplicate, ai, extractors, bytes, file, Encoding.UTF8.GetBytes(Retype(text)), "retyped.txt"));

            var reference = ReferenceIn(text);
            if (reference is not null)
            {
                cases.Add(await RunCaseAsync(file, "Reminder, same sender", LinkKind.FollowUp, ai, extractors, bytes, file,
                    Encoding.UTF8.GetBytes(Reminder(Letterhead(text), reference, arabic)), "reminder.txt"));
                // Another sender's letterhead quoting the same reference: different case, must not be linked.
                var other = letters[(i + 1) % letters.Count];
                cases.Add(await RunCaseAsync(file, "Look-alike, other sender", null, ai, extractors, bytes, file,
                    Encoding.UTF8.GetBytes(Reminder(Letterhead(other.Text), reference, arabic)), "lookalike.txt"));
            }

            cases.Add(await RunCaseAsync(file, "Replies to our letter", LinkKind.RepliesToOurLetter, ai, extractors, bytes, file,
                Encoding.UTF8.GetBytes(ReplyToUs(Letterhead(text), arabic)), "reply.txt", ourLetter: true));
        }
        return cases;
    }

    private const string OurNumber = "MDS/OUT/2026/0042";

    private static async Task<Case> RunCaseAsync(
        string sample, string name, LinkKind? expected, IAiProvider ai, IReadOnlyList<ITextExtractor> extractors,
        byte[] firstBytes, string firstName, byte[] secondBytes, string secondName, bool ourLetter = false)
    {
        await using var db = new SmartInboxDbContext(new DbContextOptionsBuilder<SmartInboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync();
        var storage = new MemoryStorage();
        var service = new DocumentService(db, storage, new NoQueue(), new NoNotifier());
        var processor = new DocumentProcessor(db, storage, extractors, ai, new NoNotifier(), NullLogger<DocumentProcessor>.Instance,
            related: new RelatedLetterFinder(db, new ThreadOptions(), new RegisterOptions(), TimeProvider.System));

        async Task<Document> Receive(byte[] bytes, string fileName)
        {
            var doc = await service.UploadAsync(new MemoryStream(bytes), fileName, UploadRules.ResolveContentType(fileName) ?? "text/plain", "eval", default);
            await processor.ProcessAsync(doc.Id, default);
            return doc;
        }

        var first = await Receive(firstBytes, firstName);
        if (ourLetter)
        {
            db.OutgoingLetters.Add(new OutgoingLetter { Number = OurNumber, Year = 2026, Sequence = 42, DepartmentId = 1, DocumentId = first.Id });
            await db.SaveChangesAsync();
        }
        var second = await Receive(secondBytes, secondName);

        var link = await db.DocumentLinks.AsNoTracking()
            .Where(l => l.DocumentId == second.Id || l.RelatedDocumentId == second.Id)
            .OrderBy(l => l.Kind)
            .FirstOrDefaultAsync();
        var right = link is not null && (link.RelatedDocumentId == first.Id || link.DocumentId == first.Id);
        return new Case(sample, name, expected, link?.Kind, right, link?.Evidence);
    }

    // ── Building the cases ───────────────────────────────────────────────────

    /// <summary>The same letter typed again: different line breaks, spacing and capitals.</summary>
    private static string Retype(string text) =>
        Spaces().Replace(text, m => m.Value.Contains('\n') ? "\r\n\r\n" : "  ").ToUpperInvariant();

    /// <summary>Who the letter is from: its "From:" / "من:" line when it has one, else the first two lines (the letterhead).</summary>
    private static string Letterhead(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var from = lines.FirstOrDefault(l => l.StartsWith("From:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("من:", StringComparison.Ordinal));
        return from ?? string.Join('\n', lines.Take(2));
    }

    private static string Reminder(string letterhead, string reference, bool arabic) => arabic
        ? $"{letterhead}\n\nالرقم: {reference}\nالتاريخ: 10 فبراير 2027\n\nتذكير\n\nإشارة إلى كتابنا رقم {reference}، لم نتلقَّ ردكم حتى الآن. يرجى التكرم بالاهتمام بالموضوع.\n\nوتفضلوا بقبول فائق الاحترام"
        : $"{letterhead}\n\nRef: {reference}\nDate: 10 February 2027\n\nREMINDER\n\nFurther to our letter ref. {reference}, we have not yet received your response. Kindly treat this matter as urgent.\n\nYours faithfully";

    private static string ReplyToUs(string letterhead, bool arabic) => arabic
        ? $"{letterhead}\n\nالتاريخ: 12 فبراير 2027\n\nإشارة إلى كتابكم رقم {OurNumber}، نفيدكم بأننا اطلعنا على ما ورد فيه وسنوافيكم بالرد خلال أسبوع.\n\nوتفضلوا بقبول فائق الاحترام"
        : $"{letterhead}\n\nDate: 12 February 2027\n\nWith reference to your letter No. {OurNumber}, we confirm receipt and will revert within a week.\n\nYours faithfully";

    /// <summary>The reference number as the letter states it ("Ref: …", "Case No.: …", "الرقم: …"), or null.</summary>
    private static string? ReferenceIn(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var m = RefLine().Match(line);
            if (m.Success) return m.Groups["ref"].Value.TrimEnd('.', '،');
        }
        return null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"(Our Ref\.?|Your Ref\.?|Ref(erence)?\.?( No\.?)?|Case No\.?|Invoice No\.?|الرقم|رقم الكتاب|رقم المرجع|المرجع|الإشارة)\s*[:：]\s*(?<ref>[^\s,،]*\d[^\s,،]*)", RegexOptions.IgnoreCase)]
    private static partial Regex RefLine();

    // ── Report ───────────────────────────────────────────────────────────────

    public static string Report(IReadOnlyList<Case> cases, string model)
    {
        bool Correct(Case c) => c.Expected is not null && c.Found == c.Expected && c.RightLetter;
        var linkedCases = cases.Where(c => c.Found is not null).ToList();
        var shouldLink = cases.Where(c => c.Expected is not null).ToList();
        var precision = linkedCases.Count == 0 ? 0 : linkedCases.Count(c => c.Expected is not null && c.RightLetter) * 100.0 / linkedCases.Count;
        var recall = shouldLink.Count == 0 ? 0 : shouldLink.Count(c => c.Found is not null && c.RightLetter) * 100.0 / shouldLink.Count;

        var md = new StringBuilder();
        md.AppendLine("# Related letters eval");
        md.AppendLine();
        md.AppendLine($"Engine: **{model}** · {cases.Count} labelled cases built from {cases.Select(c => c.Sample).Distinct().Count()} sample letters (EN + AR) · run {DateTime.UtcNow:yyyy-MM-dd}");
        md.AppendLine();
        md.AppendLine("Each case runs through the real upload → analysis → matching pipeline on a fresh database. Free, no AI beyond the analysis engine.");
        md.AppendLine();
        md.AppendLine("| | |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| **Precision**: links made that were right | **{precision:0}%** ({linkedCases.Count} links) |");
        md.AppendLine($"| **Recall**: letters that belong together and were linked | **{recall:0}%** ({shouldLink.Count} cases) |");
        md.AppendLine($"| Look-alikes linked (same reference, other sender; must be 0) | {cases.Count(c => c.Expected is null && c.Found is not null)} of {cases.Count(c => c.Expected is null)} |");
        md.AppendLine();
        md.AppendLine("| Case | Cases | Linked with the right reason | Linked, other reason | Missed | Wrong link |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var g in cases.GroupBy(c => c.Name))
        {
            var expectsLink = g.First().Expected is not null;
            md.AppendLine($"| {g.Key} | {g.Count()} " +
                          $"| {(expectsLink ? g.Count(Correct).ToString() : "–")} " +
                          $"| {(expectsLink ? g.Count(c => c.Found is not null && c.RightLetter && !Correct(c)).ToString() : "–")} " +
                          $"| {(expectsLink ? g.Count(c => c.Found is null).ToString() : "–")} " +
                          $"| {g.Count(c => c.Found is not null && (c.Expected is null || !c.RightLetter))} |");
        }
        md.AppendLine();
        md.AppendLine("The cases are built from the same samples with fixed wording, so these numbers show the matching works as designed, not real-world accuracy: real reminders are worded and laid out in more ways. A reminder is only built when the sample states a reference number. Email threads are matched on message headers alone (no analysis involved) and are covered by unit tests.");
        md.AppendLine();
        md.AppendLine("<details><summary>Per-case results</summary>");
        md.AppendLine();
        md.AppendLine("| Sample | Case | Expected | Found | Reason given |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var c in cases)
        {
            var ok = c.Expected is null ? c.Found is null : Correct(c);
            md.AppendLine($"| {c.Sample} | {c.Name} | {c.Expected?.ToString() ?? "no link"} | {(ok ? "✅" : "❌")} {c.Found?.ToString() ?? "no link"} | {c.Evidence?.Replace("|", "/") ?? "—"} |");
        }
        md.AppendLine();
        md.AppendLine("</details>");
        return md.ToString();
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private sealed class MemoryStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{Guid.NewGuid():N}/{fileName}";
            _files[path] = ms.ToArray();
            return path;
        }

        public Stream Open(string storagePath) => new MemoryStream(_files[storagePath]);
    }

    private sealed class NoQueue : IDocumentQueue
    {
        public ValueTask EnqueueAsync(Guid documentId, CancellationToken ct = default) => ValueTask.CompletedTask;
    }

    private sealed class NoNotifier : IInboxNotifier
    {
        public Task DocumentStatusChangedAsync(Guid documentId, DocumentStatus status, IReadOnlyCollection<int> departmentIds, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
