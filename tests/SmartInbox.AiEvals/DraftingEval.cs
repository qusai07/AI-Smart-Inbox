using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Persistence;

/// <summary>
/// Measures reply drafting on the labelled samples: does the template fit, how many gaps are left for a person,
/// and is every reference and date in the draft really in the letter. Free: the analysis engine is the caller's
/// choice (Rules by default) and drafting itself uses no AI.
/// </summary>
static class DraftingEval
{
    public record Row(
        string File, string Category, string ExpectedCategory, IReadOnlyList<string> Expected,
        string Chosen, string ChosenFromLabel, IReadOnlyList<string> Missing, IReadOnlyList<string> Unverified,
        double Milliseconds, string? Error);

    public static async Task<IReadOnlyList<Row>> RunAsync(
        IEnumerable<(string File, string ExpectedCategory, int DepartmentId, IReadOnlyList<string> Replies, string Text, AiAnalysisResult? Analysis, string? Error)> samples,
        DateOnly today)
    {
        var catalog = new ReplyTemplateCatalog();
        var drafter = new TemplateReplyDrafter(catalog, new DraftingOptions(), new FixedClock(today));
        var rows = new List<Row>();
        foreach (var s in samples)
        {
            if (s.Analysis is null)
            {
                rows.Add(new Row(s.File, "-", s.ExpectedCategory, s.Replies, "-", "-", [], [], 0, s.Error));
                continue;
            }
            var a = s.Analysis;
            var analysis = new DocumentAnalysis
            {
                Category = a.Category, Priority = a.Priority, Sender = a.Sender, Recipient = a.Recipient,
                ReferenceNumber = a.ReferenceNumber, DocumentDate = a.DocumentDate, Deadline = a.Deadline,
            };
            // Drafted as a clerk would: after routing to the right department.
            var department = DepartmentSeed.All.Single(d => d.Id == s.DepartmentId);
            var doc = new Document { FileName = s.File, ContentType = "text/plain", StoragePath = s.File };

            var watch = Stopwatch.StartNew();
            var draft = await drafter.DraftAsync(new DraftInput(doc, analysis, department, null, null), default);
            watch.Stop();

            var fromLabel = catalog.ForCategory(Enum.Parse<DocumentCategory>(s.ExpectedCategory)).Key;
            rows.Add(new Row(s.File, a.Category.ToString(), s.ExpectedCategory, s.Replies, draft.TemplateKey, fromLabel,
                draft.MissingInfo, Unverified(s.Text, analysis, draft), watch.Elapsed.TotalMilliseconds, null));
        }
        return rows;
    }

    /// <summary>References and dates the draft actually states that can't be found in the letter itself.</summary>
    private static List<string> Unverified(string text, DocumentAnalysis a, DraftResult draft)
    {
        var issues = new List<string>();
        var written = $"{draft.SubjectEn} {draft.BodyEn}";
        if (a.ReferenceNumber is { } r && written.Contains(r) && !text.Contains(r, StringComparison.OrdinalIgnoreCase)) issues.Add($"reference '{r}'");
        if (a.DocumentDate is { } d && written.Contains(LetterDates.En(d)) && !DateAppears(text, d)) issues.Add($"date {d:yyyy-MM-dd}");
        if (a.Deadline is { } dl && written.Contains(LetterDates.En(dl)) && !DateAppears(text, dl)) issues.Add($"deadline {dl:yyyy-MM-dd}");
        return issues;
    }

    private static readonly string[] ArabicMonths =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    private static bool DateAppears(string text, DateOnly d)
    {
        var c = CultureInfo.InvariantCulture;
        string[] forms =
        [
            d.ToString("d MMMM yyyy", c), d.ToString("dd MMMM yyyy", c), d.ToString("MMMM d, yyyy", c), d.ToString("d MMM yyyy", c),
            d.ToString("dd MMM yyyy", c), d.ToString("yyyy-MM-dd", c), d.ToString("dd/MM/yyyy", c), d.ToString("d/M/yyyy", c),
            $"{d.Day} {ArabicMonths[d.Month - 1]} {d.Year}",
        ];
        var flat = Regex.Replace(text, @"\s+", " ");
        return forms.Any(f => flat.Contains(f, StringComparison.OrdinalIgnoreCase))
               // Ranges like "14–15 April 2027" or "من 5 إلى 9 مايو 2027": the day appears before the month and year.
               || Regex.IsMatch(flat, $@"\b{d.Day}\b[^.\n]{{0,25}}({Regex.Escape(d.ToString("MMMM", c))}|{ArabicMonths[d.Month - 1]})\s+{d.Year}", RegexOptions.IgnoreCase);
    }

    public static string Report(IReadOnlyList<Row> rows, string engine, DateOnly today)
    {
        var ok = rows.Where(r => r.Error is null).ToList();
        double Pct(Func<Row, bool> f) => ok.Count == 0 ? 0 : ok.Count(f) * 100.0 / ok.Count;
        int Gaps(Row r) => r.Missing.Count;

        var md = new StringBuilder();
        md.AppendLine("# Drafting Evals");
        md.AppendLine();
        md.AppendLine($"Reply drafting with **templates only (no AI, no cost)**. Analysis engine: **{engine}** · {rows.Count} synthetic letters (EN + AR) · run {DateTime.UtcNow:yyyy-MM-dd} · \"today\" pinned to {today:yyyy-MM-dd}.");
        md.AppendLine();
        md.AppendLine("Each letter is analysed, routed to its correct department (as a clerk would), and drafted. `Expected` lists the templates a clerk could reasonably choose (a judgement recorded in `samples/labels.json`).");
        md.AppendLine();
        md.AppendLine("| Measure | Result |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| Right template, end to end (from the analysis's category) | **{Pct(r => r.Expected.Contains(r.Chosen)):0}%** |");
        md.AppendLine($"| Right template if the category were correct (template mapping alone) | {Pct(r => r.Expected.Contains(r.ChosenFromLabel)):0}% |");
        md.AppendLine($"| Complete (our number comes from the register) | {Pct(r => Gaps(r) == 0):0}% |");
        md.AppendLine($"| Average gaps for a person | {(ok.Count == 0 ? 0 : ok.Average(Gaps)):0.0} |");
        md.AppendLine($"| Every reference and date in the draft found in the letter | {Pct(r => r.Unverified.Count == 0):0}% |");
        md.AppendLine($"| Time per draft | {(ok.Count == 0 ? 0 : ok.Average(r => r.Milliseconds)):0.0} ms |");
        md.AppendLine();
        md.AppendLine("Why gaps remain:");
        md.AppendLine();
        foreach (var g in ok.SelectMany(r => r.Missing).GroupBy(m => m).OrderByDescending(g => g.Count()))
            md.AppendLine($"- {g.Key}: {g.Count()} letter(s)");
        md.AppendLine();
        md.AppendLine("<details><summary>Per-letter results</summary>");
        md.AppendLine();
        md.AppendLine("| Letter | Category (expected → got) | Template (expected → chosen) | Gaps | Not found in the letter |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var r in rows)
        {
            if (r.Error is not null)
            {
                md.AppendLine($"| {r.File} | ❌ error: {r.Error.Replace("|", "/")} | | | |");
                continue;
            }
            var gaps = r.Missing.ToList();
            md.AppendLine($"| {r.File} | {Mark(r.Category == r.ExpectedCategory)} {r.ExpectedCategory} → {r.Category} " +
                          $"| {Mark(r.Expected.Contains(r.Chosen))} {string.Join(" / ", r.Expected)} → {r.Chosen} " +
                          $"| {(gaps.Count == 0 ? "—" : string.Join(", ", gaps))} " +
                          $"| {(r.Unverified.Count == 0 ? "—" : "⚠️ " + string.Join(", ", r.Unverified))} |");
        }
        md.AppendLine();
        md.AppendLine("</details>");
        return md.ToString();
    }

    private static string Mark(bool ok) => ok ? "✅" : "❌";

    private sealed class FixedClock(DateOnly date) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
    }
}
