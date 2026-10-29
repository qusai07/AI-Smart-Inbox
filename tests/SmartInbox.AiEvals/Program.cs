// AI evals: runs the labeled samples through the analysis engine and reports accuracy per field.
//
//   dotnet run --project tests/SmartInbox.AiEvals -- --provider Rules|Ollama|Claude|Hybrid [--samples <dir>] [--out <file>]
//
// Rules and Ollama are free. Claude needs Claude:ApiKey (user-secrets of SmartInbox.Api) or ANTHROPIC_API_KEY and costs tokens.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartInbox.Application;
using SmartInbox.Infrastructure;
using SmartInbox.Infrastructure.Persistence;
using SmartInbox.Infrastructure.TextExtraction;

var root = FindRepoRoot();
var samplesDir = ArgValue("--samples") ?? Path.Combine(root, "samples");
var provider = ArgValue("--provider");
var outFile = ArgValue("--out") ?? Path.Combine(root, "docs", provider is null ? "EVALS.md" : $"EVALS-{provider.ToLowerInvariant()}.md");

var labels = JsonSerializer.Deserialize<LabelFile>(
    await File.ReadAllTextAsync(Path.Combine(samplesDir, "labels.json")),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
var evalDate = DateOnly.ParseExact(labels.EvalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);

var config = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(root, "src", "SmartInbox.Api", "appsettings.json"), optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .AddInMemoryCollection(provider is null ? [] : [new("Ai:Provider", provider)])
    .Build();

var services = new ServiceCollection()
    .AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning))
    .AddAiProvider(config)
    // Priorities depend on "today": pin it so results are reproducible.
    .AddSingleton<TimeProvider>(new FixedTimeProvider(evalDate))
    .BuildServiceProvider();

var ai = services.GetRequiredService<IAiProvider>();
var model = SmartInbox.Infrastructure.DependencyInjection.Describe(config);
ITextExtractor[] extractors = [new PlainTextExtractor(), new EmlTextExtractor(), new PdfTextExtractor(), new DocxTextExtractor()];
var departments = DepartmentSeed.All;

Console.WriteLine($"Running {labels.Documents.Count} samples against {model} (as of {evalDate:yyyy-MM-dd})…\n");

var results = new List<EvalResult>();
await Parallel.ForEachAsync(labels.Documents, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (label, ct) =>
{
    var path = Path.Combine(samplesDir, label.File);
    var contentType = UploadRules.ResolveContentType(path) ?? "text/plain";
    try
    {
        await using var stream = File.OpenRead(path);
        var text = await extractors.First(e => e.CanHandle(contentType, path)).ExtractAsync(stream, ct);
        var r = await ai.AnalyzeAsync(AnalysisInput.FromText(text!), departments, ct);
        lock (results) results.Add(new EvalResult(label, r.Category.ToString(), r.Priority.ToString(), r.SuggestedDepartmentId, r.Confidence, r.TokensUsed, r.ModelUsed, null));
    }
    catch (Exception ex)
    {
        lock (results) results.Add(new EvalResult(label, null, null, null, null, 0, null, ex.Message));
    }
});

results = results.OrderBy(r => r.Label.File).ToList();

double Pct(Func<EvalResult, bool> ok) => results.Count == 0 ? 0 : results.Count(ok) * 100.0 / results.Count;
var deptAcc = Pct(r => r.DepartmentId == r.Label.DepartmentId);
var catAcc = Pct(r => r.Category == r.Label.Category);
var priAcc = Pct(r => r.Priority == r.Label.Priority);
// A wrong department is fine if the AI flagged itself as unsure: the document goes to human review anyway.
var safeRouting = Pct(r => r.DepartmentId == r.Label.DepartmentId || r.Confidence < DocumentProcessor.ConfidenceThreshold);
var errors = results.Count(r => r.Error is not null);

var md = new StringBuilder();
md.AppendLine("# AI Evals");
md.AppendLine();
var engines = string.Join(", ", results.Where(r => r.Model is not null).GroupBy(r => r.Model).Select(g => $"`{g.Key}` ×{g.Count()}"));
md.AppendLine($"Engine: **{model}** · {results.Count} synthetic documents (EN + AR) · run {DateTime.UtcNow:yyyy-MM-dd} · \"today\" pinned to {evalDate:yyyy-MM-dd}");
md.AppendLine();
md.AppendLine($"Answered by: {engines}");
md.AppendLine();
md.AppendLine("| Field | Accuracy |");
md.AppendLine("|---|---|");
md.AppendLine($"| Department (routing) | **{deptAcc:0}%** |");
md.AppendLine($"| Category | {catAcc:0}% |");
md.AppendLine($"| Priority | {priAcc:0}% |");
md.AppendLine($"| Safe routing (correct, or flagged < {DocumentProcessor.ConfidenceThreshold:0.0} confidence) | {safeRouting:0}% |");
md.AppendLine();
md.AppendLine($"Average tokens per document: {(results.Count == 0 ? 0 : results.Average(r => r.Tokens)):0}. Errors: {errors}.");
md.AppendLine();
md.AppendLine("<details><summary>Per-document results</summary>");
md.AppendLine();
md.AppendLine("| Document | Department (expected → got, confidence) | Category | Priority |");
md.AppendLine("|---|---|---|---|");
foreach (var r in results)
{
    if (r.Error is not null)
    {
        md.AppendLine($"| {r.Label.File} | ❌ error: {r.Error.Replace("|", "/")} | | |");
        continue;
    }
    md.AppendLine($"| {r.Label.File} " +
                  $"| {Mark(r.DepartmentId == r.Label.DepartmentId)} {r.Label.DepartmentId} → {r.DepartmentId} ({r.Confidence:0.00}) " +
                  $"| {Mark(r.Category == r.Label.Category)} {r.Label.Category} → {r.Category} " +
                  $"| {Mark(r.Priority == r.Label.Priority)} {r.Label.Priority} → {r.Priority} |");
}
md.AppendLine();
md.AppendLine("</details>");

Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
await File.WriteAllTextAsync(outFile, md.ToString());
Console.WriteLine(md.ToString());
Console.WriteLine($"Written to {outFile}");
return errors == results.Count ? 1 : 0;

static string Mark(bool ok) => ok ? "✅" : "❌";

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartInbox.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

static string? ArgValue(string name)
{
    var args = Environment.GetCommandLineArgs();
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

record LabelFile(string EvalDate, List<Label> Documents);
record Label(string File, string Category, string Priority, int DepartmentId);
record EvalResult(Label Label, string? Category, string? Priority, int? DepartmentId, double? Confidence, int Tokens, string? Model, string? Error);

class FixedTimeProvider(DateOnly date) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
}
