using System.Text.RegularExpressions;
using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.Infrastructure.Ai.Rules;

/// <summary>
/// Free, offline, deterministic analysis: keyword scoring for department and category, regex extraction
/// for dates, references and parties, and simple priority rules. No model, no API key, no cost.
/// Its confidence is honest: unclear documents score low and go to human review (or to an LLM, via the gate).
/// </summary>
public class RuleBasedProvider(TimeProvider clock) : IAiProvider
{
    public const string EngineName = "rules-v1";

    private const int NameWeight = 4;
    private const int AddressedToBonus = 4;
    private const int MaxHitsPerTerm = 3;

    public Task<AiAnalysisResult> AnalyzeAsync(AnalysisInput input, IReadOnlyList<Department> departments, CancellationToken ct = default) =>
        Task.FromResult(Analyze(input, departments));

    public AiAnalysisResult Analyze(AnalysisInput input, IReadOnlyList<Department> departments)
    {
        if (string.IsNullOrWhiteSpace(input.Text)) return Unreadable();

        var text = input.Text.Replace("\r", "");
        var norm = TextTools.Normalize(text);
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var language = TextTools.DetectLanguage(text);

        var subject = FieldValue(lines, "subject", "re", "الموضوع");
        var recipient = FieldValue(lines, "to", "الي", "الساده", "معالي", "سعاده", "السيد", "السيده");
        var sender = FindSender(lines);
        var reference = FindReference(text);

        var dates = DateFinder.FindAll(norm);
        var documentDate = FindDocumentDate(lines, dates);
        var deadline = FindDeadline(norm, dates, documentDate);

        var routing = ScoreDepartments(norm, recipient, departments);
        var category = ScoreCategory(norm);
        var priority = DecidePriority(norm, category, deadline, today);
        var actions = FindActionItems(text, deadline);
        var (summaryEn, summaryAr) = Summaries(text, lines, language, category, sender, subject);

        return new AiAnalysisResult(
            summaryEn, summaryAr, category, priority, language, sender, recipient, reference,
            documentDate, deadline, actions, routing.DepartmentId, routing.Confidence, routing.Reasoning,
            EngineName, 0);
    }

    // ── Routing ────────────────────────────────────────────────────────────

    private record Routing(int DepartmentId, double Confidence, string Reasoning);

    private static Routing ScoreDepartments(string norm, string? recipient, IReadOnlyList<Department> departments)
    {
        var normRecipient = recipient is null ? "" : TextTools.Normalize(recipient);
        var scored = departments.Select(d =>
        {
            var score = 0;
            var hits = new List<string>();

            foreach (var name in new[] { d.NameEn, d.NameAr }.Select(TextTools.Normalize))
            {
                if (!TextTools.Contains(norm, name)) continue;
                score += NameWeight;
                hits.Add($"\"{name}\"");
                if (TextTools.Contains(normRecipient, name)) score += AddressedToBonus;
            }

            foreach (var keyword in (d.Keywords ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var terms = new[] { keyword }.Concat(Lexicon.KeywordExpansions.GetValueOrDefault(keyword) ?? []);
                var keywordHits = terms.Sum(t => Math.Min(MaxHitsPerTerm, TextTools.Count(norm, TextTools.Normalize(t))));
                if (keywordHits == 0) continue;
                score += keywordHits;
                hits.Add(keyword);
            }

            return (Department: d, Score: score, Hits: hits);
        })
        .OrderByDescending(x => x.Score)
        .ToList();

        var top = scored.FirstOrDefault();
        if (top.Department is null || top.Score == 0)
            return new Routing(0, 0, "No department keywords were found in the document.");

        var second = scored.Count > 1 ? scored[1] : default;
        var margin = (double)(top.Score - second.Score) / top.Score;    // how clearly it wins
        var strength = Math.Min(1.0, top.Score / 8.0);                     // how much evidence there is
        var confidence = Math.Round(0.25 + 0.45 * margin + 0.3 * strength, 2);

        var reasoning = $"Matched {string.Join(", ", top.Hits.Take(6))} → {top.Department.NameEn} (score {top.Score}).";
        if (second.Department is not null && second.Score > 0 && margin < 0.35)
            reasoning += $" Also close: {second.Department.NameEn} (score {second.Score}).";

        return new Routing(top.Department.Id, Math.Clamp(confidence, 0, 0.98), reasoning);
    }

    private static DocumentCategory ScoreCategory(string norm)
    {
        var best = DocumentCategory.Other;
        var bestScore = 0;
        foreach (var (category, terms) in Lexicon.CategoryTerms)
        {
            var score = terms.Sum(t => Math.Min(2, TextTools.Count(norm, t.Term)) * t.Weight);
            if (score > bestScore)
            {
                best = category;
                bestScore = score;
            }
        }
        return best;
    }

    private static Priority DecidePriority(string norm, DocumentCategory category, DateOnly? deadline, DateOnly today)
    {
        if (Lexicon.UrgentTerms.Any(t => TextTools.Contains(norm, t))) return Priority.Urgent;

        if (deadline is { } d)
        {
            var days = d.DayNumber - today.DayNumber;
            if (days <= 3) return Priority.Urgent;
            if (days <= 14) return Priority.High;
        }

        if (category == DocumentCategory.Legal || Lexicon.EscalationTerms.Any(t => TextTools.Contains(norm, t))) return Priority.High;
        if (category == DocumentCategory.Other && Lexicon.LowPriorityTerms.Any(t => TextTools.Contains(norm, t))) return Priority.Low;
        return Priority.Normal;
    }

    // ── Extraction ─────────────────────────────────────────────────────────

    /// <summary>Value of a "Label: value" line, e.g. "Subject: …" or "الموضوع: …".</summary>
    private static string? FieldValue(List<string> lines, params string[] labels)
    {
        foreach (var line in lines)
        {
            var norm = TextTools.Normalize(line);
            foreach (var label in labels)
            {
                if (!norm.StartsWith(label, StringComparison.Ordinal)) continue;
                // The label must be followed by ":" or "/" so ordinary sentences ("من خلال…", "To be held…") don't match.
                var after = norm[label.Length..];
                var trimmed = after.TrimStart();
                if (trimmed.Length == 0 || trimmed[0] is not (':' or '/')) continue;
                var start = label.Length + (after.Length - trimmed.Length) + 1;
                if (start >= line.Length) continue;
                var rest = Clean(line[start..]);
                if (rest.Length > 1) return rest;
            }
        }
        return null;
    }

    private static readonly string[] HeaderLabels =
        ["date", "to", "from", "subject", "ref", "dear", "التاريخ", "الي", "من", "الموضوع", "الرقم", "الساده", "معالي", "سعاده", "السيد", "السيده"];

    private static string? FindSender(List<string> lines)
    {
        if (FieldValue(lines, "from", "من") is { } from)
            return Clean(Regex.Replace(from, @"<[^>]*>", "")) is { Length: > 0 } name ? name : from;

        // Letterhead: an organization name on the first line.
        var first = lines.FirstOrDefault();
        if (first is not null && first.Length <= 80 && !HeaderLabels.Any(l => TextTools.Normalize(first).StartsWith(l, StringComparison.Ordinal)))
            return Clean(first);

        // Otherwise the signature: the first line of the last few that is a name, not a closing or a number.
        return lines.TakeLast(3)
            .Where(l => l.Length <= 60 && !l.Any(char.IsDigit))
            .Where(l => !Lexicon.ClosingLines.Any(c => TextTools.Normalize(l).StartsWith(c, StringComparison.Ordinal)))
            .Select(Clean)
            .FirstOrDefault();
    }

    private static readonly Regex ReferencePattern = new(
        @"(?:\bref(?:erence)?\.?(?:\s*no\.?)?|\binvoice\s+no\.?|\bcase\s+no\.?|\bapplication\s+number|\bticket\s+reference|\bcircular\s+no\.?|الرقم|رقم\s+الفاتورة|رقم\s+الطلب)\s*[:#]?\s*(\(?[A-Za-z0-9؀-ۿ][^\s,،)]*)",
        RegexOptions.IgnoreCase);

    private static string? FindReference(string text)
    {
        var m = ReferencePattern.Match(text);
        if (!m.Success) return null;
        var value = m.Groups[1].Value.Trim('(', ')', '.', ':');
        return value.Any(char.IsDigit) ? value : null;
    }

    private static readonly string[] DateLabels = ["date", "dated", "invoice date", "التاريخ"];

    private static DateOnly? FindDocumentDate(List<string> lines, List<DateFinder.Found> dates)
    {
        foreach (var line in lines)
        {
            var norm = TextTools.Normalize(line);
            if (DateLabels.Any(l => norm.StartsWith(l, StringComparison.Ordinal)) && DateFinder.FindAll(norm).FirstOrDefault() is { } d)
                return d.Date;
        }
        return dates.FirstOrDefault()?.Date;
    }

    private static readonly Regex WithinDays = new(@"within\s+(\d{1,3})\s+days|خلال\s+(\d{1,3})\s+(?:يوما|يوم|ايام)");
    private static readonly Regex WithinHours = new(@"within\s+(\d{1,3})\s+hours|خلال\s+(\d{1,3})\s+ساعه");

    private static DateOnly? FindDeadline(string norm, List<DateFinder.Found> dates, DateOnly? documentDate)
    {
        var candidates = dates
            .Where(d => Lexicon.DeadlineCues.Any(c => norm.Substring(Math.Max(0, d.Index - 40), Math.Min(40, d.Index)).Contains(c)))
            .Select(d => d.Date)
            .ToList();

        if (documentDate is { } baseDate)
        {
            if (WithinDays.Match(norm) is { Success: true } days)
                candidates.Add(baseDate.AddDays(int.Parse(days.Groups[1].Success ? days.Groups[1].Value : days.Groups[2].Value)));
            if (WithinHours.Match(norm) is { Success: true } hours)
                candidates.Add(baseDate.AddDays((int)Math.Ceiling(int.Parse(hours.Groups[1].Success ? hours.Groups[1].Value : hours.Groups[2].Value) / 24.0)));
        }

        var valid = candidates.Where(d => documentDate is null || d >= documentDate).ToList();
        return valid.Count == 0 ? null : valid.Min();
    }

    private static readonly Regex NumberedLine = new(@"^\s*\d{1,2}[.)]\s+(.{8,})$", RegexOptions.Multiline);
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?؟])\s+|\n+");

    private static List<AiActionItem> FindActionItems(string text, DateOnly? deadline)
    {
        var numbered = NumberedLine.Matches(text).Select(m => m.Groups[1].Value.Trim()).Take(5).ToList();
        if (numbered.Count > 0) return numbered.Select(n => new AiActionItem(Truncate(n, 180), null)).ToList();

        return SentenceSplit.Split(text)
            .Select(s => s.Trim())
            .Where(s => s.Length is > 20 and < 400)
            .Where(s => Lexicon.ActionCues.Any(c => TextTools.Contains(TextTools.Normalize(s), c)))
            .Take(2)
            .Select(s => new AiActionItem(Truncate(s, 180), deadline))
            .ToList();
    }

    // ── Summaries ──────────────────────────────────────────────────────────

    private static readonly string[] Salutations =
        ["dear", "your excellency", "to:", "from:", "date:", "ref", "subject", "تحيه طيبه", "الساده", "المحترم", "معالي", "سعاده", "التاريخ", "الرقم", "الموضوع", "الي:", "من:"];

    /// <summary>
    /// Extractive summary in the document's own language plus a templated one in the other language.
    /// The rule engine cannot translate, so the templated summary says what the document is and who sent it.
    /// </summary>
    private static (string En, string Ar) Summaries(string text, List<string> lines, string language, DocumentCategory category, string? sender, string? subject)
    {
        var body = string.Join(" ", lines
            .Where(l => l.Length > 40)
            .Where(l => !Salutations.Any(s => TextTools.Normalize(l).StartsWith(s, StringComparison.Ordinal))));
        var sentences = SentenceSplit.Split(body).Where(s => s.Trim().Length > 25).Take(2).Select(s => s.Trim());
        var extract = Truncate(string.Join(" ", sentences), 360);
        if (extract.Length == 0) extract = Truncate(text.Trim(), 240);

        var who = sender ?? (language == "ar" ? "جهة غير محددة" : "an unidentified sender");
        var templatedEn = $"{category} from {who}{(subject is null ? "" : $" regarding: {subject}")}.";
        var templatedAr = $"{Lexicon.CategoryAr[category]} من {who}{(subject is null ? "" : $" بشأن: {subject}")}.";

        return language == "ar"
            ? (templatedEn, extract)
            : (Lead(subject, extract), templatedAr);
    }

    private static string Lead(string? subject, string extract) =>
        subject is null || extract.Contains(subject, StringComparison.OrdinalIgnoreCase) ? extract : $"{subject}. {extract}";

    private static AiAnalysisResult Unreadable() => new(
        "Scanned document or image with no text layer. The rule engine cannot read it; please review it manually or enable an AI model with vision.",
        "مستند ممسوح ضوئيًا أو صورة بدون نص. لا يمكن لمحرك القواعد قراءته؛ يرجى مراجعته يدويًا أو تفعيل نموذج ذكاء اصطناعي يدعم الرؤية.",
        DocumentCategory.Other, Priority.Normal, "en", null, null, null, null, null, [], 0, 0,
        "No text to analyze.", EngineName, 0);

    private static string Clean(string s) => s.Trim().Trim('"', '\'', ',', '،', ':').Trim();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}
