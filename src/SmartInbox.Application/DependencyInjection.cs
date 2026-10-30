using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace SmartInbox.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<DocumentService>();
        services.AddScoped<DocumentProcessor>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<ReminderService>();
        services.AddScoped<CollaborationService>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<IValidator<CreateApiKeyRequest>, CreateApiKeyRequestValidator>();
        services.AddScoped<ReplyDraftService>();
        services.AddSingleton(_ => new ReplyTemplateCatalog()); // explicit: DI would pick the IEnumerable constructor with an empty list
        services.AddScoped<IReplyDrafter, TemplateReplyDrafter>();
        services.AddScoped<ReplyDraftGenerator>();
        services.AddScoped<IValidator<RequestDraftRequest>, RequestDraftRequestValidator>();
        services.AddScoped<IValidator<UpdateDraftRequest>, UpdateDraftRequestValidator>();
        services.AddScoped<IValidator<RejectDraftRequest>, RejectDraftRequestValidator>();
        services.AddScoped<IWebhookPublisher, WebhookPublisher>();
        services.AddScoped<WebhookDispatcher>();
        services.AddScoped<WebhookDeadlineScanner>();
        services.AddScoped<WebhookAdminService>();
        services.AddScoped<IValidator<WebhookEndpointRequest>, WebhookEndpointRequestValidator>();
        services.AddScoped<IValidator<CommentRequest>, CommentRequestValidator>();
        services.AddScoped<IValidator<RouteRequest>, RouteRequestValidator>();
        services.AddScoped<IValidator<CloseRequest>, CloseRequestValidator>();
        return services;
    }
}
