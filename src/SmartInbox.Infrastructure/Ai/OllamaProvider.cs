using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.Infrastructure.Ai;

public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    /// <summary>Any model pulled with `ollama pull`. qwen2.5 handles Arabic well; llava/qwen2.5vl can read images.</summary>
    public string Model { get; set; } = "qwen2.5:3b";
    public int TimeoutSeconds { get; set; } = 180;
}

/// <summary>Free local LLM through Ollama's /api/chat with a JSON-schema constrained response.</summary>
public class OllamaProvider : IAiProvider
{
    private static readonly JsonElement Schema = JsonDocument.Parse(AnalysisPrompt.OutputSchema).RootElement.Clone();

    // A local model works on one request at a time; queue here so the timeout measures generation, not waiting.
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<OllamaProvider> _logger;

    public OllamaProvider(IOptions<OllamaOptions> options, TimeProvider clock, ILogger<OllamaProvider> logger)
    {
        _options = options.Value;
        _clock = clock;
        _logger = logger;
        _http = new HttpClient { BaseAddress = new Uri(_options.BaseUrl), Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds) };
    }

    public async Task<AiAnalysisResult> AnalyzeAsync(AnalysisInput input, IReadOnlyList<Department> departments, CancellationToken ct = default)
    {
        if (input.Text is null && input.MediaType == "application/pdf")
            throw new AiException("This is a scanned PDF. The local model can't read PDFs directly; route it manually or use the Claude provider.");

        var today = _clock.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var user = new OllamaMessage("user", input.Text is null
            ? $"Today's date: {today}\n\nAnalyze the attached document image."
            : $"Today's date: {today}\n\nAnalyze this document:\n<document>\n{input.Text}\n</document>",
            input.FileBytes is { } bytes ? [Convert.ToBase64String(bytes)] : null);

        var request = new OllamaChatRequest(
            _options.Model,
            [new OllamaMessage("system", AnalysisPrompt.BuildSystemPrompt(departments) + "\n\nRespond with JSON only."), user],
            Stream: false,
            Format: Schema,
            Options: new Dictionary<string, object> { ["temperature"] = 0 });

        HttpResponseMessage response;
        await _oneAtATime.WaitAsync(ct);
        try
        {
            response = await _http.PostAsJsonAsync("/api/chat", request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AiException($"The local AI (Ollama) is not reachable at {_options.BaseUrl}. Start Ollama, then re-analyze.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AiException($"The local AI took longer than {_options.TimeoutSeconds}s. Try a smaller model or re-analyze.", ex);
        }
        finally
        {
            _oneAtATime.Release();
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new AiException($"The local model '{_options.Model}' is not installed. Run: ollama pull {_options.Model}");
        if (!response.IsSuccessStatusCode)
            throw new AiException($"The local AI returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");

        var body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(ct)
                   ?? throw new InvalidOperationException("Empty Ollama response.");
        _logger.LogInformation("Ollama analysis: model={Model} in={In} out={Out}", body.Model, body.PromptEvalCount, body.EvalCount);

        return AiJsonOutput.Parse(body.Message.Content).ToResult(body.Model ?? _options.Model, body.PromptEvalCount + body.EvalCount);
    }

    private sealed record OllamaMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("images"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Images = null);

    private sealed record OllamaChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] OllamaMessage[] Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("format")] JsonElement Format,
        [property: JsonPropertyName("options")] Dictionary<string, object> Options);

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("message")] OllamaMessage Message,
        [property: JsonPropertyName("prompt_eval_count")] int PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int EvalCount);
}
