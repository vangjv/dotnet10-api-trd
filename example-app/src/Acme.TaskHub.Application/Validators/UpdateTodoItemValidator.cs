using Acme.TaskHub.Application.Models;
using Acme.TaskHub.Domain.Entities;
using FluentValidation;

namespace Acme.TaskHub.Application.Validators;

/// <summary>
/// Validates <see cref="UpdateTodoItemRequest"/> payloads.
/// </summary>
public sealed class UpdateTodoItemValidator : AbstractValidator<UpdateTodoItemRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateTodoItemValidator"/> class.
    /// </summary>
    public UpdateTodoItemValidator()
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
