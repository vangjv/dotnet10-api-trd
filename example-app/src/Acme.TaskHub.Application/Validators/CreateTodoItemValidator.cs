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

        RuleFor(x => x.DueDate)
            .GreaterThan(DateTimeOffset.UtcNow)
            .WithMessage("Due date must be in the future.")
            .When(x => x.DueDate.HasValue);
    }
}
