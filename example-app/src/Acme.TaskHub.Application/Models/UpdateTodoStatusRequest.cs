using Acme.TaskHub.Domain.Enums;

namespace Acme.TaskHub.Application.Models;

/// <summary>
/// Request payload for transitioning a to-do item's status.
/// </summary>
public sealed record UpdateTodoStatusRequest
{
    /// <summary>Gets the target status.</summary>
    public required TodoStatus Status { get; init; }
}
