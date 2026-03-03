using Acme.TaskHub.Application.Interfaces;
using Acme.TaskHub.Infrastructure.Persistence;
using Acme.TaskHub.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.TaskHub.Infrastructure;

/// <summary>
/// Registers Infrastructure layer services with the DI container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds Infrastructure layer services (EF Core, repositories) to the service collection.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlite(config.GetConnectionString("Default"), sqlite =>
            {
                // TRD §10.1: Default DB command timeout 30s.
                sqlite.CommandTimeout(30);
            });
        });

        services.AddScoped<ITodoItemRepository, TodoItemRepository>();

        return services;
    }
}
