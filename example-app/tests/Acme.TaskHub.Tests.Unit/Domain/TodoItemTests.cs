using Acme.TaskHub.Domain.Entities;
using Acme.TaskHub.Domain.Enums;
using Acme.TaskHub.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace Acme.TaskHub.Tests.Unit.Domain;

/// <summary>
/// Unit tests for <see cref="TodoItem"/> domain entity and its invariants.
/// </summary>
public class TodoItemTests
{
    [Fact]
    public void Constructor_requires_non_empty_id()
    {
        var act = () => new TodoItem(Guid.Empty, "Buy milk");
        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public void Constructor_requires_non_empty_title()
    {
        var act = () => new TodoItem(Guid.NewGuid(), "  ");
        act.Should().Throw<ArgumentException>().WithParameterName("newTitle");
    }

    [Fact]
    public void Constructor_trims_title()
    {
        var item = new TodoItem(Guid.NewGuid(), "  Buy milk  ");
        item.Title.Should().Be("Buy milk");
    }

    [Fact]
    public void Constructor_rejects_title_exceeding_max_length()
    {
        string longTitle = new('A', TodoItem.MaxTitleLength + 1);
        var act = () => new TodoItem(Guid.NewGuid(), longTitle);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_sets_defaults()
    {
        var item = new TodoItem(Guid.NewGuid(), "Test task");

        item.Status.Should().Be(TodoStatus.Pending);
        item.Priority.Should().Be(Priority.Medium);
        item.Description.Should().BeNull();
        item.DueDate.Should().BeNull();
        item.CompletedAtUtc.Should().BeNull();
        item.CreatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Constructor_with_all_parameters()
    {
        var dueDate = DateTimeOffset.UtcNow.AddDays(7);
        var item = new TodoItem(
            Guid.NewGuid(),
            "Important task",
            "Do this thing",
            Priority.High,
            dueDate);

        item.Title.Should().Be("Important task");
        item.Description.Should().Be("Do this thing");
        item.Priority.Should().Be(Priority.High);
        item.DueDate.Should().Be(dueDate);
    }

    [Fact]
    public void Constructor_rejects_past_due_date()
    {
        var pastDate = DateTimeOffset.UtcNow.AddDays(-1);
        var act = () => new TodoItem(Guid.NewGuid(), "Late task", dueDate: pastDate);
        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("DUE_DATE_IN_PAST");
    }

    [Fact]
    public void SetTitle_updates_title()
    {
        var item = new TodoItem(Guid.NewGuid(), "Old title");
        item.SetTitle("New title");
        item.Title.Should().Be("New title");
    }

    [Fact]
    public void SetTitle_rejects_empty()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title");
        var act = () => item.SetTitle("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetDescription_updates_description()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title");
        item.SetDescription("Some description");
        item.Description.Should().Be("Some description");
    }

    [Fact]
    public void SetDescription_clears_with_null()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title", "Initial description");
        item.SetDescription(null);
        item.Description.Should().BeNull();
    }

    [Fact]
    public void SetDescription_clears_with_whitespace_only()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title", "Initial");
        item.SetDescription("   ");
        item.Description.Should().BeNull();
    }

    [Fact]
    public void SetDescription_rejects_exceeding_max_length()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title");
        string longDesc = new('X', TodoItem.MaxDescriptionLength + 1);
        var act = () => item.SetDescription(longDesc);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetPriority_updates_priority()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title");
        item.SetPriority(Priority.Critical);
        item.Priority.Should().Be(Priority.Critical);
    }

    [Fact]
    public void SetPriority_rejects_invalid_value()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title");
        var act = () => item.SetPriority((Priority)99);
        act.Should().Throw<ArgumentException>();
    }

    // ----- Status transition tests -----

    [Fact]
    public void TransitionTo_Pending_to_InProgress_succeeds()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title");
        item.TransitionTo(TodoStatus.InProgress);
        item.Status.Should().Be(TodoStatus.InProgress);
    }

    [Fact]
    public void TransitionTo_InProgress_to_Completed_sets_CompletedAtUtc()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title");
        item.TransitionTo(TodoStatus.InProgress);
        item.TransitionTo(TodoStatus.Completed);

        item.Status.Should().Be(TodoStatus.Completed);
        item.CompletedAtUtc.Should().NotBeNull();
        item.CompletedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TransitionTo_Completed_is_terminal()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title");
        item.TransitionTo(TodoStatus.Completed);

        var act = () => item.TransitionTo(TodoStatus.Pending);
        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("INVALID_STATUS_TRANSITION");
    }

    [Fact]
    public void TransitionTo_Cancelled_can_reopen_to_Pending()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title");
        item.TransitionTo(TodoStatus.Cancelled);
        item.TransitionTo(TodoStatus.Pending);
        item.Status.Should().Be(TodoStatus.Pending);
    }

    [Fact]
    public void TransitionTo_same_status_is_idempotent()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title");
        item.TransitionTo(TodoStatus.Pending); // Same as initial.
        item.Status.Should().Be(TodoStatus.Pending);
    }

    [Theory]
    [InlineData(TodoStatus.Completed, TodoStatus.InProgress)]
    [InlineData(TodoStatus.Completed, TodoStatus.Cancelled)]
    [InlineData(TodoStatus.Cancelled, TodoStatus.InProgress)]
    [InlineData(TodoStatus.Cancelled, TodoStatus.Completed)]
    public void TransitionTo_invalid_transitions_throw(TodoStatus from, TodoStatus to)
    {
        var item = new TodoItem(Guid.NewGuid(), "Title");

        // Navigate to the 'from' state.
        if (from == TodoStatus.InProgress)
        {
            item.TransitionTo(TodoStatus.InProgress);
        }
        else if (from == TodoStatus.Completed)
        {
            item.TransitionTo(TodoStatus.Completed);
        }
        else if (from == TodoStatus.Cancelled)
        {
            item.TransitionTo(TodoStatus.Cancelled);
        }

        var act = () => item.TransitionTo(to);
        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("INVALID_STATUS_TRANSITION");
    }

    [Fact]
    public void TransitionTo_InProgress_to_Pending_clears_CompletedAtUtc()
    {
        var item = new TodoItem(Guid.NewGuid(), "Title");
        item.TransitionTo(TodoStatus.InProgress);

        // CompletedAtUtc should be null since we haven't completed.
        item.CompletedAtUtc.Should().BeNull();

        // Going back to Pending should keep it null.
        item.TransitionTo(TodoStatus.Pending);
        item.CompletedAtUtc.Should().BeNull();
    }
}
