using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartInbox.Infrastructure.Ai.Rules;

/// <summary>Normalization and matching helpers for English and Arabic text.</summary>
public static class TextTools
{
    /// <summary>
    /// Lower-cases, folds Arabic letter variants (أإآ→ا, ة→ه, ى→ي), drops diacritics/tatweel and maps
    /// Arabic-Indic digits to ASCII so keyword and date matching work across spellings.
    /// </summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case >= 'ً' and <= 'ْ':
                case 'ـ':
                    continue;
                case 'أ' or 'إ' or 'آ':
                    sb.Append('ا');
                    break;
                case 'ة':
                    sb.Append('ه');
                    break;
                case 'ى':
                    sb.Append('ي');
                    break;
                case >= '٠' and <= '٩':
                    sb.Append((char)('0' + (ch - '٠')));
                    break;
                case >= '۰' and <= '۹':
                    sb.Append((char)('0' + (ch - '۰')));
                    break;
                case '’' or '‘':
                    sb.Append('\'');
                    break;
                case '–' or '—':
                    sb.Append('-');
                    break;
                default:
                    sb.Append(char.ToLowerInvariant(ch));
                    break;
            }
        }
        return sb.ToString();
    }

    public static bool IsArabic(char c) => c is >= '؀' and <= 'ۿ';

    public static string DetectLanguage(string text)
    {
        int arabic = 0, latin = 0;
        foreach (var c in text)
        {
            if (IsArabic(c)) arabic++;
            else if (char.IsAsciiLetter(c)) latin++;
        }
        if (arabic + latin == 0) return "en";
        var share = (double)arabic / (arabic + latin);
        return share > 0.7 ? "ar" : share < 0.3 ? "en" : "mixed";
    }

    /// <summary>
    /// Counts occurrences of an already-normalized term. Latin terms must start on a word boundary
    /// (so "complain" also matches "complaint"); short ones must match a whole word ("cv" ≠ "cve").
    /// Arabic terms match anywhere, because Arabic attaches prefixes such as و ب ل ال to words.
    /// </summary>
    public static int Count(string normalizedText, string normalizedTerm)
    {
        if (normalizedTerm.Length == 0) return 0;
        if (normalizedTerm.Any(IsArabic))
        {
            int count = 0, i = 0;
            while ((i = normalizedText.IndexOf(normalizedTerm, i, StringComparison.Ordinal)) >= 0)
            {
                count++;
                i += normalizedTerm.Length;
            }
            return count;
        }

        var tail = normalizedTerm.Length <= 4 ? "(?![a-z0-9])" : "";
        return Regex.Matches(normalizedText, $"(?<![a-z0-9]){Regex.Escape(normalizedTerm)}{tail}").Count;
    }

    public static bool Contains(string normalizedText, string normalizedTerm) => Count(normalizedText, normalizedTerm) > 0;
}

/// <summary>Finds English and Arabic dates in normalized text.</summary>
public static class DateFinder
{
    public record Found(DateOnly Date, int Index);

    private static readonly Dictionary<string, int> Months = new()
    {
        ["january"] = 1, ["jan"] = 1, ["february"] = 2, ["feb"] = 2, ["march"] = 3, ["mar"] = 3, ["april"] = 4, ["apr"] = 4,
        ["may"] = 5, ["june"] = 6, ["jun"] = 6, ["july"] = 7, ["jul"] = 7, ["august"] = 8, ["aug"] = 8,
        ["september"] = 9, ["sep"] = 9, ["sept"] = 9, ["october"] = 10, ["oct"] = 10, ["november"] = 11, ["nov"] = 11,
        ["december"] = 12, ["dec"] = 12,
        // Arabic month names, already normalized (أ→ا)
        ["يناير"] = 1, ["فبراير"] = 2, ["مارس"] = 3, ["ابريل"] = 4, ["مايو"] = 5, ["يونيو"] = 6, ["يوليو"] = 7,
        ["اغسطس"] = 8, ["سبتمبر"] = 9, ["اكتوبر"] = 10, ["نوفمبر"] = 11, ["ديسمبر"] = 12,
    };

    private static readonly string MonthPattern = string.Join("|", Months.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));

    private static readonly Regex Iso = new(@"\b(\d{4})-(\d{1,2})-(\d{1,2})\b");
    private static readonly Regex DayFirstNumeric = new(@"\b(\d{1,2})[/.](\d{1,2})[/.](\d{4})\b");
    private static readonly Regex DayMonthYear = new($@"\b(\d{{1,2}})(?:st|nd|rd|th)?\s+({MonthPattern})\.?,?\s+(\d{{4}})");
    private static readonly Regex MonthDayYear = new($@"\b({MonthPattern})\.?\s+(\d{{1,2}})(?:st|nd|rd|th)?,?\s+(\d{{4}})");

    public static List<Found> FindAll(string normalizedText)
    {
        var found = new List<Found>();
        foreach (Match m in Iso.Matches(normalizedText)) Add(found, m.Index, m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
        foreach (Match m in DayFirstNumeric.Matches(normalizedText)) Add(found, m.Index, m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value);
        foreach (Match m in DayMonthYear.Matches(normalizedText)) Add(found, m.Index, m.Groups[3].Value, Months[m.Groups[2].Value].ToString(), m.Groups[1].Value);
        foreach (Match m in MonthDayYear.Matches(normalizedText)) Add(found, m.Index, m.Groups[3].Value, Months[m.Groups[1].Value].ToString(), m.Groups[2].Value);
        return found.OrderBy(f => f.Index).ToList();
    }

    private static void Add(List<Found> list, int index, string y, string m, string d)
    {
        if (int.TryParse(y, CultureInfo.InvariantCulture, out var year) &&
            int.TryParse(m, CultureInfo.InvariantCulture, out var month) &&
            int.TryParse(d, CultureInfo.InvariantCulture, out var day) &&
            year is >= 1990 and <= 2100 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
        {
            list.Add(new Found(new DateOnly(year, month, day), index));
        }
    }
}
