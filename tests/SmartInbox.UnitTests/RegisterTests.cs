using Microsoft.EntityFrameworkCore;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.UnitTests;

/// <summary>
/// On SQLite (a real, transactional database in a temp file), not the in-memory provider: issuing relies on
/// all-or-nothing saves and concurrency checks, which the in-memory provider doesn't give.
/// </summary>
public class RegisterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"smartinbox-register-{Guid.NewGuid():N}.db");
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));
    private static readonly TestUser Clerk = new("Sara", Roles.Clerk);
    private static readonly TestUser Legal = new("Omar", Roles.Department, DepartmentId: 2);

    /// <summary>A separate context and connection on the same database: like a second request.</summary>
    private SmartInboxDbContext Db()
    {
        var db = new SmartInboxDbContext(new DbContextOptionsBuilder<SmartInboxDbContext>()
            .UseSqlite($"Data Source={_path};Default Timeout=30;Pooling=False").Options);
        db.Database.EnsureCreated();
        return db;
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { }
    }

    private RegisterService Register(SmartInboxDbContext db, ICurrentUser? user = null, RegisterOptions? options = null) =>
        new(db, user ?? Clerk, options ?? new RegisterOptions(), _clock, new ReminderOptions { TimeZone = "Asia/Dubai" });

    private static IssueRequest Letter(int department = 2, string recipient = "Demo City Court") =>
        new(department, recipient, "Your notice ref. 2027/118", "بشأن إخطاركم رقم 2027/118");

    [Theory]
    [InlineData("{Prefix}/OUT/{Year}/{Seq:0000}", "MDS/OUT/2026/0042")]
    [InlineData("{Prefix}/{Dept}/{Year}/{Seq:000}", "MDS/LEG/2026/042")]
    [InlineData("OUT-{Year}-{Seq}", "OUT-2026-42")]
    public void Format_ReplacesTheTokens(string format, string expected) =>
        Assert.Equal(expected, OutgoingNumber.Format(format, "MDS", 2026, 42, "LEG"));

    [Fact]
    public async Task Issue_GivesConsecutiveNumbers_AndRecordsTheEntry()
    {
        using var db = Db();
        var first = await Register(db).IssueAsync(Letter(), default);
        var second = await Register(db).IssueAsync(Letter(recipient: "Nimbus Cloud"), default);

        Assert.Equal(RegisterService.Outcome.Ok, first.Outcome);
        Assert.Equal("MDS/OUT/2026/0001", first.Letter!.Number);
        Assert.Equal("MDS/OUT/2026/0002", second.Letter!.Number);
        Assert.Equal("Sara", first.Letter.IssuedBy);
        Assert.Equal(OutgoingStatus.Issued, first.Letter.Status);
        Assert.Equal("بشأن إخطاركم رقم 2027/118", first.Letter.SubjectAr);
    }

    [Fact]
    public async Task Sequence_RestartsEachYear_InTheOrganisationsTimeZone()
    {
        using var db = Db();
        await Register(db).IssueAsync(Letter(), default);
        await Register(db).IssueAsync(Letter(), default);

        _clock.Now = new DateTimeOffset(2026, 12, 31, 21, 0, 0, TimeSpan.Zero); // already 1 January 2027 in Dubai
        var newYear = await Register(db).IssueAsync(Letter(), default);

        Assert.Equal("MDS/OUT/2027/0001", newYear.Letter!.Number);
    }

    [Fact]
    public async Task PerDepartmentSequences_CountSeparately()
    {
        using var db = Db();
        var options = new RegisterOptions { Format = "{Prefix}/{Dept}/{Year}/{Seq:0000}", SequencePerDepartment = true };

        var legal1 = await Register(db, options: options).IssueAsync(Letter(2), default);
        var hr1 = await Register(db, options: options).IssueAsync(Letter(4), default);
        var legal2 = await Register(db, options: options).IssueAsync(Letter(2), default);

        Assert.Equal("MDS/LEG/2026/0001", legal1.Letter!.Number);
        Assert.Equal("MDS/HR/2026/0001", hr1.Letter!.Number);
        Assert.Equal("MDS/LEG/2026/0002", legal2.Letter!.Number);
    }

    [Fact]
    public async Task FiftyAtTheSameMoment_GetFiftyDistinctConsecutiveNumbers()
    {
        using (var db = Db()) await Register(db).IssueAsync(Letter(), default); // this year's counter exists

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            using var db = Db();
            return await Register(db).IssueAsync(Letter(), default);
        })));

        Assert.All(results, r => Assert.Equal(RegisterService.Outcome.Ok, r.Outcome));
        var sequences = results.Select(r => r.Letter!.Sequence).Order().ToList();
        Assert.Equal(Enumerable.Range(2, 50), sequences); // no duplicates, no gaps
        using var check = Db();
        Assert.Equal(51, check.OutgoingLetters.Count());
        Assert.Equal(52, check.RegisterCounters.Single().Next);
    }

    [Fact]
    public async Task AFailedSave_BurnsNoNumber()
    {
        using var db = Db();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            Register(db).IssueAsync(Letter(), default, alongside: _ => throw new InvalidOperationException("approval failed")));

        using var fresh = Db();
        var next = await Register(fresh).IssueAsync(Letter(), default);
        Assert.Equal("MDS/OUT/2026/0001", next.Letter!.Number);
    }

    [Fact]
    public async Task Alongside_RunsWithTheIssuedNumber_InTheSameSave()
    {
        using var db = Db();
        var doc = new Document { FileName = "x", ContentType = "t", StoragePath = "x" };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        await Register(db).IssueAsync(Letter() with { DocumentId = doc.Id }, default, alongside: l => doc.HandlingNote = $"Replied with {l.Number}");

        using var check = Db();
        Assert.Equal("Replied with MDS/OUT/2026/0001", check.Documents.Single().HandlingNote);
        Assert.Equal(doc.Id, check.OutgoingLetters.Single().DocumentId);
    }

    [Fact]
    public async Task DepartmentStaff_IssueAndSeeOnlyTheirOwn()
    {
        using var db = Db();
        Assert.Equal(RegisterService.Outcome.Forbidden, (await Register(db, Legal).IssueAsync(Letter(4), default)).Outcome);
        await Register(db, Legal).IssueAsync(Letter(2), default);
        var hr = await Register(db).IssueAsync(Letter(4), default);

        var seen = await Register(db, Legal).ListAsync(new RegisterQuery(), default);
        Assert.All(seen.Items, l => Assert.Equal(2, l.DepartmentId));
        Assert.Null(await Register(db, Legal).GetAsync(hr.Letter!.Id, default));
        Assert.Equal(RegisterService.Outcome.NotFound, (await Register(db, Legal).CancelAsync(hr.Letter.Id, "x", default)).Outcome);
    }

    [Fact]
    public async Task UnknownDepartment_IsRefused()
    {
        using var db = Db();
        Assert.Equal(RegisterService.Outcome.UnknownDepartment, (await Register(db).IssueAsync(Letter(999), default)).Outcome);
    }

    [Fact]
    public async Task MarkSent_ThenCancel_KeepsTheNumberAndTheReason()
    {
        using var db = Db();
        var issued = (await Register(db).IssueAsync(Letter(), default)).Letter!;

        var sent = await Register(db).MarkSentAsync(issued.Id, new MarkSentRequest(SentVia.Post, Note: "Registered mail"), default);
        Assert.Equal(OutgoingStatus.Sent, sent.Letter!.Status);
        Assert.Equal(SentVia.Post, sent.Letter.SentVia);
        Assert.Equal(_clock.Now.UtcDateTime, sent.Letter.SentAt);

        var cancelled = await Register(db).CancelAsync(issued.Id, " Wrong recipient ", default);
        Assert.Equal(OutgoingStatus.Cancelled, cancelled.Letter!.Status);
        Assert.Equal("Wrong recipient", cancelled.Letter.CancelReason);
        Assert.Equal("Sara", cancelled.Letter.CancelledBy);
        Assert.Equal(RegisterService.Outcome.NotAllowedNow, (await Register(db).CancelAsync(issued.Id, "again", default)).Outcome);
        Assert.Equal(RegisterService.Outcome.NotAllowedNow, (await Register(db).MarkSentAsync(issued.Id, new MarkSentRequest(SentVia.Email), default)).Outcome);

        var next = await Register(db).IssueAsync(Letter(), default);
        Assert.Equal("MDS/OUT/2026/0002", next.Letter!.Number); // a cancelled number is never reused
    }

    [Fact]
    public async Task Search_ByNumberRecipientOrSubject_NewestFirst()
    {
        using var db = Db();
        await Register(db).IssueAsync(Letter(recipient: "Demo City Court"), default);
        await Register(db).IssueAsync(Letter(recipient: "Nimbus Cloud Services") with { SubjectEn = "Invoice NCS-88213" }, default);

        var byRecipient = await Register(db).ListAsync(new RegisterQuery(Q: "Nimbus"), default);
        var byNumber = await Register(db).ListAsync(new RegisterQuery(Q: "2026/0001"), default);
        var all = await Register(db).ListAsync(new RegisterQuery(Year: 2026), default);

        Assert.Equal("MDS/OUT/2026/0002", Assert.Single(byRecipient.Items).Number);
        Assert.Equal("MDS/OUT/2026/0001", Assert.Single(byNumber.Items).Number);
        Assert.Equal(["MDS/OUT/2026/0002", "MDS/OUT/2026/0001"], all.Items.Select(l => l.Number));
    }

    [Theory]
    [InlineData("Court", "Re: notice", null, true)]
    [InlineData("", "Re: notice", null, false)]
    [InlineData("Court", null, null, false)]
    [InlineData("Court", null, "بشأن", true)]
    public void Validator_NeedsARecipientAndASubject(string recipient, string? en, string? ar, bool valid) =>
        Assert.Equal(valid, new IssueRequestValidator().Validate(new IssueRequest(2, recipient, en, ar)).IsValid);
}
