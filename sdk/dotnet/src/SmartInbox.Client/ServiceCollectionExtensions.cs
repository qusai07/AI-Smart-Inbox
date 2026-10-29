using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using SmartInbox.Client;

// Extension methods live in the DI namespace so they show up next to the other Add* calls.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="ISmartInboxClient"/>.</summary>
public static class SmartInboxServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ISmartInboxClient"/> as a typed HttpClient. Transient failures (5xx, 408, 429, network) are retried
    /// with backoff for reads only: actions such as routing or closing are never repeated automatically.
    /// </summary>
    public static IHttpClientBuilder AddSmartInboxClient(this IServiceCollection services, Action<SmartInboxClientOptions> configure)
    {
        services.AddOptions<SmartInboxClientOptions>().Configure(configure).Validate(o =>
        {
            o.Validate();
            return true;
        });

        var builder = services.AddHttpClient<ISmartInboxClient, SmartInboxClient>((sp, http) =>
            SmartInboxClient.Configure(http, sp.GetRequiredService<IOptions<SmartInboxClientOptions>>().Value));

        builder.AddResilienceHandler("smart-inbox", pipeline => pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = args => ValueTask.FromResult(
                IsIdempotent(args.Context.GetRequestMessage()) && HttpClientResiliencePredicates.IsTransient(args.Outcome)),
        }));
        return builder;
    }

    private static bool IsIdempotent(HttpRequestMessage? request) =>
        request is not null && (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head
                                || request.Method == HttpMethod.Put || request.Method == HttpMethod.Delete);
}
