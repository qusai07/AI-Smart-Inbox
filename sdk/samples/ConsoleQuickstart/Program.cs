// Upload a letter, wait for the AI analysis, print it.
//   SMARTINBOX_URL=http://localhost:5080 SMARTINBOX_API_KEY=sik_... dotnet run -- ../../../samples/en-04-court-notice.txt
using SmartInbox.Client;

var inbox = new SmartInboxClient(new SmartInboxClientOptions
{
    BaseUrl = new Uri(Environment.GetEnvironmentVariable("SMARTINBOX_URL") ?? "http://localhost:5080"),
    ApiKey = Environment.GetEnvironmentVariable("SMARTINBOX_API_KEY"),
});

var path = args.FirstOrDefault() ?? throw new ArgumentException("Usage: dotnet run -- <file>");
await using var file = File.OpenRead(path);

var uploaded = await inbox.Documents.UploadAsync(file, Path.GetFileName(path));
Console.WriteLine($"Uploaded {uploaded.Id}, analyzing…");

var doc = await inbox.Documents.WaitForAnalysisAsync(uploaded.Id, TimeSpan.FromMinutes(2));
var departments = (await inbox.Departments.ListAsync()).ToDictionary(d => d.Id, d => d.NameEn);

if (doc.Analysis is not { } a)
{
    Console.WriteLine($"{doc.Status}: {doc.ErrorMessage}");
    return;
}
Console.WriteLine($"""
    Status:     {doc.Status}
    Summary:    {a.SummaryEn}
    Category:   {a.Category} · Priority: {a.Priority}
    Sender:     {a.Sender ?? "-"} · Ref: {a.ReferenceNumber ?? "-"} · Deadline: {a.Deadline?.ToString() ?? "-"}
    Suggested:  {(a.SuggestedDepartmentId is { } id ? departments.GetValueOrDefault(id, $"#{id}") : "-")} ({a.Confidence:P0} confidence)
    Actions:    {string.Join("; ", doc.ActionItems.Select(i => i.Description))}
    """);
