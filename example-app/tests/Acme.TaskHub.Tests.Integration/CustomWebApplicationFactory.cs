using Acme.TaskHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.TaskHub.Tests.Integration;

/// <summary>
/// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> that replaces the SQLite database
/// with a unique in-memory database per test class to ensure isolation.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Persistent connection that keeps the in-memory SQLite database alive
    /// for the lifetime of the factory.
    /// </summary>
    private readonly SqliteConnection _keepAliveConnection;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomWebApplicationFactory"/> class.
    /// </summary>
    public CustomWebApplicationFactory()
    {
        _keepAliveConnection = new SqliteConnection($"DataSource=IntegrationTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _keepAliveConnection.Open();
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // Remove the existing AppDbContext registration.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            // Pass the connection *object* (not the connection string) so that EF Core
            // reuses the single open SqliteConnection throughout migrations and all test
            // queries. Using the string causes EF Core to open its own separate connections;
            // across the multiple connection open/close cycles that MigrateAsync performs,
            // SQLite shared-cache can leave tables visible to a new connection while the
            // migration-history write from the previous connection hasn't been observed yet,
            // producing "table TodoItems already exists" on the second migration batch.
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(_keepAliveConnection);
            });

            // Schema is created by MigrateAsync in InitializeDatabaseAsync during
            // application startup (DependencyInjection.cs). Do NOT call EnsureCreated
            // here — it conflicts with MigrateAsync when the app runs in Development.
        });
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _keepAliveConnection.Dispose();
        }
    }
}
