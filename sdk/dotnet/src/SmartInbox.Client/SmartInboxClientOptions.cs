namespace SmartInbox.Client;

/// <summary>Where Smart Inbox is and how to authenticate.</summary>
public sealed class SmartInboxClientOptions
{
    /// <summary>The Smart Inbox site, e.g. <c>https://inbox.example.com</c>.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>An API key (<c>sik_...</c>) created in Administration → API keys. The client acts with the key's role.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Timeout for a single HTTP attempt. Default: 100 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    internal void Validate()
    {
        if (BaseUrl is null || !BaseUrl.IsAbsoluteUri) throw new InvalidOperationException("SmartInboxClientOptions.BaseUrl must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(ApiKey) || !ApiKey.StartsWith("sik_", StringComparison.Ordinal))
            throw new InvalidOperationException("SmartInboxClientOptions.ApiKey must be a Smart Inbox API key (sik_...).");
    }
}
