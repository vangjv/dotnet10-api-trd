using Acme.TaskHub.Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.TaskHub.Application;

/// <summary>
/// Registers Application layer services with the DI container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds Application layer services to the service collection.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ITodoItemService, TodoItemService>();

        // Register all FluentValidation validators from this assembly.
        services.AddValidatorsFromAssemblyContaining<TodoItemService>();

        return services;
    }
}
