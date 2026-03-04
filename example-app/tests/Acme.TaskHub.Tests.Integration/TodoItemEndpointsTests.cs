using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.TaskHub.Application.Models;
using Acme.TaskHub.Domain.Enums;
using Acme.TaskHub.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Acme.TaskHub.Tests.Integration;

/// <summary>
/// Integration tests for the /v1/todoitems endpoints — full CRUD lifecycle.
/// </summary>
/// <remarks>
/// Implements <see cref="IAsyncLifetime"/> so <see cref="InitializeAsync"/> truncates the
/// TodoItems table before each test (TRD §13.3). xUnit creates a new class instance per test,
/// so <see cref="InitializeAsync"/> effectively runs per test, giving clean isolated state
/// without requiring a separate factory per test.
/// </remarks>
public class TodoItemEndpointsTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TodoItemEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>Truncates the TodoItems table before each test for isolation (TRD §13.3).</summary>
    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TodoItems.RemoveRange(db.TodoItems);
        await db.SaveChangesAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_returns_201_and_body()
    {
        var request = new { Title = "Integration test item", Priority = "High" };

        var response = await _client.PostAsJsonAsync("/v1/todoitems", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var body = await response.Content.ReadFromJsonAsync<TodoItemResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Title.Should().Be("Integration test item");
        body.Priority.Should().Be(Priority.High);
        body.Status.Should().Be(TodoStatus.Pending);
    }

    [Fact]
    public async Task Create_validation_returns_400()
    {
        var request = new { Title = (string?)null };

        var response = await _client.PostAsJsonAsync("/v1/todoitems", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetById_returns_created_item()
    {
        var created = await CreateItemAsync("Get by id test");

        var response = await _client.GetAsync($"/v1/todoitems/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TodoItemResponse>(JsonOptions);
        body!.Title.Should().Be("Get by id test");
    }

    [Fact]
    public async Task GetById_returns_404_for_unknown()
    {
        var response = await _client.GetAsync($"/v1/todoitems/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAll_returns_paged_result()
    {
        // Table is empty before this test (InitializeAsync) so counts are exact.
        await CreateItemAsync("Paged item 1");
        await CreateItemAsync("Paged item 2");

        var response = await _client.GetAsync("/v1/todoitems?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<TodoItemResponse>>(JsonOptions);
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(2);
        body.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAll_filters_by_status()
    {
        var created = await CreateItemAsync("Filter test");

        // Transition to InProgress.
        await _client.PatchAsJsonAsync(
            $"/v1/todoitems/{created.Id}/status",
            new { Status = "InProgress" },
            JsonOptions);

        var response = await _client.GetAsync("/v1/todoitems?status=InProgress");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResult<TodoItemResponse>>(JsonOptions);
        body!.Items.Should().ContainSingle(x => x.Id == created.Id);
    }

    [Fact]
    public async Task Update_returns_updated_item()
    {
        var created = await CreateItemAsync("Before update");

        var updateRequest = new { Title = "After update", Priority = "Critical" };
        var response = await _client.PutAsJsonAsync($"/v1/todoitems/{created.Id}", updateRequest, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TodoItemResponse>(JsonOptions);
        body!.Title.Should().Be("After update");
        body.Priority.Should().Be(Priority.Critical);
    }

    [Fact]
    public async Task Update_returns_404_for_unknown()
    {
        var updateRequest = new { Title = "Ghost" };
        var response = await _client.PutAsJsonAsync($"/v1/todoitems/{Guid.NewGuid()}", updateRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateStatus_transitions_correctly()
    {
        var created = await CreateItemAsync("Status test");

        var response = await _client.PatchAsJsonAsync(
            $"/v1/todoitems/{created.Id}/status",
            new { Status = "InProgress" },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TodoItemResponse>(JsonOptions);
        body!.Status.Should().Be(TodoStatus.InProgress);
    }

    [Fact]
    public async Task UpdateStatus_invalid_transition_returns_409()
    {
        var created = await CreateItemAsync("Conflict test");

        // Complete the item.
        await _client.PatchAsJsonAsync(
            $"/v1/todoitems/{created.Id}/status",
            new { Status = "Completed" },
            JsonOptions);

        // Try to move back to Pending (invalid).
        var response = await _client.PatchAsJsonAsync(
            $"/v1/todoitems/{created.Id}/status",
            new { Status = "Pending" },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Delete_returns_204()
    {
        var created = await CreateItemAsync("Delete me");

        var response = await _client.DeleteAsync($"/v1/todoitems/{created.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deleted.
        var getResponse = await _client.GetAsync($"/v1/todoitems/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_returns_404_for_unknown()
    {
        var response = await _client.DeleteAsync($"/v1/todoitems/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Helper to create a to-do item and return the response.
    /// </summary>
    private async Task<TodoItemResponse> CreateItemAsync(string title)
    {
        var request = new { Title = title };
        var response = await _client.PostAsJsonAsync("/v1/todoitems", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TodoItemResponse>(JsonOptions);
        return body!;
    }
}
