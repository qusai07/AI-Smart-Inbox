using FluentValidation;

namespace SmartInbox.Application;

public class RouteRequestValidator : AbstractValidator<RouteRequest>
{
    public RouteRequestValidator()
    {
        RuleFor(x => x.DepartmentId).GreaterThan(0);
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public class CloseRequestValidator : AbstractValidator<CloseRequest>
{
    public CloseRequestValidator() => RuleFor(x => x.Note).MaximumLength(1000);
}

public static class UploadRules
{
    public const long MaxFileBytes = 20 * 1024 * 1024;
    public const int MaxPastedTextLength = 100_000;

    public static readonly IReadOnlyDictionary<string, string> AllowedExtensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".eml"] = "message/rfc822",
        [".txt"] = "text/plain",
    };

    /// <summary>Content type is derived from the extension, never trusted from the client.</summary>
    public static string? ResolveContentType(string fileName) =>
        AllowedExtensions.TryGetValue(Path.GetExtension(fileName), out var type) ? type : null;
}
