using Acme.TaskHub.Application.Interfaces;
using Acme.TaskHub.Infrastructure.Persistence;
using Acme.TaskHub.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Acme.TaskHub.Infrastructure;

/// <summary>
/// Registers Infrastructure layer services with the DI container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds Infrastructure layer services (EF Core, repositories, health checks) to the service collection.
    /// </summary>
    /// <remarks>
    /// Encapsulates all infrastructure concerns so that the composition root (Program.cs)
    /// does not need to reference infrastructure types such as <see cref="AppDbContext"/> directly
    /// (TRD §5.3, §5.6).
    /// </remarks>
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

        // Health check — gates /health/ready on database connectivity (TRD §14.1).
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database");

        return services;
    }

    /// <summary>
    /// Initializes the database for the current environment.
    /// In Development, calls <c>MigrateAsync</c> to apply pending migrations and create the
    /// database if absent (TRD §11.2). In all other environments this is a no-op; migrations
    /// are applied via the controlled deployment pipeline.
    /// </summary>
    /// <remarks>
    /// <c>EnsureCreated</c> and EF Core migrations are mutually exclusive — <c>EnsureCreated</c>
    /// bypasses migration history and must not be used once any migration has been created.
    /// </remarks>
    /// <param name="host">The application host.</param>
    public static async Task InitializeDatabaseAsync(this IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        using var scope = host.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();

        if (env.IsDevelopment())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync().ConfigureAwait(false);
        }
    }
}
