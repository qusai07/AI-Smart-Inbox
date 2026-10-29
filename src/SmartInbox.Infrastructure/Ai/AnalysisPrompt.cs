using System.Text.Encodings.Web;
using System.Text.Json;
using SmartInbox.Domain;

namespace SmartInbox.Infrastructure.Ai;

/// <summary>Prompt text and output schema. Kept free of per-request data so the system prompt caches.</summary>
public static class AnalysisPrompt
{
    private static readonly JsonSerializerOptions DepartmentJson = new()
    {
        WriteIndented = true,
        // Prompt text, not HTML: keep Arabic, apostrophes and "&" readable for the model.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string BuildSystemPrompt(IReadOnlyList<Department> departments)
    {
        var list = departments
            .OrderBy(d => d.Id)
            .Select(d => new { id = d.Id, name_en = d.NameEn, name_ar = d.NameAr, handles = d.Description, keywords = d.Keywords });

        return $$"""
            You are an assistant for a government correspondence office. Each incoming document
            (letter, memo, email, invoice, circular) is analyzed so that a human clerk can route it
            to ONE department quickly. The clerk always makes the final decision, so be accurate
            and honest about uncertainty rather than confident.

            How to fill each field:
            - summary_en / summary_ar: at most 3 sentences each, written for a busy manager. The
              Arabic summary should be natural formal Arabic, not a word-for-word translation.
            - Never invent data. If a field is not present in the document, return null.
            - Dates must be ISO format (YYYY-MM-DD). Convert Hijri dates to Gregorian.
            - priority: Urgent only if an explicit deadline is within 3 days of today's date, or the
              text explicitly says urgent / عاجل / فوري. High for deadlines within 14 days or matters
              with legal or financial exposure. Low for purely informational items such as
              newsletters or thank-you notes. Otherwise Normal.
            - category: Request, Complaint, Invitation, Circular, Invoice, Legal or Other.
            - action_items: concrete things the receiving department must do, with a due date if
              one is stated. Empty list if none.
            - suggested_department_id: must be one of the ids below.
            - confidence: 0.0-1.0, how sure you are about the department. If the document could
              reasonably belong to more than one department, or is ambiguous, keep it below 0.6.
            - reasoning: one or two sentences explaining the department choice, citing the
              document's content.
            - transcription: only when the document is given to you as an image or scanned PDF,
              transcribe its full text in the original language; otherwise null.

            Departments:
            {{JsonSerializer.Serialize(list, DepartmentJson)}}
            """;
    }

    public const string OutputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summary_en", "summary_ar", "category", "priority", "language", "sender", "recipient",
                       "reference_number", "document_date", "deadline", "action_items",
                       "suggested_department_id", "confidence", "reasoning", "transcription"],
          "properties": {
            "summary_en": { "type": "string" },
            "summary_ar": { "type": "string" },
            "category": { "type": "string", "enum": ["Request", "Complaint", "Invitation", "Circular", "Invoice", "Legal", "Other"] },
            "priority": { "type": "string", "enum": ["Low", "Normal", "High", "Urgent"] },
            "language": { "type": "string", "enum": ["ar", "en", "mixed"] },
            "sender": { "type": ["string", "null"] },
            "recipient": { "type": ["string", "null"] },
            "reference_number": { "type": ["string", "null"] },
            "document_date": { "type": ["string", "null"], "description": "YYYY-MM-DD" },
            "deadline": { "type": ["string", "null"], "description": "YYYY-MM-DD" },
            "action_items": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["description", "due_date"],
                "properties": {
                  "description": { "type": "string" },
                  "due_date": { "type": ["string", "null"], "description": "YYYY-MM-DD" }
                }
              }
            },
            "suggested_department_id": { "type": "integer" },
            "confidence": { "type": "number" },
            "reasoning": { "type": "string" },
            "transcription": { "type": ["string", "null"] }
          }
        }
        """;
}
