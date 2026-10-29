using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Persistence;
using SmartInbox.Infrastructure.Webhooks;

namespace SmartInbox.UnitTests;

internal class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Reversible but visibly different, so tests can tell protected from plain.</summary>
internal class ReversingProtector : ISecretProtector
{
    public string Protect(string plaintext) => "p:" + new string(plaintext.Reverse().ToArray());
    public string Unprotect(string protectedText) => new(protectedText[2..].Reverse().ToArray());
}

internal class FakeTransport : IWebhookTransport
{
    public List<WebhookRequest> Requests { get; } = [];
    public Func<WebhookRequest, WebhookResponse> Respond { get; set; } = _ => new WebhookResponse(200, "ok", null, 5);

    public Task<WebhookResponse> SendAsync(WebhookRequest request, CancellationToken ct = default)
    {
        lock (Requests) Requests.Add(request);
        return Task.FromResult(Respond(request));
    }
}

public class WebhookTests
{
    private readonly SmartInboxDbContext _db = TestDb.Create();
    private readonly FakeTransport _transport = new();
    private readonly ReversingProtector _protector = new();
    // Ahead of the wall clock, so rows stamped with DateTime.UtcNow are already due.
    private readonly TestClock _clock = new(DateTimeOffset.UtcNow.AddMinutes(1));
    private static readonly NotificationOptions Options = new() { AppBaseUrl = "http://inbox.test" };

    private WebhookPublisher Publisher => new(_db, Options);
    private WebhookDispatcher Dispatcher => new(_db, _transport, _protector, _clock, NullLogger<WebhookDispatcher>.Instance);
    private WebhookAdminService Admin => new(_db, _protector, Dispatcher, new TestUser("Admin", Roles.Admin), _clock);

    private async Task<(WebhookEndpoint Endpoint, string Secret)> Endpoint(string events = "*", int? departmentId = null)
    {
        var created = await Admin.CreateAsync(new WebhookEndpointRequest("https://hooks.example.com/x", "Test", events == "*" ? null : events.Split(','), departmentId), default);
        return (_db.WebhookEndpoints.Single(e => e.Id == created!.Endpoint.Id), created!.Secret);
    }

    private DocumentService Documents(ICurrentUser user) => new(_db, new MemoryStorage(), new NullQueue(), new RecordingNotifier(), user, Options, Publisher);

    private async Task<Document> AnalyzedDocument()
    {
        var d = new Document { FileName = "a.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed };
        d.Analysis = new DocumentAnalysis { DocumentId = d.Id, Priority = Priority.High, Category = DocumentCategory.Legal, SuggestedDepartmentId = 2, Confidence = 0.9 };
        _db.Documents.Add(d);
        await _db.SaveChangesAsync();
        return d;
    }

    // ── Signing ──────────────────────────────────────────────────────

    [Fact]
    public void Signature_MatchesKnownVector() =>
        // Computed independently: hmac.new(b"whsec_test", b'1730538840.{"id":"evt_1"}', sha256).hexdigest()
        Assert.Equal("75bae9ab5519a2aa8092ccf0216135045ae428b883621dcd44435d9a1c71632e",
            WebhookSignature.Compute("whsec_test", 1730538840, """{"id":"evt_1"}"""));

    [Fact]
    public void SignatureHeader_HasOneV1PerSecret() =>
        Assert.Matches("^t=42,v1=[0-9a-f]{64},v1=[0-9a-f]{64}$", WebhookSignature.HeaderValue(["a", "b"], 42, "{}"));

    // ── Publishing ───────────────────────────────────────────────────

    [Fact]
    public async Task Route_QueuesDeliveryInTheSameSave_WithThinPayload()
    {
        var (endpoint, _) = await Endpoint();
        var d = await AnalyzedDocument();

        await Documents(new TestUser("Sara", Roles.Clerk)).RouteAsync(d.Id, new RouteRequest(2, "please handle"), null, default);

        var delivery = Assert.Single(_db.WebhookDeliveries);
        Assert.Equal(endpoint.Id, delivery.EndpointId);
        Assert.Equal(WebhookEvents.DocumentRouted, delivery.EventType);
        Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status);

        using var json = JsonDocument.Parse(delivery.Payload);
        var root = json.RootElement;
        Assert.Equal(delivery.EventId, root.GetProperty("id").GetString());
        Assert.Equal("document.routed", root.GetProperty("type").GetString());
        Assert.Equal(WebhookRules.ApiVersion, root.GetProperty("apiVersion").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(d.Id, data.GetProperty("documentId").GetGuid());
        Assert.Equal(2, data.GetProperty("departmentId").GetInt32());
        Assert.Equal("High", data.GetProperty("priority").GetString());
        Assert.Equal("Sara", data.GetProperty("actor").GetString());
        Assert.Equal($"http://inbox.test/documents/{d.Id}", data.GetProperty("url").GetString());
        // Thin: no summaries, text or notes leave the system.
        Assert.DoesNotContain("please handle", delivery.Payload);
        Assert.False(data.TryGetProperty("summaryEn", out _));
    }

    [Fact]
    public async Task EveryDocumentEvent_FiresFromItsSource()
    {
        await Endpoint();
        var desk = Documents(new TestUser("Sara", Roles.Clerk));
        var legal = Documents(new TestUser("Omar", Roles.Department, DepartmentId: 2));

        var uploaded = await desk.UploadTextAsync("Hello", null, default);
        var d = await AnalyzedDocument();
        await desk.RouteAsync(d.Id, new RouteRequest(2, null), null, default);
        await legal.AcknowledgeAsync(d.Id, default);
        await legal.CloseAsync(d.Id, "done", default);
        await legal.ReopenAsync(d.Id, default);

        Assert.Equal(
            [WebhookEvents.DocumentReceived, WebhookEvents.DocumentRouted, WebhookEvents.DocumentAcknowledged, WebhookEvents.DocumentClosed, WebhookEvents.DocumentReopened],
            _db.WebhookDeliveries.OrderBy(x => x.Id).Select(x => x.EventType).ToList());
        Assert.Contains(uploaded.Id.ToString(), _db.WebhookDeliveries.OrderBy(x => x.Id).First().Payload);
    }

    [Fact]
    public async Task Processor_FiresAnalyzedAndFailed()
    {
        await Endpoint();
        var storage = new MemoryStorage();
        var docs = new DocumentService(_db, storage, new NullQueue(), new RecordingNotifier());
        var ok = await docs.UploadTextAsync("Please review the contract", null, default);
        var bad = await docs.UploadTextAsync("Another letter", null, default);

        await new DocumentProcessor(_db, storage, [new Infrastructure.TextExtraction.PlainTextExtractor()], new FakeAi(_ => FakeAi.Result(2, 0.9)),
            new RecordingNotifier(), NullLogger<DocumentProcessor>.Instance, Publisher).ProcessAsync(ok.Id, default);
        await new DocumentProcessor(_db, storage, [new Infrastructure.TextExtraction.PlainTextExtractor()], new FakeAi(_ => throw new AiException("down")),
            new RecordingNotifier(), NullLogger<DocumentProcessor>.Instance, Publisher).ProcessAsync(bad.Id, default);

        Assert.Equal([WebhookEvents.DocumentAnalyzed, WebhookEvents.DocumentAnalysisFailed], _db.WebhookDeliveries.OrderBy(x => x.Id).Select(x => x.EventType).ToList());
        Assert.Contains("\"suggestedDepartmentId\":2", _db.WebhookDeliveries.OrderBy(x => x.Id).First().Payload);
    }

    [Fact]
    public async Task Subscriptions_FilterByEventAndDepartment()
    {
        var (all, _) = await Endpoint();
        var (closedOnly, _) = await Endpoint(WebhookEvents.DocumentClosed);
        var (hrOnly, _) = await Endpoint(departmentId: 4);
        var (legalOnly, _) = await Endpoint(departmentId: 2);
        var d = await AnalyzedDocument();

        await Documents(new TestUser("Sara", Roles.Clerk)).RouteAsync(d.Id, new RouteRequest(2, null), null, default);

        var endpoints = _db.WebhookDeliveries.Select(x => x.EndpointId).OrderBy(x => x).ToList();
        Assert.Equal([all.Id, legalOnly.Id], endpoints);
        Assert.DoesNotContain(closedOnly.Id, endpoints);
        Assert.DoesNotContain(hrOnly.Id, endpoints);
    }

    [Fact]
    public async Task Reroute_ReachesThePreviousDepartmentsEndpointToo()
    {
        var (legalOnly, _) = await Endpoint(departmentId: 2);
        var d = await AnalyzedDocument();
        var desk = Documents(new TestUser("Sara", Roles.Clerk));
        await desk.RouteAsync(d.Id, new RouteRequest(2, null), null, default);

        await desk.RouteAsync(d.Id, new RouteRequest(4, null), null, default);

        var last = _db.WebhookDeliveries.OrderBy(x => x.Id).Last();
        Assert.Equal(legalOnly.Id, last.EndpointId);
        Assert.Contains("\"previousDepartmentId\":2", last.Payload);
    }

    [Fact]
    public async Task DisabledEndpoints_GetNothing()
    {
        var (e, _) = await Endpoint();
        e.Enabled = false;
        await _db.SaveChangesAsync();

        await Documents(new TestUser("Sara", Roles.Clerk)).UploadTextAsync("x", null, default);

        Assert.Empty(_db.WebhookDeliveries);
    }

    // ── Dispatching ──────────────────────────────────────────────────

    [Fact]
    public async Task Dispatch_SignsWithTheEndpointSecret_AndMarksDelivered()
    {
        var (_, secret) = await Endpoint();
        await Documents(new TestUser("Sara", Roles.Clerk)).UploadTextAsync("x", null, default);

        Assert.Equal(1, await Dispatcher.DispatchPendingAsync(default));

        var request = Assert.Single(_transport.Requests);
        var delivery = _db.WebhookDeliveries.Single();
        Assert.Equal(delivery.EventId, request.Headers[WebhookSignature.EventIdHeader]);
        Assert.Equal("document.received", request.Headers[WebhookSignature.EventTypeHeader]);
        var t = _clock.Now.ToUnixTimeSeconds();
        Assert.Equal($"t={t},v1={WebhookSignature.Compute(secret, t, request.Body)}", request.Headers[WebhookSignature.Header]);
        Assert.Equal(WebhookDeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal(200, delivery.LastStatusCode);
        Assert.NotNull(delivery.DeliveredAt);
    }

    [Fact]
    public async Task Dispatch_RetriesWithBackoff_ThenFails_KeepingTheEventId()
    {
        await Endpoint();
        await Documents(new TestUser("Sara", Roles.Clerk)).UploadTextAsync("x", null, default);
        _transport.Respond = _ => new WebhookResponse(500, "boom", "HTTP 500", 3);
        var delivery = _db.WebhookDeliveries.Single();

        for (var attempt = 1; attempt <= WebhookRules.MaxAttempts; attempt++)
        {
            Assert.Equal(1, await Dispatcher.DispatchPendingAsync(default));
            Assert.Equal(0, await Dispatcher.DispatchPendingAsync(default)); // not due again yet
            if (attempt < WebhookRules.MaxAttempts)
            {
                Assert.Equal(_clock.Now.UtcDateTime + WebhookRules.Backoff[attempt - 1], delivery.NextAttemptAt);
                _clock.Now += WebhookRules.Backoff[attempt - 1];
            }
        }

        Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(7, delivery.Attempts);
        Assert.Equal("HTTP 500", delivery.LastError);
        Assert.Equal("boom", delivery.LastResponseSnippet);
        Assert.Single(_transport.Requests.Select(r => r.Headers[WebhookSignature.EventIdHeader]).Distinct());
    }

    [Fact]
    public async Task Dispatch_DisablesEndpointAfter20FailedAttemptsInARow()
    {
        var (e, _) = await Endpoint();
        var docs = Documents(new TestUser("Sara", Roles.Clerk));
        for (var i = 0; i < 25; i++) await docs.UploadTextAsync($"x{i}", null, default);
        _transport.Respond = _ => new WebhookResponse(null, null, "Connection refused", 1);

        await Dispatcher.DispatchPendingAsync(default);

        Assert.False(e.Enabled);
        Assert.Equal(25, e.ConsecutiveFailures);
        Assert.Contains("Connection refused", e.DisabledReason);
        Assert.Equal(0, await Dispatcher.DispatchPendingAsync(default)); // nothing goes to a disabled endpoint
    }

    [Fact]
    public async Task Dispatch_SuccessResetsTheFailureCount()
    {
        var (e, _) = await Endpoint();
        e.ConsecutiveFailures = 19;
        await _db.SaveChangesAsync();
        await Documents(new TestUser("Sara", Roles.Clerk)).UploadTextAsync("x", null, default);

        await Dispatcher.DispatchPendingAsync(default);

        Assert.Equal(0, e.ConsecutiveFailures);
        Assert.True(e.Enabled);
    }

    [Fact]
    public async Task Dispatch_DisabledEndpoint_EmailsAdmins()
    {
        var (e, _) = await Endpoint();
        e.ConsecutiveFailures = WebhookRules.DisableAfterFailures - 1;
        await _db.SaveChangesAsync();
        await Documents(new TestUser("Sara", Roles.Clerk)).UploadTextAsync("x", null, default);
        _transport.Respond = _ => new WebhookResponse(503, null, "HTTP 503", 1);
        var email = new RecordingEmail();

        await new WebhookDispatcher(_db, _transport, _protector, _clock, NullLogger<WebhookDispatcher>.Instance, new AdminDirectory(), email)
            .DispatchPendingAsync(default);

        Assert.False(e.Enabled);
        Assert.Equal("admin@test", Assert.Single(email.Sent).To);
    }

    // ── Admin ────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_StoresTheSecretEncrypted()
    {
        var (e, secret) = await Endpoint();

        Assert.StartsWith("whsec_", secret);
        Assert.NotEqual(secret, e.Secret);
        Assert.Equal(secret, _protector.Unprotect(e.Secret));
    }

    [Fact]
    public async Task RotateSecret_SignsWithBothSecretsFor24Hours()
    {
        var (e, oldSecret) = await Endpoint();
        var rotated = await Admin.RotateSecretAsync(e.Id, default);
        Assert.True(rotated!.Endpoint.RotationInProgress);

        var ping = await Admin.PingAsync(e.Id, default);
        var header = _transport.Requests.Last().Headers[WebhookSignature.Header];
        var body = _transport.Requests.Last().Body;
        var t = _clock.Now.ToUnixTimeSeconds();
        Assert.Equal(WebhookDeliveryStatus.Delivered, ping!.Status);
        Assert.Equal($"t={t},v1={WebhookSignature.Compute(rotated.Secret, t, body)},v1={WebhookSignature.Compute(oldSecret, t, body)}", header);

        _clock.Now += WebhookAdminService.RotationOverlap + TimeSpan.FromSeconds(1);
        await Admin.PingAsync(e.Id, default);
        Assert.Single(_transport.Requests.Last().Headers[WebhookSignature.Header].Split(",v1=").Skip(1));
    }

    [Fact]
    public async Task Ping_SendsImmediately_AndIsLogged()
    {
        var (e, _) = await Endpoint();

        var ping = await Admin.PingAsync(e.Id, default);

        Assert.Equal(WebhookEvents.Ping, ping!.EventType);
        Assert.Equal(WebhookDeliveryStatus.Delivered, ping.Status);
        var log = await Admin.DeliveriesAsync(e.Id, 1, default);
        Assert.Equal(ping.Id, Assert.Single(log!.Items).Id);
    }

    [Fact]
    public async Task Redeliver_SendsTheSameEventAgain()
    {
        var (e, _) = await Endpoint();
        _transport.Respond = _ => new WebhookResponse(500, null, "HTTP 500", 1);
        var first = await Admin.PingAsync(e.Id, default);
        _transport.Respond = _ => new WebhookResponse(204, null, null, 1);

        var again = await Admin.RedeliverAsync(first!.Id, default);

        Assert.NotEqual(first.Id, again!.Id);
        Assert.Equal(first.EventId, again.EventId);
        Assert.Equal(first.Payload, again.Payload);
        Assert.Equal(WebhookDeliveryStatus.Delivered, again.Status);
    }

    [Fact]
    public async Task List_ShowsSuccessRateAndLastDelivery()
    {
        var (e, _) = await Endpoint();
        await Admin.PingAsync(e.Id, default);
        _transport.Respond = _ => new WebhookResponse(500, null, "HTTP 500", 1);
        await Admin.PingAsync(e.Id, default);

        var row = Assert.Single(await Admin.ListAsync(default));

        Assert.Equal(1, row.Deliveries7d); // the failed one is still pending a retry
        Assert.Equal(1.0, row.SuccessRate7d);
        Assert.Equal(500, row.LastDelivery!.StatusCode);
    }

    [Fact]
    public async Task Update_ReenablingResetsFailures()
    {
        var (e, _) = await Endpoint();
        e.Enabled = false;
        e.ConsecutiveFailures = 20;
        e.DisabledReason = "down";
        await _db.SaveChangesAsync();

        await Admin.UpdateAsync(e.Id, new WebhookEndpointRequest(e.Url, null, null, null, Enabled: true), default);

        Assert.True(e.Enabled);
        Assert.Equal(0, e.ConsecutiveFailures);
        Assert.Null(e.DisabledReason);
    }

    [Theory]
    [InlineData("https://hooks.example.com/x", false, true)]
    [InlineData("http://hooks.example.com/x", false, false)]
    [InlineData("http://localhost:5000/x", true, true)]
    [InlineData("ftp://hooks.example.com/x", true, false)]
    [InlineData("https://user:pass@hooks.example.com/x", false, false)]
    [InlineData("not a url", false, false)]
    public void Validator_ChecksUrl(string url, bool allowHttp, bool valid) =>
        Assert.Equal(valid, new WebhookEndpointRequestValidator(new WebhookOptions { AllowHttp = allowHttp })
            .Validate(new WebhookEndpointRequest(url, null, null, null)).IsValid);

    [Fact]
    public void Validator_RejectsUnknownEvents()
    {
        var v = new WebhookEndpointRequestValidator(new WebhookOptions());
        Assert.True(v.Validate(new WebhookEndpointRequest("https://a.example", null, ["document.routed", "deadline.approaching"], null)).IsValid);
        Assert.False(v.Validate(new WebhookEndpointRequest("https://a.example", null, ["document.deleted"], null)).IsValid);
        Assert.False(v.Validate(new WebhookEndpointRequest("https://a.example", null, [WebhookEvents.Ping], null)).IsValid);
    }

    // ── Deadlines ────────────────────────────────────────────────────

    [Fact]
    public async Task DeadlineScanner_FiresOncePerDocumentDepartmentAndDeadline()
    {
        await Endpoint();
        var today = DateOnly.FromDateTime(_clock.Now.UtcDateTime);
        var soon = await AnalyzedDocument();
        var later = await AnalyzedDocument();
        soon.Analysis!.Deadline = today.AddDays(2);
        later.Analysis!.Deadline = today.AddDays(10);
        await _db.SaveChangesAsync();
        var desk = Documents(new TestUser("Sara", Roles.Clerk));
        await desk.RouteAsync(soon.Id, new RouteRequest(2, null), null, default);
        await desk.RouteAsync(later.Id, new RouteRequest(2, null), null, default);
        var scanner = new WebhookDeadlineScanner(_db, Publisher, new WebhookOptions(), _clock, NullLogger<WebhookDeadlineScanner>.Instance);

        Assert.Equal(1, await scanner.RunAsync(default));
        Assert.Equal(0, await scanner.RunAsync(default));

        soon.Analysis.Deadline = today.AddDays(1); // new deadline → fires again
        await _db.SaveChangesAsync();
        Assert.Equal(1, await scanner.RunAsync(default));

        var events = _db.WebhookDeliveries.Where(x => x.EventType == WebhookEvents.DeadlineApproaching).ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, x => Assert.Contains(soon.Id.ToString(), x.Payload));
    }

    [Fact]
    public async Task DeadlineScanner_SkipsClosedAndOverdue()
    {
        await Endpoint();
        var today = DateOnly.FromDateTime(_clock.Now.UtcDateTime);
        var closed = await AnalyzedDocument();
        var overdue = await AnalyzedDocument();
        closed.Analysis!.Deadline = today.AddDays(1);
        overdue.Analysis!.Deadline = today.AddDays(-1);
        await _db.SaveChangesAsync();
        var desk = Documents(new TestUser("Sara", Roles.Clerk));
        await desk.RouteAsync(closed.Id, new RouteRequest(2, null), null, default);
        await desk.RouteAsync(overdue.Id, new RouteRequest(2, null), null, default);
        await Documents(new TestUser("Omar", Roles.Department, DepartmentId: 2)).CloseAsync(closed.Id, null, default);

        Assert.Equal(0, await new WebhookDeadlineScanner(_db, Publisher, new WebhookOptions(), _clock, NullLogger<WebhookDeadlineScanner>.Instance).RunAsync(default));
    }

    // ── SSRF ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")] // IPv4-mapped loopback
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("64:ff9b::a00:1")] // NAT64 to 10.0.0.1
    public void Ssrf_BlocksInternalAddresses(string ip) => Assert.False(SsrfGuard.IsPublic(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void Ssrf_AllowsPublicAddresses(string ip) => Assert.True(SsrfGuard.IsPublic(IPAddress.Parse(ip)));

    [Fact]
    public async Task Ssrf_HostnamesResolvingInside_AreBlockedAtConnectTime()
    {
        var transport = new HttpWebhookTransport(new GuardedClients(allowPrivateNetworks: false));

        var result = await transport.SendAsync(new WebhookRequest("http://localhost:1/hook", new Dictionary<string, string>(), "{}"));

        Assert.Null(result.StatusCode);
        Assert.Contains("non-public address", result.Error);
    }

    [Fact]
    public async Task Transport_DoesNotFollowRedirects()
    {
        using var listener = new HttpListener();
        var port = FreePort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var serve = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = "http://169.254.169.254/latest/meta-data";
            ctx.Response.Close();
        });

        var result = await new HttpWebhookTransport(new GuardedClients(allowPrivateNetworks: true))
            .SendAsync(new WebhookRequest($"http://127.0.0.1:{port}/hook", new Dictionary<string, string>(), "{}"));
        await serve;

        Assert.Equal(302, result.StatusCode);
        Assert.False(result.Succeeded);
        Assert.Contains("redirects are not followed", result.Error);
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>The same handler settings as production DI.</summary>
    private class GuardedClients(bool allowPrivateNetworks) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, ConnectCallback = SsrfGuard.ConnectCallback(allowPrivateNetworks),
        }) { Timeout = HttpWebhookTransport.Timeout };
    }

    private class AdminDirectory : IUserDirectory
    {
        public Task<DirectoryUser?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult<DirectoryUser?>(null);
        public Task<IReadOnlyList<DirectoryUser>> DepartmentMembersAsync(int departmentId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DirectoryUser>>([]);
        public Task<IReadOnlyList<string>> AdminEmailsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["admin@test"]);
    }

    private class RecordingEmail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
