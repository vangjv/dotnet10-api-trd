using System.Net;
using FluentAssertions;
using Xunit;

namespace Acme.TaskHub.Tests.Integration;

/// <summary>
/// Integration tests for the health check endpoints (TRD §3.1, §14.1).
/// </summary>
public class HealthEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointsTests(CustomWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task Live_health_returns_200()
    {
        var resp = await _client.GetAsync("/health/live");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_health_returns_200()
    {
        var resp = await _client.GetAsync("/health/ready");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
