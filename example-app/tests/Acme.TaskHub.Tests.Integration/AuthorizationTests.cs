using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Xunit;

namespace Acme.TaskHub.Tests.Integration;

/// <summary>
/// Verifies that protected endpoints enforce authorization policies by returning
/// <c>401 Unauthorized</c> when no credentials are provided (TRD §8.2, §13.3).
/// Uses <see cref="UnauthenticatedWebApplicationFactory"/> which replaces <c>DevAuthHandler</c>
/// with a handler that always fails, ensuring the auth pipeline is exercised for real.
/// </summary>
public class AuthorizationTests : IClassFixture<UnauthenticatedWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationTests"/> class.
    /// </summary>
    public AuthorizationTests(UnauthenticatedWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task Get_all_without_credentials_returns_401()
    {
        var response = await _client.GetAsync("/v1/todoitems");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_by_id_without_credentials_returns_401()
    {
        var response = await _client.GetAsync($"/v1/todoitems/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_without_credentials_returns_401()
    {
        var response = await _client.PostAsJsonAsync(
            "/v1/todoitems",
            new { Title = "Unauthorized create" },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_without_credentials_returns_401()
    {
        var response = await _client.PutAsJsonAsync(
            $"/v1/todoitems/{Guid.NewGuid()}",
            new { Title = "Unauthorized update" },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patch_status_without_credentials_returns_401()
    {
        var response = await _client.PatchAsJsonAsync(
            $"/v1/todoitems/{Guid.NewGuid()}/status",
            new { Status = "InProgress" },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_without_credentials_returns_401()
    {
        var response = await _client.DeleteAsync($"/v1/todoitems/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_live_is_public_and_returns_200()
    {
        // Liveness endpoint must not require auth — it is polled by the container orchestrator.
        var response = await _client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_ready_is_public_and_returns_200()
    {
        // Readiness endpoint must not require auth — polled by the orchestrator.
        var response = await _client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
