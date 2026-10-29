using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SmartInbox.Domain;

namespace SmartInbox.Application;

public record ApiKeyDto(
    int Id, string Name, string Prefix, string Role, int? DepartmentId, string? CreatedBy,
    DateTime CreatedAt, DateTime? ExpiresAt, DateTime? LastUsedAt, DateTime? RevokedAt);

public record CreateApiKeyRequest(string Name, string Role, int? DepartmentId, DateTime? ExpiresAt);

/// <summary>The only time the full key is ever returned.</summary>
public record CreatedApiKeyDto(ApiKeyDto Key, string Secret);

public class CreateApiKeyRequestValidator : AbstractValidator<CreateApiKeyRequest>
{
    public CreateApiKeyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ApiKeyService.MaxNameLength);
        RuleFor(x => x.Role).Must(r => Roles.All.Contains(r)).WithMessage($"Role must be one of: {string.Join(", ", Roles.All)}.");
        RuleFor(x => x.DepartmentId).NotNull().When(x => x.Role == Roles.Department).WithMessage("Department keys need a department.");
        RuleFor(x => x.ExpiresAt).Must(e => e is null || e > DateTime.UtcNow).WithMessage("Expiry must be in the future.");
    }
}

/// <summary>
/// Key format: "sik_" + 8 hex chars (public prefix) + "_" + 32 random bytes in base64url.
/// The prefix finds the row; the SHA-256 hash of the whole key proves it.
/// </summary>
public static class ApiKeyFormat
{
    public const string Marker = "sik_";
    private const int PrefixLength = 12;   // "sik_" + 8 hex
    private const int SecretLength = 43;   // 32 bytes, base64url without padding

    public static (string Key, string Prefix) Generate()
    {
        var prefix = Marker + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return ($"{prefix}_{secret}", prefix);
    }

    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    /// <summary>The public prefix of a well-formed key, or null.</summary>
    public static string? PrefixOf(string key) =>
        key.Length == PrefixLength + 1 + SecretLength && key.StartsWith(Marker, StringComparison.Ordinal) && key[PrefixLength] == '_'
            ? key[..PrefixLength]
            : null;
}

public class ApiKeyService(IAppDbContext db, ICurrentUser user)
{
    public const int MaxNameLength = 100;
    /// <summary>LastUsedAt is only written when it is older than this, so busy integrations don't cost a DB write per request.</summary>
    public static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken ct) =>
        (await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(ct)).Select(ToDto).ToList();

    /// <summary>Null when the department doesn't exist. The request must already be validated.</summary>
    public async Task<CreatedApiKeyDto?> CreateAsync(CreateApiKeyRequest request, CancellationToken ct)
    {
        var departmentId = request.Role == Roles.Department ? request.DepartmentId : null;
        if (departmentId is not null && !await db.Departments.AnyAsync(d => d.Id == departmentId, ct)) return null;

        // A prefix collision is a 1-in-4-billion event; pick another rather than fail.
        var (key, prefix) = ApiKeyFormat.Generate();
        while (await db.ApiKeys.AnyAsync(k => k.Prefix == prefix, ct)) (key, prefix) = ApiKeyFormat.Generate();

        var entity = new ApiKey
        {
            Name = request.Name.Trim(), Prefix = prefix, Hash = ApiKeyFormat.Hash(key), Role = request.Role,
            DepartmentId = departmentId, CreatedBy = user.Name, ExpiresAt = request.ExpiresAt,
        };
        db.ApiKeys.Add(entity);
        await db.SaveChangesAsync(ct);
        return new CreatedApiKeyDto(ToDto(entity), key);
    }

    /// <summary>False when the key doesn't exist. Revoking twice keeps the first revocation time.</summary>
    public async Task<bool> RevokeAsync(int id, CancellationToken ct)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null) return false;
        key.RevokedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The active key matching the presented secret, or null if it's unknown, wrong, revoked or expired.</summary>
    public async Task<ApiKey?> AuthenticateAsync(string presented, CancellationToken ct)
    {
        if (ApiKeyFormat.PrefixOf(presented) is not { } prefix) return null;
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix, ct);
        if (key is null) return null;

        var expected = Encoding.ASCII.GetBytes(key.Hash);
        var actual = Encoding.ASCII.GetBytes(ApiKeyFormat.Hash(presented));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return null;

        var now = DateTime.UtcNow;
        if (key.RevokedAt is not null || key.ExpiresAt <= now) return null;

        if (key.LastUsedAt is null || now - key.LastUsedAt >= LastUsedResolution)
        {
            key.LastUsedAt = now;
            await db.SaveChangesAsync(ct);
        }
        return key;
    }

    private static ApiKeyDto ToDto(ApiKey k) =>
        new(k.Id, k.Name, k.Prefix, k.Role, k.DepartmentId, k.CreatedBy, k.CreatedAt, k.ExpiresAt, k.LastUsedAt, k.RevokedAt);
}
