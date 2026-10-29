using SmartInbox.Application;

namespace SmartInbox.UnitTests;

public class ApiKeyTests
{
    private static readonly TestUser Admin = new("Admin", Roles.Admin);

    private static async Task<(Infrastructure.Persistence.SmartInboxDbContext Db, ApiKeyService Keys, CreatedApiKeyDto Created)> CreateKey(
        string role = Roles.Clerk, int? departmentId = null, DateTime? expiresAt = null)
    {
        var db = TestDb.Create();
        var keys = new ApiKeyService(db, Admin);
        var created = await keys.CreateAsync(new CreateApiKeyRequest("ERP integration", role, departmentId, expiresAt), default);
        return (db, keys, created!);
    }

    [Fact]
    public void Generate_HasDocumentedShape()
    {
        var (key, prefix) = ApiKeyFormat.Generate();

        Assert.Matches("^sik_[0-9a-f]{8}_[A-Za-z0-9_-]{43}$", key);
        Assert.Equal(prefix, ApiKeyFormat.PrefixOf(key));
        Assert.NotEqual(ApiKeyFormat.Generate().Key, key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sik_")]
    [InlineData("Bearer sik_a1b2c3d4_x")]
    [InlineData("xyz_a1b2c3d4_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("sik_a1b2c3d4-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void PrefixOf_RejectsMalformedKeys(string key) => Assert.Null(ApiKeyFormat.PrefixOf(key));

    [Fact]
    public async Task Create_StoresOnlyTheHash()
    {
        var (db, _, created) = await CreateKey();

        var row = Assert.Single(db.ApiKeys);
        Assert.Equal(ApiKeyFormat.Hash(created.Secret), row.Hash);
        Assert.DoesNotContain(created.Secret, row.Hash);
        Assert.StartsWith(row.Prefix + "_", created.Secret);
        Assert.Equal("Admin", row.CreatedBy);
    }

    [Fact]
    public async Task Authenticate_AcceptsTheKey_AndActsAsItsRole()
    {
        var (_, keys, created) = await CreateKey(Roles.Department, departmentId: 2);

        var key = await keys.AuthenticateAsync(created.Secret, default);

        Assert.NotNull(key);
        Assert.Equal(Roles.Department, key.Role);
        Assert.Equal(2, key.DepartmentId);
    }

    [Fact]
    public async Task Authenticate_RejectsWrongSecretWithTheSamePrefix()
    {
        var (_, keys, created) = await CreateKey();
        var forged = created.Secret[..^1] + (created.Secret[^1] == 'A' ? 'B' : 'A');

        Assert.Null(await keys.AuthenticateAsync(forged, default));
    }

    [Fact]
    public async Task Authenticate_RejectsRevokedKeys()
    {
        var (_, keys, created) = await CreateKey();

        Assert.True(await keys.RevokeAsync(created.Key.Id, default));

        Assert.Null(await keys.AuthenticateAsync(created.Secret, default));
    }

    [Fact]
    public async Task Authenticate_RejectsExpiredKeys()
    {
        var (db, keys, created) = await CreateKey(expiresAt: DateTime.UtcNow.AddDays(1));
        Assert.NotNull(await keys.AuthenticateAsync(created.Secret, default));

        db.ApiKeys.Single().ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        Assert.Null(await keys.AuthenticateAsync(created.Secret, default));
    }

    [Fact]
    public async Task Authenticate_UpdatesLastUsedAtAtMostOncePerMinute()
    {
        var (db, keys, created) = await CreateKey();

        await keys.AuthenticateAsync(created.Secret, default);
        var first = db.ApiKeys.Single().LastUsedAt;
        await keys.AuthenticateAsync(created.Secret, default);

        Assert.NotNull(first);
        Assert.Equal(first, db.ApiKeys.Single().LastUsedAt);

        db.ApiKeys.Single().LastUsedAt = DateTime.UtcNow - ApiKeyService.LastUsedResolution - TimeSpan.FromSeconds(1);
        await db.SaveChangesAsync();
        await keys.AuthenticateAsync(created.Secret, default);

        Assert.True(db.ApiKeys.Single().LastUsedAt > first);
    }

    [Fact]
    public async Task Create_RejectsUnknownDepartment_AndDropsDepartmentForDeskRoles()
    {
        var db = TestDb.Create();
        var keys = new ApiKeyService(db, Admin);

        Assert.Null(await keys.CreateAsync(new CreateApiKeyRequest("Bad", Roles.Department, 999, null), default));

        var clerk = await keys.CreateAsync(new CreateApiKeyRequest("Desk bot", Roles.Clerk, 2, null), default);
        Assert.Null(clerk!.Key.DepartmentId);
    }

    [Fact]
    public async Task Revoke_UnknownKey_ReturnsFalse() =>
        Assert.False(await new ApiKeyService(TestDb.Create(), Admin).RevokeAsync(42, default));

    [Theory]
    [InlineData("", Roles.Clerk, null, false)]
    [InlineData("ERP", "Root", null, false)]
    [InlineData("ERP", Roles.Department, null, false)]
    [InlineData("ERP", Roles.Department, 2, true)]
    [InlineData("ERP", Roles.Admin, null, true)]
    public void Validator_ChecksNameRoleAndDepartment(string name, string role, int? departmentId, bool valid) =>
        Assert.Equal(valid, new CreateApiKeyRequestValidator().Validate(new CreateApiKeyRequest(name, role, departmentId, null)).IsValid);

    [Fact]
    public void Validator_RejectsPastExpiry() =>
        Assert.False(new CreateApiKeyRequestValidator().Validate(new CreateApiKeyRequest("ERP", Roles.Clerk, null, DateTime.UtcNow.AddMinutes(-1))).IsValid);
}
