using Acme.TaskHub.Domain.Enums;

namespace Acme.TaskHub.Application.Models;

/// <summary>
/// Request payload for creating a new to-do item.
/// </summary>
public sealed record CreateTodoItemRequest
{
    /// <summary>Gets the title (required, max 200 chars).</summary>
    public required string Title { get; init; }

    /// <summary>Gets the optional description (max 2000 chars).</summary>
    public string? Description { get; init; }

    /// <summary>Gets the priority (defaults to Medium).</summary>
    public Priority Priority { get; init; } = Priority.Medium;

    /// <summary>Gets the optional due date (must be in the future).</summary>
    public DateTimeOffset? DueDate { get; init; }
}
