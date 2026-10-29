using System.Globalization;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.Infrastructure.Ai;

public class ClaudeOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-sonnet-5-5";
    public int MaxTokens { get; set; } = 16000;
    /// <summary>low | medium | high. Routing letters is not hard reasoning; medium is plenty.</summary>
    public string Effort { get; set; } = "medium";
}

public class ClaudeProvider(AnthropicClient client, IOptions<ClaudeOptions> options, TimeProvider clock, ILogger<ClaudeProvider> logger)
    : IAiProvider
{
    private static readonly Dictionary<string, JsonElement> Schema =
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(AnalysisPrompt.OutputSchema)!;

    private readonly ClaudeOptions _options = options.Value;

    public async Task<AiAnalysisResult> AnalyzeAsync(AnalysisInput input, IReadOnlyList<Department> departments, CancellationToken ct = default)
    {
        var today = clock.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        BetaMessage response;
        try
        {
            response = await SendAsync(input, departments, today, ct);
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw new AiException(
                "The AI is not configured: no valid Anthropic API key. Set Claude:ApiKey (user-secrets) or ANTHROPIC_API_KEY, restart, then re-analyze.", ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new AiException("The AI service is busy (rate limit). Please re-analyze in a minute.", ex);
        }

        if (response.StopReason == "refusal")
            throw new AiRefusalException("The AI declined to analyze this document. Please review it manually.");
        if (response.StopReason == "max_tokens")
            throw new InvalidOperationException("AI response was truncated (max_tokens).");

        var json = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));

        var usage = response.Usage;
        logger.LogInformation(
            "Claude analysis: model={Model} in={In} out={Out} cache_read={CacheRead} cache_write={CacheWrite}",
            response.Model, usage.InputTokens, usage.OutputTokens, usage.CacheReadInputTokens, usage.CacheCreationInputTokens);

        return AiJsonOutput.Parse(json).ToResult(
            response.Model.ToString(),
            (int)(usage.InputTokens + (usage.CacheReadInputTokens ?? 0) + (usage.CacheCreationInputTokens ?? 0) + usage.OutputTokens));
    }

    private Task<BetaMessage> SendAsync(AnalysisInput input, IReadOnlyList<Department> departments, string today, CancellationToken ct) =>
        client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            // Refusals are re-served by a fallback model inside the same call.
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            // Static instructions + department list first, cached; per-request data goes in the user turn.
            System = new List<BetaTextBlockParam>
            {
                new() { Text = AnalysisPrompt.BuildSystemPrompt(departments), CacheControl = new BetaCacheControlEphemeral() },
            },
            OutputConfig = new BetaOutputConfig
            {
                Effort = _options.Effort switch { "low" => Effort.Low, "high" => Effort.High, _ => Effort.Medium },
                Format = new BetaJsonOutputFormat { Schema = Schema },
            },
            Messages = [new BetaUserMessageParam { Content = BuildContent(input, today) }],
        }, ct);

    private static List<BetaContentBlockParam> BuildContent(AnalysisInput input, string today)
    {
        var blocks = new List<BetaContentBlockParam>();

        if (input.FileBytes is { } bytes)
        {
            var data = Convert.ToBase64String(bytes);
            blocks.Add(input.MediaType == "application/pdf"
                ? new BetaRequestDocumentBlock { Source = new BetaBase64PdfSource { Data = data } }
                : new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = data, MediaType = input.MediaType! } });
            blocks.Add(new BetaTextBlockParam { Text = $"Today's date: {today}\n\nAnalyze the attached document." });
        }
        else
        {
            blocks.Add(new BetaTextBlockParam
            {
                Text = $"Today's date: {today}\n\nAnalyze this document:\n<document>\n{input.Text}\n</document>",
            });
        }

        return blocks;
    }
}
