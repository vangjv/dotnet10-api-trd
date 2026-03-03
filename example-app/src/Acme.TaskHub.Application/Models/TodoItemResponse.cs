using Acme.TaskHub.Domain.Enums;

namespace Acme.TaskHub.Application.Models;

/// <summary>
/// Response payload representing a to-do item.
/// </summary>
public sealed record TodoItemResponse
{
    /// <summary>Gets the unique identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the priority level.</summary>
    public required Priority Priority { get; init; }

    /// <summary>Gets the lifecycle status.</summary>
    public required TodoStatus Status { get; init; }

    /// <summary>Gets the due date.</summary>
    public DateTimeOffset? DueDate { get; init; }

    /// <summary>Gets the creation timestamp (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Gets the completion timestamp (UTC).</summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }
}
