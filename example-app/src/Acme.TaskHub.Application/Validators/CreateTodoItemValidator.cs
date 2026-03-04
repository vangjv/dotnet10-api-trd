using Acme.TaskHub.Application.Models;
using Acme.TaskHub.Domain.Entities;
using FluentValidation;

namespace Acme.TaskHub.Application.Validators;

/// <summary>
/// Validates <see cref="CreateTodoItemRequest"/> payloads.
/// </summary>
public sealed class CreateTodoItemValidator : AbstractValidator<CreateTodoItemRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateTodoItemValidator"/> class.
    /// </summary>
    public CreateTodoItemValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(TodoItem.MaxTitleLength)
            .WithMessage($"Title must not exceed {TodoItem.MaxTitleLength} characters.");

        RuleFor(x => x.Description)
            .MaximumLength(TodoItem.MaxDescriptionLength)
            .WithMessage($"Description must not exceed {TodoItem.MaxDescriptionLength} characters.")
            .When(x => x.Description is not null);

        RuleFor(x => x.Priority)
            .IsInEnum().WithMessage("Invalid priority value.");

        // TRD §13.3: Use .Must() lambda so the comparison is evaluated at validation time;
        // .GreaterThan(DateTimeOffset.UtcNow) captures the value at constructor time and
        // returns a stale timestamp for the lifetime of the validator instance.
        RuleFor(x => x.DueDate)
            .Must(d => d > DateTimeOffset.UtcNow)
            .WithMessage("Due date must be in the future.")
            .When(x => x.DueDate.HasValue);
    }
}
