using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Company.Product.Service.Infrastructure.Persistence;

internal sealed class SqlReadinessCheck(AppDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await database.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("SQL Server is unavailable.");
}
