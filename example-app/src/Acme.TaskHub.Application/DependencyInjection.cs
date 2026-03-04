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
        // Register the system clock abstraction so it can be injected into services
        // that pass it to domain entities. Tests can replace this with FakeTimeProvider.
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<ITodoItemService, TodoItemService>();

        // Register all FluentValidation validators from this assembly.
        services.AddValidatorsFromAssemblyContaining<TodoItemService>();

        return services;
    }
}
