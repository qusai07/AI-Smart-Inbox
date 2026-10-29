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
        services.AddScoped<IValidator<CommentRequest>, CommentRequestValidator>();
        services.AddScoped<IValidator<RouteRequest>, RouteRequestValidator>();
        services.AddScoped<IValidator<CloseRequest>, CloseRequestValidator>();
        return services;
    }
}
