using Company.Product.Service.Application;
using Company.Product.Service.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Company.Product.Service.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration("ConnectionStrings")
            .Validate(options => IsValidConnectionString(options.Default),
                "ConnectionStrings:Default must be a valid SQL Server connection string with a server and database.")
            .ValidateOnStart();
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseSqlServer(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Default));
        services.AddScoped<ICustomerStore, CustomerStore>();
        services.AddHealthChecks().AddCheck<SqlReadinessCheck>(
            "sql-server", tags: ["ready"], timeout: TimeSpan.FromSeconds(2));

        return services;
    }

    private static bool IsValidConnectionString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            var parsed = new SqlConnectionStringBuilder(value);
            return !string.IsNullOrWhiteSpace(parsed.DataSource) && !string.IsNullOrWhiteSpace(parsed.InitialCatalog);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

internal sealed class DatabaseOptions
{
    public string? Default { get; set; }
}
