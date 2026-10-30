using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartInbox.Domain;

namespace SmartInbox.Application;

public class ThreadOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>How far back to look for related letters.</summary>
    public int LookbackDays { get; set; } = 365;
    /// <summary>Shorter texts (a one-line email) are too generic to call two letters the same by text alone.</summary>
    public int MinTextForDuplicate { get; set; } = 200;
}

/// <summary>Normalised keys for matching letters: hashes, reference numbers, senders, our outgoing numbers.</summary>
public static partial class LetterKeys
{
    /// <summary>Webmail domains say nothing about who sent a letter.</summary>
    private static readonly HashSet<string> PublicDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "hotmail.com", "outlook.com", "live.com", "msn.com", "yahoo.com", "icloud.com", "me.com",
        "aol.com", "proton.me", "protonmail.com", "gmx.com", "mail.com", "yandex.com",
    };

    private static readonly HashSet<string> NameNoise = new(StringComparer.Ordinal) { "the", "of", "and", "for", "&", "و" };

    public static async Task<string> HashAsync(Stream content, CancellationToken ct) =>
        Convert.ToHexString(await SHA256.HashDataAsync(content, ct)).ToLowerInvariant();

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>The text with whitespace and digits normalised; null when it's too short to identify a letter.</summary>
    public static string? TextHash(string? text, int minLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = Spaces().Replace(AsciiDigits(text), " ").Trim().ToLowerInvariant();
        return normalized.Length < minLength ? null : Hash(normalized);
    }

    /// <summary>"Ref. No. ٢٠٢٧ / ١١٨" → "2027/118". Null when there's nothing specific enough to link on.</summary>
    public static string? Reference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var s = AsciiDigits(reference).Trim().ToUpperInvariant();
        string previous;
        do // "Ref. No. 2027/118" → "2027/118"
        {
            previous = s;
            s = RefLabel().Replace(s, "").TrimStart();
        } while (s != previous);
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '/') sb.Append('/'); // any separator (space, -, ., \) is one /
        }
        var key = sb.ToString().Trim('/');
        return key.Length >= 3 && key.Any(char.IsAsciiDigit) ? key[..Math.Min(key.Length, 100)] : null;
    }

    /// <summary>A sender's name for comparing: lower case, Arabic letter forms unified, "the"/"ال" and punctuation dropped.</summary>
    public static string[] SenderName(string? sender)
    {
        if (string.IsNullOrWhiteSpace(sender)) return [];
        var s = EmailAddress().Replace(sender, " ").ToLowerInvariant();
        s = ArabicMarks().Replace(s, "");
        s = s.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ة', 'ه').Replace('ى', 'ي');
        return NotWord().Split(s)
            .Select(w => w.Length > 4 && w.StartsWith("ال", StringComparison.Ordinal) ? w[2..] : w)
            .Where(w => w.Length > 0 && !NameNoise.Contains(w))
            .ToArray();
    }

    /// <summary>The organisation's email domain, or null for webmail and no address.</summary>
    public static string? Domain(string? address)
    {
        var m = address is null ? null : EmailAddress().Match(address);
        if (m is not { Success: true }) return null;
        var domain = m.Value[(m.Value.IndexOf('@') + 1)..].ToLowerInvariant();
        return PublicDomains.Contains(domain) ? null : domain;
    }

    /// <summary>
    /// The same sender: the same organisation email domain, or the same name (one name may be the start of a fuller
    /// one with at least two words, e.g. "Demo City Court" and "Demo City Court of First Instance").
    /// </summary>
    public static bool SameSender((string[] Name, string? Domain) a, (string[] Name, string? Domain) b)
    {
        if (a.Domain is not null && a.Domain == b.Domain) return true;
        if (a.Name.Length == 0 || b.Name.Length == 0) return false;
        var (shorter, longer) = a.Name.Length <= b.Name.Length ? (a.Name, b.Name) : (b.Name, a.Name);
        if (shorter.Length == longer.Length) return shorter.SequenceEqual(longer);
        return shorter.Length >= 2 && longer.Take(shorter.Length).SequenceEqual(shorter);
    }

    /// <summary>A pattern for our own outgoing numbers, built from Register:Format.</summary>
    public static Regex OurNumbers(RegisterOptions register)
    {
        var sb = new StringBuilder(@"(?<![A-Za-z0-9])");
        var last = 0;
        foreach (Match m in FormatToken().Matches(register.Format))
        {
            sb.Append(Regex.Escape(register.Format[last..m.Index]));
            sb.Append(m.Groups[1].Value switch
            {
                "Prefix" => Regex.Escape(register.Prefix),
                "Year" => @"\d{4}",
                "Dept" => "[A-Za-z0-9]{1,10}",
                _ => @"\d{1,9}",
            });
            last = m.Index + m.Length;
        }
        sb.Append(Regex.Escape(register.Format[last..])).Append("(?![0-9])");
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    /// <summary>Arabic-Indic and Persian digits as 0-9.</summary>
    public static string AsciiDigits(string s)
    {
        if (!s.Any(c => c is >= '٠' and <= '٩' or >= '۰' and <= '۹')) return s;
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '٠' and <= '٩') chars[i] = (char)('0' + chars[i] - '٠');
            else if (chars[i] is >= '۰' and <= '۹') chars[i] = (char)('0' + chars[i] - '۰');
        }
        return new string(chars);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(OUR\s+REF(ERENCE)?|YOUR\s+REF(ERENCE)?|REF(ERENCE)?|NO|NUMBER|NUM|رقم|الرقم|المرجع|مرجع|الإشارة)\b[\s.:#\-]*")]
    private static partial Regex RefLabel();

    [GeneratedRegex(@"[\w.+\-]+@[\w\-]+(\.[\w\-]+)+")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"[ً-ٰٟـ]")]
    private static partial Regex ArabicMarks();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord();

    [GeneratedRegex(@"\{(Prefix|Year|Dept|Seq)(?::0+)?\}")]
    private static partial Regex FormatToken();
}

/// <summary>
/// Finds the letters a document belongs with: the same letter received again, a follow-up in the same case, a reply
/// to one of our letters, the next email in a conversation. Exact, explainable signals only; no AI.
/// </summary>
public class RelatedLetterFinder(IAppDbContext db, ThreadOptions options, RegisterOptions register, TimeProvider clock)
{
    public const int MaxPerKind = 5;
    private const int MaxCandidates = 50;

    private record Found(Guid? OtherId, DateTime? OtherUploadedAt, LinkKind Kind, string Evidence, Guid? OutgoingLetterId = null);

    /// <summary>
    /// Refreshes the document's match keys and adds links to related letters it isn't linked to yet (a removed link is
    /// never added again). The caller saves. Returns the new links.
    /// </summary>
    public async Task<IReadOnlyList<DocumentLink>> LinkAsync(Guid documentId, CancellationToken ct)
    {
        if (!options.Enabled) return [];
        var doc = await db.Documents.Include(d => d.Analysis).FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return [];
        var email = doc.InboundEmailId is { } emailId ? await db.InboundEmails.AsNoTracking().FirstOrDefaultAsync(e => e.Id == emailId, ct) : null;

        doc.TextHash = LetterKeys.TextHash(doc.ExtractedText, options.MinTextForDuplicate);
        doc.ReferenceKey = LetterKeys.Reference(doc.Analysis?.ReferenceNumber);

        var since = clock.GetUtcNow().UtcDateTime.AddDays(-options.LookbackDays);
        var others = db.Documents.AsNoTracking().Where(d => d.Id != doc.Id && d.UploadedAt >= since);

        var found = new List<Found>();
        found.AddRange(await DuplicatesAsync(doc, others, ct));
        found.AddRange(await FollowUpsAsync(doc, email, others, ct));
        found.AddRange(await RepliesToOurLettersAsync(doc, email, ct));
        found.AddRange(await EmailThreadAsync(doc, email, ct));

        // One link per pair, the strongest reason first (LinkKind is ordered that way).
        var best = found
            .GroupBy(f => (f.OtherId, Outgoing: f.OtherId is null ? f.OutgoingLetterId : null))
            .Select(g => g.OrderBy(f => f.Kind).First())
            .ToList();
        if (best.Count == 0) return [];

        var existing = await db.DocumentLinks.AsNoTracking()
            .Where(l => l.DocumentId == doc.Id || l.RelatedDocumentId == doc.Id)
            .Select(l => new { l.DocumentId, l.RelatedDocumentId, l.OutgoingLetterId })
            .ToListAsync(ct);

        var added = new List<DocumentLink>();
        foreach (var f in best)
        {
            if (f.OtherId is { } other)
            {
                if (existing.Any(l => (l.DocumentId == doc.Id && l.RelatedDocumentId == other) || (l.DocumentId == other && l.RelatedDocumentId == doc.Id)))
                    continue;
                // Newer → earlier, so a pair is stored the same way whichever letter is analysed (again) first.
                var docIsNewer = doc.UploadedAt > f.OtherUploadedAt || (doc.UploadedAt == f.OtherUploadedAt && doc.Id.CompareTo(other) > 0);
                added.Add(new DocumentLink
                {
                    DocumentId = docIsNewer ? doc.Id : other, RelatedDocumentId = docIsNewer ? other : doc.Id,
                    Kind = f.Kind, Evidence = Limit(f.Evidence), OutgoingLetterId = f.OutgoingLetterId,
                });
            }
            else
            {
                if (existing.Any(l => l.DocumentId == doc.Id && l.RelatedDocumentId == null && l.OutgoingLetterId == f.OutgoingLetterId)) continue;
                added.Add(new DocumentLink { DocumentId = doc.Id, Kind = f.Kind, Evidence = Limit(f.Evidence), OutgoingLetterId = f.OutgoingLetterId });
            }
        }
        db.DocumentLinks.AddRange(added);
        return added;
    }

    /// <summary>
    /// The department that already has this case: where the most recent related letter was routed, or the department
    /// that sent the letter of ours it replies to. <paramref name="pending"/> are links added but not saved yet.
    /// </summary>
    public async Task<int?> CaseDepartmentAsync(Guid documentId, IReadOnlyList<DocumentLink> pending, CancellationToken ct)
    {
        var links = await db.DocumentLinks.AsNoTracking()
            .Where(l => l.RemovedAt == null && (l.DocumentId == documentId || l.RelatedDocumentId == documentId))
            .ToListAsync(ct);
        links.AddRange(pending);
        if (links.Count == 0) return null;

        var others = links.Select(l => l.DocumentId == documentId ? l.RelatedDocumentId : l.DocumentId).OfType<Guid>().Distinct().ToList();
        var routed = await db.Documents.AsNoTracking()
            .Where(d => others.Contains(d.Id) && d.AssignedDepartmentId != null)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => d.AssignedDepartmentId)
            .FirstOrDefaultAsync(ct);
        if (routed is not null) return routed;

        var ours = links.Where(l => l.OutgoingLetterId != null).Select(l => l.OutgoingLetterId!.Value).Distinct().ToList();
        return ours.Count == 0 ? null : await db.OutgoingLetters.AsNoTracking()
            .Where(l => ours.Contains(l.Id))
            .OrderByDescending(l => l.IssuedAt)
            .Select(l => (int?)l.DepartmentId)
            .FirstOrDefaultAsync(ct);
    }

    private static async Task<IEnumerable<Found>> DuplicatesAsync(Document doc, IQueryable<Document> others, CancellationToken ct)
    {
        var (file, text) = (doc.ContentHash, doc.TextHash);
        if (file is null && text is null) return [];
        var matches = await others
            .Where(d => (file != null && d.ContentHash == file) || (text != null && d.TextHash == text))
            .OrderBy(d => d.UploadedAt)
            .Select(d => new { d.Id, d.UploadedAt, SameFile = file != null && d.ContentHash == file })
            .Take(MaxPerKind)
            .ToListAsync(ct);
        return matches.Select(m => new Found(m.Id, m.UploadedAt, LinkKind.Duplicate, m.SameFile ? "Same file" : "Same text"));
    }

    private async Task<IEnumerable<Found>> FollowUpsAsync(Document doc, InboundEmail? email, IQueryable<Document> others, CancellationToken ct)
    {
        if (doc.ReferenceKey is not { } reference) return [];
        var candidates = await others
            .Where(d => d.ReferenceKey == reference)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new { d.Id, d.UploadedAt, Sender = d.Analysis != null ? d.Analysis.Sender : null, d.InboundEmailId })
            .Take(MaxCandidates)
            .ToListAsync(ct);
        if (candidates.Count == 0) return [];

        var emailIds = candidates.Where(c => c.InboundEmailId != null).Select(c => c.InboundEmailId!.Value).Distinct().ToList();
        var fromAddresses = await db.InboundEmails.AsNoTracking().Where(e => emailIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FromAddress, ct);

        var me = SenderOf(doc.Analysis?.Sender, email?.FromAddress);
        var shownSender = doc.Analysis?.Sender ?? email?.FromAddress;
        return candidates
            .Where(c => LetterKeys.SameSender(me, SenderOf(c.Sender, c.InboundEmailId is { } e ? fromAddresses.GetValueOrDefault(e) : null)))
            .Take(MaxPerKind)
            .Select(c => new Found(c.Id, c.UploadedAt, LinkKind.FollowUp, $"Same reference {doc.Analysis!.ReferenceNumber} from {shownSender}"));
    }

    private async Task<IEnumerable<Found>> RepliesToOurLettersAsync(Document doc, InboundEmail? email, CancellationToken ct)
    {
        var text = string.Join('\n', doc.ExtractedText, email?.Subject, email?.BodyText);
        if (string.IsNullOrWhiteSpace(text)) return [];
        var numbers = LetterKeys.OurNumbers(register).Matches(LetterKeys.AsciiDigits(text))
            .Select(m => m.Value.ToUpperInvariant()).Distinct().Take(10).ToList();
        if (numbers.Count == 0) return [];

        var letters = await db.OutgoingLetters.AsNoTracking().Where(l => numbers.Contains(l.Number)).Take(MaxPerKind).ToListAsync(ct);
        var answered = letters.Where(l => l.DocumentId is { } d && d != doc.Id).Select(l => l.DocumentId!.Value).Distinct().ToList();
        var uploaded = await db.Documents.AsNoTracking().Where(d => answered.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.UploadedAt, ct);
        return letters.Select(l => l.DocumentId is { } d && uploaded.TryGetValue(d, out var at)
            ? new Found(d, at, LinkKind.RepliesToOurLetter, $"Quotes our letter {l.Number}", l.Id)
            : new Found(null, null, LinkKind.RepliesToOurLetter, $"Quotes our letter {l.Number}", l.Id));
    }

    private async Task<IEnumerable<Found>> EmailThreadAsync(Document doc, InboundEmail? email, CancellationToken ct)
    {
        if (email is null) return [];
        var ids = $"{email.InReplyTo} {email.References}".Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().Take(25).ToList();
        if (ids.Count == 0) return [];

        var earlier = await db.InboundEmails.AsNoTracking()
            .Where(e => ids.Contains(e.MessageId) && e.Id != email.Id && e.DocumentIds != null)
            .Select(e => new { e.Subject, e.DocumentIds })
            .ToListAsync(ct);
        var docIds = earlier
            .SelectMany(e => (JsonSerializer.Deserialize<List<Guid>>(e.DocumentIds!) ?? []).Select(id => (Id: id, e.Subject)))
            .Where(x => x.Id != doc.Id)
            .DistinctBy(x => x.Id)
            .Take(MaxPerKind)
            .ToList();
        var lookup = docIds.Select(x => x.Id).ToList();
        var uploaded = await db.Documents.AsNoTracking().Where(d => lookup.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.UploadedAt, ct);
        return docIds.Where(x => uploaded.ContainsKey(x.Id))
            .Select(x => new Found(x.Id, uploaded[x.Id], LinkKind.EmailThread, $"Reply to the email \"{x.Subject ?? "(no subject)"}\""));
    }

    private static (string[] Name, string? Domain) SenderOf(string? sender, string? fromAddress) =>
        (LetterKeys.SenderName(sender), LetterKeys.Domain(fromAddress) ?? LetterKeys.Domain(sender));

    private static string Limit(string s) => s.Length <= 300 ? s : s[..300];
}

/// <summary>A letter related to the one being viewed, and why.</summary>
public record RelatedLetterDto(
    long LinkId, LinkKind Kind, string? Evidence, bool Earlier, DateTime LinkedAt,
    Guid? DocumentId, string? FileName, DateTime? UploadedAt, DocumentStatus? Status, int? DepartmentId, HandlingStatus? Handling, string? Sender,
    Guid? OutgoingLetterId, string? OutgoingNumber);

/// <summary>Related letters for people: only letters they may see; a wrong link can be removed (and stays removed).</summary>
public class RelatedLetterService(IAppDbContext db, ICurrentUser user)
{
    /// <summary>Null when the document doesn't exist or the user can't see it.</summary>
    public async Task<IReadOnlyList<RelatedLetterDto>?> ListAsync(Guid documentId, CancellationToken ct)
    {
        if (!await db.Documents.VisibleTo(user).AnyAsync(d => d.Id == documentId, ct)) return null;
        var links = await db.DocumentLinks.AsNoTracking()
            .Where(l => l.RemovedAt == null && (l.DocumentId == documentId || l.RelatedDocumentId == documentId))
            .ToListAsync(ct);
        if (links.Count == 0) return [];

        var ids = links.Select(l => l.DocumentId == documentId ? l.RelatedDocumentId : l.DocumentId).OfType<Guid>().Distinct().ToList();
        var docs = await db.Documents.AsNoTracking().VisibleTo(user)
            .Where(d => ids.Contains(d.Id))
            .Select(d => new { d.Id, d.FileName, d.UploadedAt, d.Status, d.AssignedDepartmentId, d.Handling, Sender = d.Analysis != null ? d.Analysis.Sender : null })
            .ToDictionaryAsync(d => d.Id, ct);
        var outgoingIds = links.Where(l => l.OutgoingLetterId != null).Select(l => l.OutgoingLetterId!.Value).Distinct().ToList();
        var outgoing = await db.OutgoingLetters.AsNoTracking()
            .Where(l => outgoingIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => new { l.Number, l.DepartmentId }, ct);

        var result = new List<RelatedLetterDto>();
        foreach (var l in links)
        {
            var otherId = l.DocumentId == documentId ? l.RelatedDocumentId : l.DocumentId;
            var ours = l.OutgoingLetterId is { } o && outgoing.TryGetValue(o, out var letter)
                       && (user.RestrictedToDepartment is not { } own || own == letter.DepartmentId) ? letter : null;
            if (otherId is { } id)
            {
                if (!docs.TryGetValue(id, out var d)) continue; // a letter in another department: not shown
                result.Add(new RelatedLetterDto(l.Id, l.Kind, l.Evidence, l.DocumentId == documentId, l.CreatedAt,
                    d.Id, d.FileName, d.UploadedAt, d.Status, d.AssignedDepartmentId, d.Handling, d.Sender, ours is null ? null : l.OutgoingLetterId, ours?.Number));
            }
            else if (ours is not null)
            {
                result.Add(new RelatedLetterDto(l.Id, l.Kind, l.Evidence, true, l.CreatedAt,
                    null, null, null, null, ours.DepartmentId, null, null, l.OutgoingLetterId, ours.Number));
            }
        }
        return result.OrderBy(r => r.Kind).ThenByDescending(r => r.UploadedAt ?? r.LinkedAt).ToList();
    }

    public enum RemoveOutcome { Removed, NotFound }

    public async Task<RemoveOutcome> RemoveAsync(Guid documentId, long linkId, CancellationToken ct)
    {
        if (!await db.Documents.VisibleTo(user).AnyAsync(d => d.Id == documentId, ct)) return RemoveOutcome.NotFound;
        var link = await db.DocumentLinks.FirstOrDefaultAsync(
            l => l.Id == linkId && l.RemovedAt == null && (l.DocumentId == documentId || l.RelatedDocumentId == documentId), ct);
        if (link is null) return RemoveOutcome.NotFound;
        link.RemovedAt = DateTime.UtcNow;
        link.RemovedBy = user.Name;
        db.DocumentEvents.Add(new DocumentEvent { DocumentId = documentId, Type = DocumentEventType.Linked, Actor = user.Name, Note = $"Link removed: {link.Evidence}" });
        await db.SaveChangesAsync(ct);
        return RemoveOutcome.Removed;
    }
}

/// <summary>
/// Letters received before related-letter matching existed have no keys: give them file hashes and keys (oldest first,
/// so newer letters find older ones) and link them. Quiet: no timeline entries or webhooks for history.
/// </summary>
public class RelatedLetterBackfill(
    IAppDbContext db, IFileStorage storage, RelatedLetterFinder finder, ThreadOptions options, TimeProvider clock, ILogger<RelatedLetterBackfill> logger)
{
    public async Task<int> RunAsync(CancellationToken ct, int batchSize = 50)
    {
        if (!options.Enabled) return 0;
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-options.LookbackDays);
        var unreadable = new List<Guid>(); // e.g. a file removed from storage: skipped, not retried in this run
        var done = 0;
        while (true)
        {
            var batch = await db.Documents
                .Where(d => d.ContentHash == null && d.UploadedAt >= since && !unreadable.Contains(d.Id))
                .OrderBy(d => d.UploadedAt)
                .Take(batchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) return done;

            foreach (var d in batch)
            {
                try
                {
                    await using var file = storage.Open(d.StoragePath);
                    d.ContentHash = await LetterKeys.HashAsync(file, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "No file to hash for document {DocumentId}", d.Id);
                    unreadable.Add(d.Id);
                }
                await db.SaveChangesAsync(ct);
                await finder.LinkAsync(d.Id, ct);
                await db.SaveChangesAsync(ct); // before the next one, so it can find this one
                done++;
            }
        }
    }
}
