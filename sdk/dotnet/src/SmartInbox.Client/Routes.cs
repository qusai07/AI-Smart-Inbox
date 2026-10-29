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
    public const string Stats = "api/stats";

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
        ("GET", "/api/departments"),
        ("GET", "/api/departments/workload"),
        ("GET", "/api/stats"),
    ];
}
