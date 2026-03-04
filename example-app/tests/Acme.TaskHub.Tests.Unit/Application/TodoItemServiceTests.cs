using Acme.TaskHub.Application.Interfaces;
using Acme.TaskHub.Application.Models;
using Acme.TaskHub.Application.Services;
using Acme.TaskHub.Domain.Entities;
using Acme.TaskHub.Domain.Enums;
using Acme.TaskHub.Domain.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Acme.TaskHub.Tests.Unit.Application;

/// <summary>
/// Unit tests for <see cref="TodoItemService"/>.
/// </summary>
public class TodoItemServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2025, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly ITodoItemRepository _repository;
    private readonly ILogger<TodoItemService> _logger;
    private readonly FakeTimeProvider _clock;
    private readonly TodoItemService _sut;

    public TodoItemServiceTests()
    {
        _repository = Substitute.For<ITodoItemRepository>();
        _logger = NullLogger<TodoItemService>.Instance;
        _clock = new FakeTimeProvider(FixedNow);
        _sut = new TodoItemService(_repository, _logger, _clock);
    }

    [Fact]
    public async Task CreateAsync_returns_created_item()
    {
        var request = new CreateTodoItemRequest
        {
            Title = "Write tests",
            Description = "Cover all the things",
            Priority = Priority.High,
        };

        var result = await _sut.CreateAsync(request);

        result.Should().NotBeNull();
        result.Title.Should().Be("Write tests");
        result.Description.Should().Be("Cover all the things");
        result.Priority.Should().Be(Priority.High);
        result.Status.Should().Be(TodoStatus.Pending);
        result.Id.Should().NotBeEmpty();
        result.CreatedAtUtc.Should().Be(FixedNow);

        await _repository.Received(1).AddAsync(
            Arg.Is<TodoItem>(x => x.Title == "Write tests"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_not_found()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TodoItem?)null);

        var result = await _sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_returns_mapped_item()
    {
        var item = new TodoItem(Guid.NewGuid(), "Existing", timeProvider: _clock);
        _repository.GetByIdAsync(item.Id, Arg.Any<CancellationToken>())
            .Returns(item);

        var result = await _sut.GetByIdAsync(item.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(item.Id);
        result.Title.Should().Be("Existing");
    }

    [Fact]
    public async Task GetPagedAsync_clamps_page_size()
    {
        _repository.GetPagedAsync(
            Arg.Any<TodoStatus?>(),
            Arg.Any<Priority?>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns((new List<TodoItem>().AsReadOnly() as IReadOnlyList<TodoItem>, 0));

        var query = new TodoItemQueryParameters { PageSize = 999, Page = 0 };
        var result = await _sut.GetPagedAsync(query);

        result.PageSize.Should().Be(TodoItemQueryParameters.MaxPageSize);
        result.Page.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_returns_null_when_not_found()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TodoItem?)null);

        var result = await _sut.UpdateAsync(Guid.NewGuid(), new UpdateTodoItemRequest { Title = "X" });

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_updates_item()
    {
        var item = new TodoItem(Guid.NewGuid(), "Old Title", timeProvider: _clock);
        _repository.GetByIdAsync(item.Id, Arg.Any<CancellationToken>())
            .Returns(item);

        var request = new UpdateTodoItemRequest
        {
            Title = "New Title",
            Description = "New Desc",
            Priority = Priority.Critical,
        };

        var result = await _sut.UpdateAsync(item.Id, request);

        result.Should().NotBeNull();
        result!.Title.Should().Be("New Title");
        result.Description.Should().Be("New Desc");
        result.Priority.Should().Be(Priority.Critical);

        await _repository.Received(1).UpdateAsync(item, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_throws_when_item_is_completed()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title", timeProvider: _clock);
        item.TransitionTo(TodoStatus.Completed);
        _repository.GetByIdAsync(item.Id, Arg.Any<CancellationToken>())
            .Returns(item);

        var act = async () => await _sut.UpdateAsync(
            item.Id,
            new UpdateTodoItemRequest { Title = "Mutate completed" });

        await act.Should().ThrowAsync<DomainException>()
            .Where(ex => ex.ErrorCode == "ITEM_NOT_MUTABLE");
    }

    [Fact]
    public async Task UpdateStatusAsync_transitions_status()
    {
        var item = new TodoItem(Guid.NewGuid(), "Task", timeProvider: _clock);
        _repository.GetByIdAsync(item.Id, Arg.Any<CancellationToken>())
            .Returns(item);

        var result = await _sut.UpdateStatusAsync(
            item.Id,
            new UpdateTodoStatusRequest { Status = TodoStatus.InProgress });

        result.Should().NotBeNull();
        result!.Status.Should().Be(TodoStatus.InProgress);
    }

    [Fact]
    public async Task DeleteAsync_returns_false_when_not_found()
    {
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.DeleteAsync(Guid.NewGuid());
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_returns_true_when_deleted()
    {
        var id = Guid.NewGuid();
        _repository.DeleteAsync(id, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.DeleteAsync(id);
        result.Should().BeTrue();
    }
}
