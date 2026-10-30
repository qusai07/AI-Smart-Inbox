namespace SmartInbox.Client;

/// <summary>Every API route the client calls. A contract test checks each one against the server's OpenAPI document.</summary>
internal static class Routes
{
    public const string Documents = "api/documents";
    public static string Document(Guid id) => $"api/documents/{id}";
    public static string DocumentFile(Guid id) => $"api/documents/{id}/file";
    public static string DocumentAction(Guid id, string action) => $"api/documents/{id}/{action}";
    public const string Departments = "api/departments";
    public const string Workload = "api/departments/workload";
    public static string Members(int departmentId) => $"api/departments/{departmentId}/members";
    public const string Stats = "api/stats";
    public const string ReplyTemplates = "api/reply-templates";
    public static string DocumentDrafts(Guid documentId) => $"api/documents/{documentId}/drafts";
    public static string Draft(Guid draftId) => $"api/drafts/{draftId}";
    public static string DraftAction(Guid draftId, string action) => $"api/drafts/{draftId}/{action}";

    /// <summary>(method, OpenAPI path template) pairs, for the contract test.</summary>
    public static readonly (string Method, string Template)[] All =
    [
        ("POST", "/api/documents"),
        ("GET", "/api/documents"),
        ("GET", "/api/documents/{id}"),
        ("GET", "/api/documents/{id}/file"),
        ("POST", "/api/documents/{id}/reanalyze"),
        ("POST", "/api/documents/{id}/route"),
        ("POST", "/api/documents/{id}/acknowledge"),
        ("POST", "/api/documents/{id}/close"),
        ("POST", "/api/documents/{id}/reopen"),
        ("POST", "/api/documents/{id}/assign"),
        ("POST", "/api/documents/{id}/comments"),
        ("GET", "/api/departments/{id}/members"),
        ("GET", "/api/departments"),
        ("GET", "/api/departments/workload"),
        ("GET", "/api/stats"),
        ("GET", "/api/reply-templates"),
        ("POST", "/api/documents/{id}/drafts"),
        ("GET", "/api/documents/{id}/drafts"),
        ("GET", "/api/drafts/{draftId}"),
        ("PUT", "/api/drafts/{draftId}"),
        ("POST", "/api/drafts/{draftId}/approve"),
        ("POST", "/api/drafts/{draftId}/reject"),
        ("GET", "/api/drafts/{draftId}/docx"),
    ];
}
