using Acme.TaskHub.Application.Models;
using FluentValidation;

namespace Acme.TaskHub.Application.Validators;

/// <summary>
/// Validates <see cref="TodoItemQueryParameters"/> for list/search operations.
/// </summary>
public sealed class TodoItemQueryParametersValidator : AbstractValidator<TodoItemQueryParameters>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TodoItemQueryParametersValidator"/> class.
    /// </summary>
    public TodoItemQueryParametersValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, TodoItemQueryParameters.MaxPageSize)
            .WithMessage($"PageSize must be between 1 and {TodoItemQueryParameters.MaxPageSize}.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid status value.")
            .When(x => x.Status.HasValue);

        RuleFor(x => x.Priority)
            .IsInEnum().WithMessage("Invalid priority value.")
            .When(x => x.Priority.HasValue);

        RuleFor(x => x.Search)
            .MaximumLength(100).WithMessage("Search term must not exceed 100 characters.")
            .When(x => x.Search is not null);
    }
}
