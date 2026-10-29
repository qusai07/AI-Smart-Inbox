using Microsoft.Extensions.Logging;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Ai.Rules;

namespace SmartInbox.Infrastructure.Ai;

public class AiOptions
{
    /// <summary>Rules | Ollama | Claude | Hybrid. Hybrid = rule engine first, LLM only when the rules are unsure.</summary>
    public string Provider { get; set; } = "Hybrid";
    /// <summary>The LLM Hybrid escalates to: Ollama (free, local) or Claude (paid).</summary>
    public string Llm { get; set; } = "Ollama";
    /// <summary>Rule results at or above this confidence are used as-is; below it the LLM is asked.</summary>
    public double GateConfidence { get; set; } = 0.75;
}

/// <summary>
/// The cost gate: the free rule engine handles clear-cut documents; only uncertain ones (or scans with no text)
/// are sent to the LLM. If the LLM is unavailable, the rule result is kept so nothing is lost; its low
/// confidence sends the document to human review.
/// </summary>
public class GatedProvider(RuleBasedProvider rules, IAiProvider llm, double gateConfidence, ILogger<GatedProvider> logger) : IAiProvider
{
    public async Task<AiAnalysisResult> AnalyzeAsync(AnalysisInput input, IReadOnlyList<Department> departments, CancellationToken ct = default)
    {
        var ruleResult = rules.Analyze(input, departments);
        if (input.Text is not null && ruleResult.Confidence >= gateConfidence)
            return ruleResult;

        try
        {
            var llmResult = await llm.AnalyzeAsync(input, departments, ct);
            return CrossCheck(ruleResult, llmResult);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("LLM unavailable, keeping the rule-engine result: {Message}", ex.Message);
            return ruleResult with { Reasoning = $"{ruleResult.Reasoning} (AI model unavailable: {ex.Message})" };
        }
    }

    /// <summary>Confidence a disagreement is capped at: below the review threshold, so a human decides.</summary>
    public const double DisagreementConfidence = 0.5;

    /// <summary>
    /// Small local models are often confidently wrong, so the two engines check each other: agreement keeps the
    /// LLM's richer answer with at least the rules' confidence; disagreement goes to human review with both opinions.
    /// </summary>
    private static AiAnalysisResult CrossCheck(AiAnalysisResult rules, AiAnalysisResult llm)
    {
        if (rules.SuggestedDepartmentId == 0) return llm;
        if (rules.SuggestedDepartmentId == llm.SuggestedDepartmentId)
            return llm with { Confidence = Math.Max(llm.Confidence, rules.Confidence) };

        return llm with
        {
            Confidence = Math.Min(llm.Confidence, DisagreementConfidence),
            Reasoning = $"{llm.Reasoning} The rule engine disagreed: {rules.Reasoning}",
        };
    }
}
