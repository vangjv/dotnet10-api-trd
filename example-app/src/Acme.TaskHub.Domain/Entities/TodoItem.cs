using Acme.TaskHub.Domain.Enums;
using Acme.TaskHub.Domain.Exceptions;

namespace Acme.TaskHub.Domain.Entities;

/// <summary>
/// Represents a to-do item with lifecycle management and domain invariants.
/// </summary>
public sealed class TodoItem
{
    /// <summary>Maximum allowed length for <see cref="Title"/>.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Maximum allowed length for <see cref="Description"/>.</summary>
    public const int MaxDescriptionLength = 2000;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoItem"/> class.
    /// </summary>
    /// <param name="id">Unique identifier (must not be <see cref="Guid.Empty"/>).</param>
    /// <param name="title">Required title (max 200 chars).</param>
    /// <param name="description">Optional description (max 2000 chars).</param>
    /// <param name="priority">Priority level (defaults to <see cref="Priority.Medium"/>).</param>
    /// <param name="dueDate">Optional due date (must be in the future if provided).</param>
    public TodoItem(
        Guid id,
        string title,
        string? description = null,
        Priority priority = Priority.Medium,
        DateTimeOffset? dueDate = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id cannot be empty.", nameof(id));
        }

        Id = id;
        SetTitle(title);
        SetDescription(description);
        Priority = priority;
        Status = TodoStatus.Pending;
        CreatedAtUtc = DateTimeOffset.UtcNow;

        if (dueDate.HasValue)
        {
            ValidateDueDate(dueDate.Value);
            DueDate = dueDate.Value;
        }
    }

    /// <summary>Gets the unique identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the title.</summary>
    public string Title { get; private set; } = null!;

    /// <summary>Gets the optional description.</summary>
    public string? Description { get; private set; }

    /// <summary>Gets the priority level.</summary>
    public Priority Priority { get; private set; }

    /// <summary>Gets the current lifecycle status.</summary>
    public TodoStatus Status { get; private set; }

    /// <summary>Gets the optional due date.</summary>
    public DateTimeOffset? DueDate { get; private set; }

    /// <summary>Gets the creation timestamp (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Gets the completion timestamp (UTC), set when status transitions to Completed.</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>
    /// Updates the title of this item.
    /// </summary>
    /// <param name="newTitle">New title (required, max 200 chars).</param>
    public void SetTitle(string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
        {
            throw new ArgumentException("Title is required.", nameof(newTitle));
        }

        string trimmed = newTitle.Trim();
        if (trimmed.Length > MaxTitleLength)
        {
            throw new ArgumentException($"Title must not exceed {MaxTitleLength} characters.", nameof(newTitle));
        }

        Title = trimmed;
    }

    /// <summary>
    /// Updates the description of this item.
    /// </summary>
    /// <param name="newDescription">New description (optional, max 2000 chars).</param>
    public void SetDescription(string? newDescription)
    {
        if (newDescription is not null)
        {
            string trimmed = newDescription.Trim();
            if (trimmed.Length > MaxDescriptionLength)
            {
                throw new ArgumentException(
                    $"Description must not exceed {MaxDescriptionLength} characters.",
                    nameof(newDescription));
            }

            Description = trimmed.Length == 0 ? null : trimmed;
        }
        else
        {
            Description = null;
        }
    }

    /// <summary>
    /// Updates the priority level.
    /// </summary>
    /// <param name="newPriority">New priority.</param>
    public void SetPriority(Priority newPriority)
    {
        if (!Enum.IsDefined(newPriority))
        {
            throw new ArgumentException("Invalid priority value.", nameof(newPriority));
        }

        Priority = newPriority;
    }

    /// <summary>
    /// Updates the due date.
    /// </summary>
    /// <param name="newDueDate">New due date (null to clear).</param>
    public void SetDueDate(DateTimeOffset? newDueDate)
    {
        if (newDueDate.HasValue)
        {
            ValidateDueDate(newDueDate.Value);
        }

        DueDate = newDueDate;
    }

    /// <summary>
    /// Transitions the item to a new status, enforcing lifecycle rules.
    /// </summary>
    /// <param name="newStatus">Target status.</param>
    /// <exception cref="DomainException">
    /// Thrown when the transition violates lifecycle rules
    /// (e.g., transitioning from Completed back to Pending).
    /// </exception>
    public void TransitionTo(TodoStatus newStatus)
    {
        if (Status == newStatus)
        {
            return; // No-op for idempotency.
        }

        if (!IsValidTransition(Status, newStatus))
        {
            throw new DomainException(
                "INVALID_STATUS_TRANSITION",
                $"Cannot transition from '{Status}' to '{newStatus}'.");
        }

        Status = newStatus;

        if (newStatus == TodoStatus.Completed)
        {
            CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            CompletedAtUtc = null;
        }
    }

    /// <summary>
    /// Determines whether a status transition is valid.
    /// </summary>
    /// <param name="from">The current status.</param>
    /// <param name="to">The target status.</param>
    /// <returns><c>true</c> if the transition is allowed; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Valid transitions:
    /// <list type="bullet">
    ///   <item>Pending -> InProgress, Completed, Cancelled</item>
    ///   <item>InProgress -> Completed, Cancelled, Pending (re-open)</item>
    ///   <item>Completed -> (terminal — no transitions allowed)</item>
    ///   <item>Cancelled -> Pending (re-open only)</item>
    /// </list>
    /// </remarks>
    internal static bool IsValidTransition(TodoStatus from, TodoStatus to)
    {
        return (from, to) switch
        {
            (TodoStatus.Pending, TodoStatus.InProgress) => true,
            (TodoStatus.Pending, TodoStatus.Completed) => true,
            (TodoStatus.Pending, TodoStatus.Cancelled) => true,
            (TodoStatus.InProgress, TodoStatus.Completed) => true,
            (TodoStatus.InProgress, TodoStatus.Cancelled) => true,
            (TodoStatus.InProgress, TodoStatus.Pending) => true,
            (TodoStatus.Cancelled, TodoStatus.Pending) => true,
            _ => false,
        };
    }

    private static void ValidateDueDate(DateTimeOffset dueDate)
    {
        if (dueDate <= DateTimeOffset.UtcNow)
        {
            throw new DomainException(
                "DUE_DATE_IN_PAST",
                "Due date must be in the future.");
        }
    }

    // EF Core requires a private parameterless constructor for materialization.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value
    private TodoItem()
    {
    }
#pragma warning restore CS8618
}
