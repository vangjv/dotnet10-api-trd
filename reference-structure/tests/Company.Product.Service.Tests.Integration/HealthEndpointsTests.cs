using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Company.Product.Service.Tests.Integration;

public class HealthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Live_health_returns_200()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health/live");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
