using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Company.Product.Service.Tests.Integration;

public sealed class ApiContractTests : IClassFixture<ApiFactory>
{
    private const string CustomerPath = "/v1/customers/8a533ea3-b804-474c-9460-b799c928cd02";
    private readonly ApiFactory _factory;

    public ApiContractTests(ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task Endpoint_without_authorization_metadata_requires_a_key(string? key)
    {
        using var client = _factory.CreateClient();
        if (key is not null)
            client.DefaultRequestHeaders.Add("X-Api-Key", key);
        using var response = await client.GetAsync(CustomerPath);
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_rotation_keys_reach_async_validation(bool useNextKey)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", useNextKey ? ApiFactory.NextKey : ApiFactory.CurrentKey);
        using var response = await client.PutAsJsonAsync(CustomerPath + "/display-name", new { displayName = "   " });
        var body = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_request");
        Assert.True(body.GetProperty("errors").TryGetProperty("displayName", out _));
    }

    [Fact]
    public async Task Liveness_ignores_an_unavailable_database_but_readiness_does_not()
    {
        using var client = _factory.CreateClient();
        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task Development_openapi_documents_the_versioned_operations()
    {
        using var client = _factory.CreateClient();
        var schema = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = schema.GetProperty("paths");
        Assert.True(paths.GetProperty("/v1/customers/{customerId}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/v1/customers/{customerId}/display-name").TryGetProperty("put", out _));
        var security = schema.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal("X-Api-Key", security.GetProperty("name").GetString());
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("errorCode").GetString());
        Assert.Matches("^[a-f0-9]{32}$", body.GetProperty("traceId").GetString()!);
        return body;
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    internal static readonly string CurrentKey = new('a', 64);
    internal static readonly string NextKey = new('b', 64);
    internal const string UnavailableDatabase = "Server=tcp:127.0.0.1,1;Database=Unavailable;Integrated Security=true;Connect Timeout=1";
    private readonly string _connectionString;

    public ApiFactory() : this(UnavailableDatabase) { }

    internal ApiFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseEnvironment("Development")
        .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ApiKey:Keys:0"] = CurrentKey,
                ["ApiKey:Keys:1"] = NextKey,
                ["ConnectionStrings:Default"] = _connectionString,
            }));
}
