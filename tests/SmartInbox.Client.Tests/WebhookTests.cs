using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using SmartInbox.Client.Webhooks;

namespace SmartInbox.Client.Tests;

public class WebhookVerifierTests
{
    private const string Secret = "whsec_test";
    private const string Body = """{"id":"evt_1","type":"document.routed","createdAt":"2026-11-02T09:14:00Z","apiVersion":"2026-11-01","data":{"documentId":"8f3c6a1e-0000-4000-8000-000000000001","status":"Routed","departmentId":2,"previousDepartmentId":null,"suggestedDepartmentId":2,"priority":"High","category":"Legal","confidence":0.91,"handling":"New","deadline":"2026-11-05","actor":"Sara","url":"https://inbox.example.com/documents/8f3c6a1e-0000-4000-8000-000000000001"}}""";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private static string Header(long t, params string[] secrets) =>
        $"t={t}," + string.Join(',', secrets.Select(s => $"v1={WebhookVerifier.Sign(s, t, Body)}"));

    [Fact]
    public void Sign_MatchesTheServerKnownVector() =>
        Assert.Equal("75bae9ab5519a2aa8092ccf0216135045ae428b883621dcd44435d9a1c71632e",
            WebhookVerifier.Sign("whsec_test", 1730538840, """{"id":"evt_1"}"""));

    [Fact]
    public void Verify_ParsesATypedEvent()
    {
        var evt = WebhookVerifier.Verify(Body, Header(Now.ToUnixTimeSeconds(), Secret), Secret, now: Now);

        var routed = Assert.IsType<DocumentRoutedEvent>(evt);
        Assert.Equal("evt_1", routed.Id);
        Assert.Equal(2, routed.Data.DepartmentId);
        Assert.Equal(Priority.High, routed.Data.Priority);
        Assert.Equal(new DateOnly(2026, 11, 5), routed.Data.Deadline);
    }

    [Fact]
    public void Verify_AcceptsEitherSignatureDuringRotation() =>
        Assert.IsType<DocumentRoutedEvent>(WebhookVerifier.Verify(Body, Header(Now.ToUnixTimeSeconds(), "whsec_new", Secret), Secret, now: Now));

    [Fact]
    public void Verify_RejectsWrongSecret() =>
        Assert.Throws<WebhookVerificationException>(() => WebhookVerifier.Verify(Body, Header(Now.ToUnixTimeSeconds(), "whsec_other"), Secret, now: Now));

    [Fact]
    public void Verify_RejectsATamperedBody() =>
        Assert.Throws<WebhookVerificationException>(() =>
            WebhookVerifier.Verify(Body.Replace("\"departmentId\":2", "\"departmentId\":3"), Header(Now.ToUnixTimeSeconds(), Secret), Secret, now: Now));

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    public void Verify_RejectsTimestampsOutsideFiveMinutes(int offsetSeconds) =>
        Assert.Throws<WebhookVerificationException>(() =>
            WebhookVerifier.Verify(Body, Header(Now.ToUnixTimeSeconds() + offsetSeconds, Secret), Secret, now: Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1=abc")]
    [InlineData("t=123")]
    [InlineData("garbage")]
    public void Verify_RejectsMissingOrMalformedHeaders(string? header) =>
        Assert.Throws<WebhookVerificationException>(() => WebhookVerifier.Verify(Body, header, Secret, now: Now));

    [Fact]
    public void Parse_KeepsUnknownEventTypes()
    {
        var evt = WebhookVerifier.Parse("""{"id":"evt_2","type":"document.archived","createdAt":"2026-11-02T09:14:00Z","apiVersion":"2027-01-01","data":{"x":1}}""");

        var unknown = Assert.IsType<UnknownEvent>(evt);
        Assert.Equal("document.archived", unknown.Type);
        Assert.Equal(1, unknown.Data.GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task MapSmartInboxWebhooks_Returns200ForValidAnd400ForInvalid()
    {
        var received = new List<SmartInboxEvent>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapSmartInboxWebhooks("/hooks", Secret, (evt, _) =>
        {
            received.Add(evt);
            return Task.CompletedTask;
        });
        await app.StartAsync();
        var http = app.GetTestClient();

        var valid = new HttpRequestMessage(HttpMethod.Post, "/hooks") { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        valid.Headers.Add(WebhookVerifier.SignatureHeader, Header(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Secret));
        var forged = new HttpRequestMessage(HttpMethod.Post, "/hooks") { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        forged.Headers.Add(WebhookVerifier.SignatureHeader, Header(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "whsec_attacker"));

        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(valid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(forged)).StatusCode);
        Assert.IsType<DocumentRoutedEvent>(Assert.Single(received));
    }
}

/// <summary>End to end: a real HTTP receiver gets a signed document.routed from the running server's webhook worker.</summary>
[Collection(ServerCollection.Name)]
public class WebhookEndToEndTests(SmartInboxFactory server)
{
    [Fact]
    public async Task RoutingADocument_DeliversASignedEventTheSdkVerifies()
    {
        using var listener = new HttpListener();
        var port = FreePort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        using var admin = await server.AdminAsync();
        var create = await admin.PostAsJsonAsync("/api/webhooks", new
        {
            url = $"http://127.0.0.1:{port}/hook", description = "e2e", events = new[] { "document.routed" }, departmentId = (int?)null,
        });
        create.EnsureSuccessStatusCode();
        var secret = (await create.Content.ReadFromJsonAsync<CreatedEndpoint>())!.Secret;

        var (_, key) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(key);
        var doc = await inbox.Documents.UploadTextAsync("Court hearing notice for the Ministry; appoint legal counsel.");
        await inbox.Documents.WaitForAnalysisAsync(doc.Id, TimeSpan.FromSeconds(30));
        await inbox.Documents.RouteAsync(doc.Id, 2);

        // The worker starts 10 s after boot and polls every 5 s.
        var contextTask = listener.GetContextAsync();
        Assert.Same(contextTask, await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromSeconds(45))));
        var ctx = await contextTask;
        string body;
        using (var reader = new StreamReader(ctx.Request.InputStream)) body = await reader.ReadToEndAsync();
        ctx.Response.StatusCode = 200;
        ctx.Response.Close();

        var evt = Assert.IsType<DocumentRoutedEvent>(WebhookVerifier.Verify(body, ctx.Request.Headers[WebhookVerifier.SignatureHeader], secret));
        Assert.Equal(doc.Id, evt.Data.DocumentId);
        Assert.Equal(2, evt.Data.DepartmentId);
        Assert.Equal("API key: Clerk test key", evt.Data.Actor);
        Assert.Equal(evt.Id, ctx.Request.Headers["SmartInbox-Event-Id"]);
        Assert.DoesNotContain("legal counsel", body); // thin payload
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private sealed record CreatedEndpoint(string Secret);
}
