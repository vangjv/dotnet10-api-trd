using Acme.TaskHub.Domain.Enums;

namespace Acme.TaskHub.Application.Models;

/// <summary>
/// Request payload for updating an existing to-do item.
/// </summary>
public sealed record UpdateTodoItemRequest
{
    /// <summary>Gets the updated title (required, max 200 chars).</summary>
    public required string Title { get; init; }

    /// <summary>Gets the updated description (max 2000 chars).</summary>
    public string? Description { get; init; }

    /// <summary>Gets the updated priority.</summary>
    public Priority Priority { get; init; } = Priority.Medium;

    /// <summary>Gets the updated due date (null to clear).</summary>
    public DateTimeOffset? DueDate { get; init; }
}
