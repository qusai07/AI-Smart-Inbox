using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SmartInbox.Domain;

namespace SmartInbox.Application;

/// <summary>
/// A reply letter in English and Arabic. Text uses three kinds of markers:
/// <c>{Slot}</c> is filled from the letter's analysis or the department (a missing value becomes [●]);
/// <c>{Slot?}</c> is optional (its line disappears when empty); <c>{?Label}</c> is a gap a person must fill.
/// </summary>
public record ReplyTemplate(
    string Key, string NameEn, string NameAr, IReadOnlyList<DocumentCategory> Categories,
    string SubjectEn, string SubjectAr, string BodyEn, string BodyAr, IReadOnlyList<string> Commitments);

public record ReplyTemplateDto(string Key, string NameEn, string NameAr, IReadOnlyList<DocumentCategory> Categories);

/// <summary>The templates shipped in the assembly (ReplyTemplates/*.json).</summary>
public class ReplyTemplateCatalog
{
    public const string Fallback = "general-reply";

    private readonly Dictionary<string, ReplyTemplate> _templates;

    public ReplyTemplateCatalog() : this(LoadEmbedded()) { }

    public ReplyTemplateCatalog(IEnumerable<ReplyTemplate> templates)
    {
        _templates = templates.ToDictionary(t => t.Key);
        if (!_templates.ContainsKey(Fallback)) throw new InvalidOperationException($"The '{Fallback}' reply template is missing.");
        foreach (var t in _templates.Values) TemplateRenderer.Validate(t);
    }

    public IReadOnlyCollection<ReplyTemplate> All => _templates.Values;

    public ReplyTemplate? Find(string? key) => key is not null && _templates.TryGetValue(key, out var t) ? t : null;

    /// <summary>The template for a document category, or the general reply.</summary>
    public ReplyTemplate ForCategory(DocumentCategory? category) =>
        _templates.Values.FirstOrDefault(t => category is { } c && t.Categories.Contains(c)) ?? _templates[Fallback];

    public IReadOnlyList<ReplyTemplateDto> List() =>
        _templates.Values.OrderBy(t => t.Key == Fallback).ThenBy(t => t.NameEn)
            .Select(t => new ReplyTemplateDto(t.Key, t.NameEn, t.NameAr, t.Categories)).ToList();

    private static IEnumerable<ReplyTemplate> LoadEmbedded()
    {
        var assembly = typeof(ReplyTemplateCatalog).Assembly;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("ReplyTemplates.", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            yield return JsonSerializer.Deserialize<ReplyTemplate>(stream, json)
                         ?? throw new InvalidOperationException($"Reply template {name} is empty.");
        }
    }
}

/// <summary>Slot values for one language. Null means unknown.</summary>
public sealed class SlotValues : Dictionary<string, string?>
{
    public static readonly IReadOnlyDictionary<string, string> MissingLabels = new Dictionary<string, string>
    {
        ["OurReference"] = "Our outgoing reference number",
        ["Today"] = "Date of the reply",
        ["SenderName"] = "Recipient (who the reply is addressed to)",
        ["TheirReference"] = "Their reference number",
        ["TheirDate"] = "Date of their letter",
        ["Deadline"] = "Deadline",
        ["DepartmentTeam"] = "Department",
        ["Department"] = "Department",
        ["Organisation"] = "Organisation name",
        ["SignatoryName"] = "Signatory name",
        ["SignatoryTitle"] = "Signatory title",
        ["Response"] = "The reply to what they asked",
    };

    public SlotValues() : base(StringComparer.Ordinal) { }
}

public record RenderedText(string Text, IReadOnlyList<string> Missing);

public static partial class TemplateRenderer
{
    // {Slot}  {Slot?}  {?Free label}
    [GeneratedRegex(@"\{(?<free>\?)?(?<name>[^{}?]+?)(?<optional>\?)?\}")]
    private static partial Regex Marker();

    public static RenderedText Render(string template, SlotValues values)
    {
        var missing = new List<string>();
        var lines = new List<string>();
        foreach (var line in template.Split('\n'))
        {
            var hadOptional = false;
            var rendered = Marker().Replace(line, m =>
            {
                var name = m.Groups["name"].Value.Trim();
                if (m.Groups["free"].Success)
                {
                    missing.Add(name);
                    return ReplyDraftRules.Placeholder;
                }
                var value = values.GetValueOrDefault(name);
                if (!string.IsNullOrWhiteSpace(value)) return value;
                if (m.Groups["optional"].Success)
                {
                    hadOptional = true;
                    return "";
                }
                missing.Add(SlotValues.MissingLabels.GetValueOrDefault(name, name));
                return ReplyDraftRules.Placeholder;
            });
            if (hadOptional && string.IsNullOrWhiteSpace(rendered)) continue; // an empty optional line disappears
            lines.Add(rendered);
        }
        return new RenderedText(string.Join('\n', lines), missing.Distinct().ToList());
    }

    /// <summary>Every {Slot} must be one the drafter knows how to fill; typos would silently become [●].</summary>
    public static void Validate(ReplyTemplate t)
    {
        foreach (var text in new[] { t.SubjectEn, t.SubjectAr, t.BodyEn, t.BodyAr })
        foreach (Match m in Marker().Matches(text))
        {
            if (m.Groups["free"].Success) continue;
            var name = m.Groups["name"].Value.Trim();
            if (!SlotValues.MissingLabels.ContainsKey(name))
                throw new InvalidOperationException($"Reply template '{t.Key}' uses unknown slot {{{name}}}.");
        }
    }
}

/// <summary>Dates as they appear on letters: "4 March 2027" and «4 مارس 2027»; today's date in Arabic also in Hijri.</summary>
public static class LetterDates
{
    private static readonly string[] ArabicMonths =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    private static readonly string[] HijriMonths =
        ["محرم", "صفر", "ربيع الأول", "ربيع الآخر", "جمادى الأولى", "جمادى الآخرة", "رجب", "شعبان", "رمضان", "شوال", "ذو القعدة", "ذو الحجة"];

    private static readonly UmAlQuraCalendar Hijri = new();

    public static string En(DateOnly date) => date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    public static string Ar(DateOnly date) => $"{date.Day} {ArabicMonths[date.Month - 1]} {date.Year}";

    /// <summary>«30 أكتوبر 2026م الموافق 18 جمادى الأولى 1448هـ»</summary>
    public static string ArWithHijri(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        var sb = new StringBuilder(Ar(date)).Append('م');
        if (dt >= Hijri.MinSupportedDateTime && dt <= Hijri.MaxSupportedDateTime)
            sb.Append($" الموافق {Hijri.GetDayOfMonth(dt)} {HijriMonths[Hijri.GetMonth(dt) - 1]} {Hijri.GetYear(dt)}هـ");
        return sb.ToString();
    }
}
