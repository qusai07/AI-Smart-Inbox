using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.Infrastructure.Ai;

/// <summary>The JSON shape defined by <see cref="AnalysisPrompt.OutputSchema"/>; shared by every LLM provider.</summary>
internal sealed record AiJsonOutput(
    [property: JsonPropertyName("summary_en")] string SummaryEn,
    [property: JsonPropertyName("summary_ar")] string SummaryAr,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("sender")] string? Sender,
    [property: JsonPropertyName("recipient")] string? Recipient,
    [property: JsonPropertyName("reference_number")] string? ReferenceNumber,
    [property: JsonPropertyName("document_date")] string? DocumentDate,
    [property: JsonPropertyName("deadline")] string? Deadline,
    [property: JsonPropertyName("action_items")] List<AiJsonActionItem>? ActionItems,
    [property: JsonPropertyName("suggested_department_id")] int SuggestedDepartmentId,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("reasoning")] string? Reasoning,
    [property: JsonPropertyName("transcription")] string? Transcription)
{
    public static AiJsonOutput Parse(string json) =>
        JsonSerializer.Deserialize<AiJsonOutput>(json) ?? throw new InvalidOperationException("Empty AI response.");

    public AiAnalysisResult ToResult(string model, int tokens) => new(
        SummaryEn,
        SummaryAr,
        Enum.TryParse<DocumentCategory>(Category, true, out var c) ? c : DocumentCategory.Other,
        Enum.TryParse<Domain.Priority>(Priority, true, out var p) ? p : Domain.Priority.Normal,
        Language,
        Sender,
        Recipient,
        ReferenceNumber,
        ParseDate(DocumentDate),
        ParseDate(Deadline),
        (ActionItems ?? []).Select(a => new AiActionItem(a.Description, ParseDate(a.DueDate))).ToList(),
        SuggestedDepartmentId,
        Confidence,
        Reasoning ?? "",
        model,
        tokens,
        string.IsNullOrWhiteSpace(Transcription) ? null : Transcription);

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

internal sealed record AiJsonActionItem(
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("due_date")] string? DueDate);
