using Microsoft.Extensions.Options;
using SmartInbox.Application;

namespace SmartInbox.Infrastructure.Storage;

public class StorageOptions
{
    public string Root { get; set; } = "App_Data/uploads";
}

/// <summary>Stores files on disk under yyyy/MM/{guid}{ext}. The stored path never contains user input.</summary>
public class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.Root);

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        var relative = Path.Combine(DateTime.UtcNow.ToString("yyyy"), DateTime.UtcNow.ToString("MM"),
            $"{Guid.NewGuid():N}{Path.GetExtension(fileName).ToLowerInvariant()}");
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        await using var file = File.Create(full);
        await content.CopyToAsync(file, ct);
        return relative.Replace('\\', '/');
    }

    public Stream Open(string storagePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, storagePath));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Invalid storage path.");
        return File.OpenRead(full);
    }
}
