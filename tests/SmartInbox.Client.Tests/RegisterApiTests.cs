using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartInbox.Client.Tests;

/// <summary>The outgoing register through the real API (the SDK gets a Register client in 0.5.0).</summary>
[Collection(ServerCollection.Name)]
public class RegisterApiTests(SmartInboxFactory server)
{
    private async Task<HttpClient> HttpAsync(string role, int? departmentId = null)
    {
        var (_, key) = await server.CreateKeyAsync(role, departmentId);
        var http = server.CreateClient(new() { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return http;
    }

    [Fact]
    public async Task Issue_Find_MarkSent_Cancel_Export()
    {
        using var desk = await HttpAsync("Clerk");
        var created = await desk.PostAsJsonAsync("api/register",
            new { departmentId = 3, recipient = "Nimbus Cloud Services", subjectEn = "Invoice NCS-88213 query", subjectAr = "استفسار عن الفاتورة" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var letter = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = letter.GetProperty("id").GetGuid();
        var number = letter.GetProperty("number").GetString()!;
        Assert.Matches(@"^MDS/OUT/\d{4}/\d{4}$", number);

        var found = await desk.GetFromJsonAsync<JsonElement>($"api/register?q={Uri.EscapeDataString("NCS-88213")}");
        Assert.Contains(found.GetProperty("items").EnumerateArray(), l => l.GetProperty("id").GetGuid() == id);

        var sent = await desk.PostAsJsonAsync($"api/register/{id}/sent", new { via = "Email", note = "accounts@nimbus.example" });
        Assert.Equal("Sent", (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var noReason = await desk.PostAsJsonAsync($"api/register/{id}/cancel", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var cancelled = await desk.PostAsJsonAsync($"api/register/{id}/cancel", new { reason = "Sent to the wrong address" });
        Assert.Equal("Cancelled", (await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await desk.PostAsJsonAsync($"api/register/{id}/cancel", new { reason = "again" })).StatusCode);

        var csv = await desk.GetAsync("api/register/export.csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var text = await csv.Content.ReadAsStringAsync();
        Assert.Contains(number, text);
        Assert.Contains("Sent to the wrong address", text);
        Assert.Contains("استفسار عن الفاتورة", text);
    }

    [Fact]
    public async Task DepartmentStaff_OwnDepartmentOnly_AndCantCancel()
    {
        using var desk = await HttpAsync("Clerk");
        var hrLetter = await (await desk.PostAsJsonAsync("api/register",
            new { departmentId = 4, recipient = "Applicant", subjectEn = "Your application" })).Content.ReadFromJsonAsync<JsonElement>();

        using var legal = await HttpAsync("Department", departmentId: 2);
        Assert.Equal(HttpStatusCode.Forbidden, (await legal.PostAsJsonAsync("api/register",
            new { departmentId = 4, recipient = "Applicant", subjectEn = "x" })).StatusCode);
        var own = await legal.PostAsJsonAsync("api/register", new { departmentId = 2, recipient = "Demo City Court", subjectEn = "Defence memorandum" });
        Assert.Equal(HttpStatusCode.Created, own.StatusCode);
        var ownId = (await own.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var seen = await legal.GetFromJsonAsync<JsonElement>("api/register?pageSize=500");
        Assert.All(seen.GetProperty("items").EnumerateArray(), l => Assert.Equal(2, l.GetProperty("departmentId").GetInt32()));
        Assert.Equal(HttpStatusCode.NotFound, (await legal.GetAsync($"api/register/{hrLetter.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await legal.PostAsJsonAsync($"api/register/{ownId}/cancel", new { reason = "x" })).StatusCode);
    }
}
