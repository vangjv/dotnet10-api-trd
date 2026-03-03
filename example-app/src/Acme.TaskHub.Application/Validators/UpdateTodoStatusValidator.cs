using Acme.TaskHub.Application.Models;
using FluentValidation;

namespace Acme.TaskHub.Application.Validators;

/// <summary>
/// Validates <see cref="UpdateTodoStatusRequest"/> payloads.
/// </summary>
public sealed class UpdateTodoStatusValidator : AbstractValidator<UpdateTodoStatusRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateTodoStatusValidator"/> class.
    /// </summary>
    public UpdateTodoStatusValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid status value.");
    }
}
