using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.UnitTests;

internal class RecordingDraftQueue : IDraftQueue
{
    public List<Guid> Enqueued { get; } = [];

    public ValueTask EnqueueAsync(Guid draftId, CancellationToken ct = default)
    {
        Enqueued.Add(draftId);
        return ValueTask.CompletedTask;
    }
}

public class ReplyDraftTests
{
    private readonly SmartInboxDbContext _db = TestDb.Create();
    private readonly RecordingDraftQueue _queue = new();
    private static readonly TestUser Clerk = new("Sara", Roles.Clerk);
    private static readonly TestUser Legal = new("Omar", Roles.Department, DepartmentId: 2);
    private static readonly TestUser Hr = new("Huda", Roles.Department, DepartmentId: 4);

    private ReplyDraftService Service(ICurrentUser user, DraftingOptions? options = null) =>
        new(_db, user, options ?? new DraftingOptions(), _queue, new RecordingNotifier());

    private async Task<Guid> DocumentAsync(int? routedTo = 2)
    {
        var d = new Document { FileName = "court.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed };
        _db.Documents.Add(d);
        await _db.SaveChangesAsync();
        if (routedTo is { } dep)
            await new DocumentService(_db, new MemoryStorage(), new NullQueue(), new RecordingNotifier(), Clerk).RouteAsync(d.Id, new RouteRequest(dep, null), null, default);
        return d.Id;
    }

    /// <summary>Stands in for the generator (Day 2): fills the draft and marks it Ready.</summary>
    private async Task<ReplyDraft> MakeReadyAsync(Guid draftId, string bodyEn = "We acknowledge your notice ref [●].")
    {
        var d = _db.ReplyDrafts.Single(x => x.Id == draftId);
        d.Status = ReplyDraftStatus.Ready;
        d.Engine = "Templates";
        d.SubjectEn = "Re: Court hearing notice";
        d.BodyEn = bodyEn;
        d.SubjectAr = "بشأن: إشعار جلسة";
        d.BodyAr = "نفيدكم باستلام إشعاركم رقم [●].";
        d.MissingInfo = ReplyDraftRules.ToJson(["Your reference number"]);
        await _db.SaveChangesAsync();
        return d;
    }

    [Fact]
    public async Task Request_QueuesVersion1_AndLogsIt()
    {
        var doc = await DocumentAsync();

        var result = await Service(Legal).RequestAsync(doc, new RequestDraftRequest("legal-acknowledgement", " ask for the contract "), default);

        Assert.Equal(ReplyDraftService.Outcome.Ok, result.Outcome);
        var draft = result.Draft!;
        Assert.Equal(1, draft.Version);
        Assert.Equal(ReplyDraftStatus.Requested, draft.Status);
        Assert.Equal("ask for the contract", draft.Instructions);
        Assert.Equal("Omar", draft.RequestedBy);
        Assert.Equal(draft.Id, Assert.Single(_queue.Enqueued));
        Assert.Contains(_db.DocumentEvents, e => e.DocumentId == doc && e.Type == DocumentEventType.DraftRequested && e.Actor == "Omar");
    }

    [Fact]
    public async Task Request_NeedsARoutedDocument()
    {
        var doc = await DocumentAsync(routedTo: null);
        Assert.Equal(ReplyDraftService.Outcome.NotRouted, (await Service(Clerk).RequestAsync(doc, new(), default)).Outcome);
    }

    [Fact]
    public async Task Request_WhileOneIsBeingPrepared_IsBusy()
    {
        var doc = await DocumentAsync();
        var first = await Service(Clerk).RequestAsync(doc, new(), default);

        var second = await Service(Clerk).RequestAsync(doc, new(), default);

        Assert.Equal(ReplyDraftService.Outcome.Busy, second.Outcome);
        Assert.Equal(first.Draft!.Id, second.Draft!.Id);
        Assert.Single(_queue.Enqueued);
    }

    [Fact]
    public async Task Request_WhenDisabled_IsRefused()
    {
        var doc = await DocumentAsync();
        Assert.Equal(ReplyDraftService.Outcome.Disabled,
            (await Service(Clerk, new DraftingOptions { Enabled = false }).RequestAsync(doc, new(), default)).Outcome);
    }

    [Fact]
    public async Task OtherDepartments_CantSeeOrTouchDrafts()
    {
        var doc = await DocumentAsync(routedTo: 2);
        var draft = (await Service(Legal).RequestAsync(doc, new(), default)).Draft!;
        await MakeReadyAsync(draft.Id);

        Assert.Equal(ReplyDraftService.Outcome.NotFound, (await Service(Hr).RequestAsync(doc, new(), default)).Outcome);
        Assert.Null(await Service(Hr).ListAsync(doc, default));
        Assert.Null(await Service(Hr).GetAsync(draft.Id, default));
        Assert.Equal(ReplyDraftService.Outcome.NotFound, (await Service(Hr).ApproveAsync(draft.Id, default)).Outcome);
        Assert.NotNull(await Service(Clerk).GetAsync(draft.Id, default)); // the desk sees everything
    }

    [Fact]
    public async Task Edit_CreatesANewVersion_AndKeepsTheOriginal()
    {
        var doc = await DocumentAsync();
        var v1 = (await Service(Legal).RequestAsync(doc, new(), default)).Draft!;
        await MakeReadyAsync(v1.Id);

        var edit = await Service(Legal).UpdateAsync(v1.Id, new UpdateDraftRequest("Re: Hearing", "We received ref ADJD/4471.", "بشأن: الجلسة", "استلمنا الإشعار رقم ADJD/4471."), default);

        Assert.Equal(ReplyDraftService.Outcome.Ok, edit.Outcome);
        var v2 = edit.Draft!;
        Assert.Equal(2, v2.Version);
        Assert.Equal("Edited", v2.Engine);
        Assert.Equal("Omar", v2.EditedBy);
        Assert.Equal(0, v2.Placeholders);
        Assert.Equal(["Your reference number"], v2.MissingInfo);
        Assert.Contains("[●]", _db.ReplyDrafts.Single(d => d.Id == v1.Id).BodyEn); // original untouched

        var list = (await Service(Legal).ListAsync(doc, default))!;
        Assert.Equal([2, 1], list.Select(d => d.Version));
        Assert.Equal([true, false], list.Select(d => d.IsLatest));

        Assert.Equal(ReplyDraftService.Outcome.NotLatest, (await Service(Legal).UpdateAsync(v1.Id, new("a", "b", "c", "d"), default)).Outcome);
        Assert.Equal(ReplyDraftService.Outcome.NotLatest, (await Service(Legal).ApproveAsync(v1.Id, default)).Outcome);
    }

    [Fact]
    public async Task Edit_BeforeTheDraftIsReady_IsRefused()
    {
        var doc = await DocumentAsync();
        var draft = (await Service(Legal).RequestAsync(doc, new(), default)).Draft!;

        Assert.Equal(ReplyDraftService.Outcome.NotEditable, (await Service(Legal).UpdateAsync(draft.Id, new("a", "b", "c", "d"), default)).Outcome);
    }

    [Fact]
    public async Task Approve_IsBlockedWhilePlaceholdersRemain()
    {
        var doc = await DocumentAsync();
        var draft = (await Service(Legal).RequestAsync(doc, new(), default)).Draft!;
        await MakeReadyAsync(draft.Id);

        var blocked = await Service(Legal).ApproveAsync(draft.Id, default);

        Assert.Equal(ReplyDraftService.Outcome.Incomplete, blocked.Outcome);
        Assert.Equal(2, blocked.Draft!.Placeholders);
        Assert.Equal(ReplyDraftStatus.Ready, _db.ReplyDrafts.Single(d => d.Id == draft.Id).Status);
    }

    [Fact]
    public async Task Approve_ACompleteDraft_RecordsWhoAndWhen()
    {
        var doc = await DocumentAsync();
        var v1 = (await Service(Legal).RequestAsync(doc, new(), default)).Draft!;
        await MakeReadyAsync(v1.Id);
        var v2 = (await Service(Legal).UpdateAsync(v1.Id, new("Re: Hearing", "Body", "بشأن", "النص"), default)).Draft!;

        var approved = await Service(Legal).ApproveAsync(v2.Id, default);

        Assert.Equal(ReplyDraftService.Outcome.Ok, approved.Outcome);
        Assert.Equal(ReplyDraftStatus.Approved, approved.Draft!.Status);
        Assert.Equal("Omar", approved.Draft.DecidedBy);
        Assert.Contains(_db.DocumentEvents, e => e.Type == DocumentEventType.DraftApproved && e.Note == "version 2");
        Assert.Equal(ReplyDraftService.Outcome.NotEditable, (await Service(Legal).UpdateAsync(v2.Id, new("a", "b", "c", "d"), default)).Outcome);
    }

    [Fact]
    public async Task Approve_NeedsBothLanguages()
    {
        var doc = await DocumentAsync();
        var v1 = (await Service(Legal).RequestAsync(doc, new(), default)).Draft!;
        await MakeReadyAsync(v1.Id);
        var v2 = (await Service(Legal).UpdateAsync(v1.Id, new("Re: Hearing", "Body", "", ""), default)).Draft!;

        Assert.Equal(ReplyDraftService.Outcome.Incomplete, (await Service(Legal).ApproveAsync(v2.Id, default)).Outcome);
    }

    [Fact]
    public async Task Reject_ThenANewRequestStartsTheNextVersion()
    {
        var doc = await DocumentAsync();
        var v1 = (await Service(Clerk).RequestAsync(doc, new(), default)).Draft!;
        await MakeReadyAsync(v1.Id);

        var rejected = await Service(Clerk).RejectAsync(v1.Id, "Wrong tone", default);
        var again = await Service(Clerk).RequestAsync(doc, new RequestDraftRequest(Instructions: "more formal"), default);

        Assert.Equal(ReplyDraftStatus.Rejected, rejected.Draft!.Status);
        Assert.Contains(_db.DocumentEvents, e => e.Type == DocumentEventType.DraftRejected && e.Note == "Wrong tone");
        Assert.Equal(2, again.Draft!.Version);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("no gaps", 0)]
    [InlineData("[●]", 1)]
    [InlineData("Dear [●], your letter of [●] ref [●]", 3)]
    public void CountPlaceholders(string? text, int expected) => Assert.Equal(expected, ReplyDraftRules.CountPlaceholders(text));

    [Theory]
    [InlineData("legal-acknowledgement", true)]
    [InlineData("Legal Ack", false)]
    [InlineData("../etc", false)]
    public void Validator_ChecksTemplateKeys(string key, bool valid) =>
        Assert.Equal(valid, new RequestDraftRequestValidator().Validate(new RequestDraftRequest(key)).IsValid);
}
