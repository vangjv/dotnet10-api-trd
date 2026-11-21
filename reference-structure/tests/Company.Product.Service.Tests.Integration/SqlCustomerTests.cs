using System.Net;
using System.Net.Http.Json;
using Company.Product.Service.Domain;
using Company.Product.Service.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Company.Product.Service.Tests.Integration;

public sealed class SqlCustomerTests
{
    [SqlServerFact]
    public async Task Migration_readiness_and_customer_rename_work_against_sql_server()
    {
        var connection = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("REFERENCE_TEST_SQL_SERVER"))
        {
            InitialCatalog = $"ReferenceTests_{Guid.NewGuid():N}",
        };
        using var factory = new ApiFactory(connection.ConnectionString);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        try
        {
            await database.Database.MigrateAsync();
            database.Customers.Add(new Customer(id, "Before"));
            await database.SaveChangesAsync();

            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

            client.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.CurrentKey);
            using var renamed = await client.PutAsJsonAsync(
                $"/v1/customers/{id}/display-name", new { displayName = " After " });
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            var customer = await client.GetFromJsonAsync<CustomerSnapshot>($"/v1/customers/{id}");
            Assert.Equal(new CustomerSnapshot(id, "After"), customer);

            using var missing = await client.GetAsync($"/v1/customers/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }

    private sealed record CustomerSnapshot(Guid Id, string DisplayName);
}

internal sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REFERENCE_TEST_SQL_SERVER")))
            Skip = "Set REFERENCE_TEST_SQL_SERVER to run against a disposable SQL Server test database.";
    }
}
