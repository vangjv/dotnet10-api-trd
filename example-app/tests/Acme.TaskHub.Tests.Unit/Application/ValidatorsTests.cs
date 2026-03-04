using Acme.TaskHub.Application.Models;
using Acme.TaskHub.Application.Validators;
using Acme.TaskHub.Domain.Entities;
using Acme.TaskHub.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Acme.TaskHub.Tests.Unit.Application;

// =============================================================================
// CreateTodoItemValidator
// =============================================================================

/// <summary>
/// Unit tests for <see cref="CreateTodoItemValidator"/>.
/// Covers all non-trivial rules: length limits, conditional .When() clauses,
/// enum validation, and time-relative due-date comparison (TRD §13.3).
/// </summary>
public class CreateTodoItemValidatorTests
{
    private readonly CreateTodoItemValidator _sut = new();

    // ----- Title -----

    [Fact]
    public async Task Valid_minimal_request_passes()
    {
        var request = new CreateTodoItemRequest { Title = "Buy milk", Priority = Priority.Medium };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_title_fails(string title)
    {
        var request = new CreateTodoItemRequest { Title = title, Priority = Priority.Medium };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
    }

    [Fact]
    public async Task Title_at_boundary_max_length_passes()
    {
        var request = new CreateTodoItemRequest
        {
            Title = new string('X', TodoItem.MaxTitleLength),
            Priority = Priority.Medium,
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Title_one_over_max_length_fails()
    {
        var request = new CreateTodoItemRequest
        {
            Title = new string('X', TodoItem.MaxTitleLength + 1),
            Priority = Priority.Medium,
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
    }

    // ----- Description (.When condition) -----

    [Fact]
    public async Task Null_description_skips_length_check()
    {
        var request = new CreateTodoItemRequest
        {
            Title = "Valid",
            Priority = Priority.Medium,
            Description = null,
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Description_at_boundary_max_length_passes()
    {
        var request = new CreateTodoItemRequest
        {
            Title = "Valid",
            Priority = Priority.Medium,
            Description = new string('X', TodoItem.MaxDescriptionLength),
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Description_one_over_max_length_fails()
    {
        var request = new CreateTodoItemRequest
        {
            Title = "Valid",
            Priority = Priority.Medium,
            Description = new string('X', TodoItem.MaxDescriptionLength + 1),
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Description");
    }

    // ----- Priority -----

    [Theory]
    [InlineData(Priority.Low)]
    [InlineData(Priority.Medium)]
    [InlineData(Priority.High)]
    [InlineData(Priority.Critical)]
    public async Task Valid_priority_passes(Priority priority)
    {
        var request = new CreateTodoItemRequest { Title = "Valid", Priority = priority };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_priority_fails()
    {
        var request = new CreateTodoItemRequest { Title = "Valid", Priority = (Priority)99 };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Priority");
    }

    // ----- DueDate (.When condition + .Must() lambda) -----

    [Fact]
    public async Task Null_due_date_skips_date_check()
    {
        var request = new CreateTodoItemRequest
        {
            Title = "Valid",
            Priority = Priority.Medium,
            DueDate = null,
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Future_due_date_passes()
    {
        var request = new CreateTodoItemRequest
        {
            Title = "Valid",
            Priority = Priority.Medium,
            DueDate = DateTimeOffset.UtcNow.AddDays(1),
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Past_due_date_fails()
    {
        var request = new CreateTodoItemRequest
        {
            Title = "Valid",
            Priority = Priority.Medium,
            DueDate = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DueDate");
    }

    [Fact]
    public async Task Due_date_exactly_now_fails_because_not_strictly_in_future()
    {
        // Validates that .Must(d => d > UtcNow) is strictly greater-than,
        // not greater-than-or-equal-to. The lambda evaluates fresh on each call
        // so this also confirms time is NOT captured at construction.
        var request = new CreateTodoItemRequest
        {
            Title = "Valid",
            Priority = Priority.Medium,
            DueDate = DateTimeOffset.UtcNow,
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DueDate");
    }
}

// =============================================================================
// UpdateTodoItemValidator
// =============================================================================

/// <summary>
/// Unit tests for <see cref="UpdateTodoItemValidator"/>.
/// </summary>
public class UpdateTodoItemValidatorTests
{
    private readonly UpdateTodoItemValidator _sut = new();

    [Fact]
    public async Task Valid_request_passes()
    {
        var request = new UpdateTodoItemRequest { Title = "Updated", Priority = Priority.High };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_title_fails()
    {
        var request = new UpdateTodoItemRequest { Title = "", Priority = Priority.Medium };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
    }

    [Fact]
    public async Task Title_one_over_max_length_fails()
    {
        var request = new UpdateTodoItemRequest
        {
            Title = new string('X', TodoItem.MaxTitleLength + 1),
            Priority = Priority.Medium,
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
    }

    [Fact]
    public async Task Description_one_over_max_length_fails_when_provided()
    {
        var request = new UpdateTodoItemRequest
        {
            Title = "Valid",
            Priority = Priority.Medium,
            Description = new string('X', TodoItem.MaxDescriptionLength + 1),
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Description");
    }

    [Fact]
    public async Task Null_description_does_not_trigger_length_rule()
    {
        var request = new UpdateTodoItemRequest
        {
            Title = "Valid",
            Priority = Priority.Medium,
            Description = null,
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_priority_fails()
    {
        var request = new UpdateTodoItemRequest { Title = "Valid", Priority = (Priority)99 };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Priority");
    }

    [Fact]
    public async Task Past_due_date_fails()
    {
        var request = new UpdateTodoItemRequest
        {
            Title = "Valid",
            Priority = Priority.Medium,
            DueDate = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DueDate");
    }

    [Fact]
    public async Task Null_due_date_skips_date_check()
    {
        var request = new UpdateTodoItemRequest
        {
            Title = "Valid",
            Priority = Priority.Medium,
            DueDate = null,
        };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }
}

// =============================================================================
// UpdateTodoStatusValidator
// =============================================================================

/// <summary>
/// Unit tests for <see cref="UpdateTodoStatusValidator"/>.
/// </summary>
public class UpdateTodoStatusValidatorTests
{
    private readonly UpdateTodoStatusValidator _sut = new();

    [Theory]
    [InlineData(TodoStatus.Pending)]
    [InlineData(TodoStatus.InProgress)]
    [InlineData(TodoStatus.Completed)]
    [InlineData(TodoStatus.Cancelled)]
    public async Task Valid_status_passes(TodoStatus status)
    {
        var request = new UpdateTodoStatusRequest { Status = status };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_status_fails()
    {
        var request = new UpdateTodoStatusRequest { Status = (TodoStatus)99 };
        var result = await _sut.ValidateAsync(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Status");
    }
}

// =============================================================================
// TodoItemQueryParametersValidator
// =============================================================================

/// <summary>
/// Unit tests for <see cref="TodoItemQueryParametersValidator"/>.
/// </summary>
public class TodoItemQueryParametersValidatorTests
{
    private readonly TodoItemQueryParametersValidator _sut = new();

    [Fact]
    public async Task Default_parameters_pass()
    {
        var query = new TodoItemQueryParameters();
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Page_zero_fails()
    {
        var query = new TodoItemQueryParameters { Page = 0 };
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Fact]
    public async Task Page_one_passes()
    {
        var query = new TodoItemQueryParameters { Page = 1 };
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task PageSize_zero_fails()
    {
        var query = new TodoItemQueryParameters { PageSize = 0 };
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Fact]
    public async Task PageSize_at_max_boundary_passes()
    {
        var query = new TodoItemQueryParameters { PageSize = TodoItemQueryParameters.MaxPageSize };
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task PageSize_one_over_max_fails()
    {
        var query = new TodoItemQueryParameters { PageSize = TodoItemQueryParameters.MaxPageSize + 1 };
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PageSize");
    }

    [Fact]
    public async Task Null_status_skips_status_check()
    {
        var query = new TodoItemQueryParameters { Status = null };
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_status_fails_when_provided()
    {
        var query = new TodoItemQueryParameters { Status = (TodoStatus)99 };
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Status");
    }

    [Fact]
    public async Task Null_priority_skips_priority_check()
    {
        var query = new TodoItemQueryParameters { Priority = null };
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_priority_fails_when_provided()
    {
        var query = new TodoItemQueryParameters { Priority = (Priority)99 };
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Priority");
    }

    [Fact]
    public async Task Null_search_skips_length_check()
    {
        var query = new TodoItemQueryParameters { Search = null };
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Search_at_boundary_100_chars_passes()
    {
        var query = new TodoItemQueryParameters { Search = new string('X', 100) };
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Search_101_chars_fails()
    {
        var query = new TodoItemQueryParameters { Search = new string('X', 101) };
        var result = await _sut.ValidateAsync(query);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Search");
    }
}
