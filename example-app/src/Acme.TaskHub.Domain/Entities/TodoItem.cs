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

    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoItem"/> class.
    /// </summary>
    /// <param name="id">Unique identifier (must not be <see cref="Guid.Empty"/>).</param>
    /// <param name="title">Required title (max 200 chars).</param>
    /// <param name="description">Optional description (max 2000 chars).</param>
    /// <param name="priority">Priority level (defaults to <see cref="Priority.Medium"/>).</param>
    /// <param name="dueDate">Optional due date (must be in the future if provided).</param>
    /// <param name="timeProvider">
    /// Clock abstraction used for all time reads. Defaults to <see cref="TimeProvider.System"/>.
    /// Pass a <c>FakeTimeProvider</c> in tests for deterministic time assertions.
    /// </param>
    public TodoItem(
        Guid id,
        string title,
        string? description = null,
        Priority priority = Priority.Medium,
        DateTimeOffset? dueDate = null,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (id == Guid.Empty)
        {
            throw new DomainValidationException("INVALID_ID", "Id cannot be empty.");
        }

        Id = id;
        Status = TodoStatus.Pending;
        var now = _timeProvider.GetUtcNow();
        CreatedAtUtc = now;
        UpdatedAtUtc = now;

        // Use guard-free core helpers — EnsureMutable() would pass anyway (Status is Pending)
        // but avoiding it keeps construction semantics clean.
        SetTitleCore(title);
        SetDescriptionCore(description);
        SetPriorityCore(priority);

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

    /// <summary>Gets the last-modified timestamp (UTC). Updated on every mutation.</summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Gets the completion timestamp (UTC), set when status transitions to Completed.</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the optimistic concurrency row version token (managed by EF Core).
    /// Prevents lost-update anomalies when two requests concurrently read and mutate the same item.
    /// </summary>
    public byte[]? RowVersion { get; private set; }

    /// <summary>
    /// Throws a <see cref="DomainException"/> if the item is in a terminal state
    /// (<see cref="TodoStatus.Completed"/> or <see cref="TodoStatus.Cancelled"/>)
    /// and therefore cannot be mutated.
    /// </summary>
    /// <remarks>
    /// Call this at the top of every behavioral method that modifies non-status state.
    /// <see cref="TransitionTo"/> has its own state-machine guard and does not need this.
    /// To mutate a cancelled item, re-open it first by transitioning to
    /// <see cref="TodoStatus.Pending"/>.
    /// </remarks>
    /// <exception cref="DomainException">
    /// Thrown with error code <c>ITEM_NOT_MUTABLE</c> when the item is in a terminal state.
    /// </exception>
    public void EnsureMutable()
    {
        if (Status is TodoStatus.Completed or TodoStatus.Cancelled)
        {
            throw new DomainException(
                "ITEM_NOT_MUTABLE",
                $"Cannot modify a to-do item in '{Status}' status. Re-open it first.");
        }
    }

    /// <summary>
    /// Updates the title of this item.
    /// </summary>
    /// <param name="newTitle">New title (required, max 200 chars).</param>
    /// <exception cref="DomainException">Thrown when the item is in a terminal state.</exception>
    /// <exception cref="DomainValidationException">Thrown when the title is empty or too long.</exception>
    public void SetTitle(string newTitle)
    {
        EnsureMutable();
        SetTitleCore(newTitle);
    }

    /// <summary>
    /// Updates the description of this item.
    /// </summary>
    /// <param name="newDescription">New description (optional, max 2000 chars).</param>
    /// <exception cref="DomainException">Thrown when the item is in a terminal state.</exception>
    /// <exception cref="DomainValidationException">Thrown when the description exceeds the limit.</exception>
    public void SetDescription(string? newDescription)
    {
        EnsureMutable();
        SetDescriptionCore(newDescription);
    }

    /// <summary>
    /// Updates the priority level.
    /// </summary>
    /// <param name="newPriority">New priority.</param>
    /// <exception cref="DomainException">Thrown when the item is in a terminal state.</exception>
    /// <exception cref="DomainValidationException">Thrown when the priority value is not defined.</exception>
    public void SetPriority(Priority newPriority)
    {
        EnsureMutable();
        SetPriorityCore(newPriority);
    }

    /// <summary>
    /// Updates the due date.
    /// </summary>
    /// <param name="newDueDate">New due date (null to clear).</param>
    /// <exception cref="DomainException">Thrown when the item is in a terminal state.</exception>
    /// <exception cref="DomainValidationException">Thrown when the due date is not in the future.</exception>
    public void SetDueDate(DateTimeOffset? newDueDate)
    {
        EnsureMutable();

        if (newDueDate.HasValue)
        {
            ValidateDueDate(newDueDate.Value);
        }

        DueDate = newDueDate;
        Touch();
    }

    /// <summary>
    /// Transitions the item to a new status, enforcing lifecycle rules.
    /// </summary>
    /// <param name="newStatus">Target status.</param>
    /// <exception cref="DomainException">
    /// Thrown with error code <c>INVALID_STATUS_TRANSITION</c> when the transition is not permitted.
    /// </exception>
    /// <remarks>
    /// Valid transitions:
    /// <list type="bullet">
    ///   <item>Pending &#8594; InProgress, Completed, Cancelled</item>
    ///   <item>InProgress &#8594; Completed, Cancelled, Pending (re-open)</item>
    ///   <item>Completed &#8594; (terminal — no transitions allowed)</item>
    ///   <item>Cancelled &#8594; Pending (re-open only)</item>
    /// </list>
    /// Same-status transitions are no-ops (idempotent).
    /// </remarks>
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
            CompletedAtUtc = _timeProvider.GetUtcNow();
        }
        else
        {
            CompletedAtUtc = null;
        }

        Touch();
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
    ///   <item>Pending &#8594; InProgress, Completed, Cancelled</item>
    ///   <item>InProgress &#8594; Completed, Cancelled, Pending (re-open)</item>
    ///   <item>Completed &#8594; (terminal — no transitions allowed)</item>
    ///   <item>Cancelled &#8594; Pending (re-open only)</item>
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

    /// <summary>Updates <see cref="UpdatedAtUtc"/> to the current clock time.</summary>
    private void Touch() => UpdatedAtUtc = _timeProvider.GetUtcNow();

    // Guard-free private helpers — used by both the constructor (before Status is terminal)
    // and the public setters (which call EnsureMutable() first).
    private void SetTitleCore(string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
        {
            throw new DomainValidationException("TITLE_REQUIRED", "Title is required.");
        }

        string trimmed = newTitle.Trim();
        if (trimmed.Length > MaxTitleLength)
        {
            throw new DomainValidationException(
                "TITLE_TOO_LONG",
                $"Title must not exceed {MaxTitleLength} characters.");
        }

        Title = trimmed;
        Touch();
    }

    private void SetDescriptionCore(string? newDescription)
    {
        if (newDescription is not null)
        {
            string trimmed = newDescription.Trim();
            if (trimmed.Length > MaxDescriptionLength)
            {
                throw new DomainValidationException(
                    "DESCRIPTION_TOO_LONG",
                    $"Description must not exceed {MaxDescriptionLength} characters.");
            }

            Description = trimmed.Length == 0 ? null : trimmed;
        }
        else
        {
            Description = null;
        }

        Touch();
    }

    private void SetPriorityCore(Priority newPriority)
    {
        if (!Enum.IsDefined(newPriority))
        {
            throw new DomainValidationException("INVALID_PRIORITY", "Invalid priority value.");
        }

        Priority = newPriority;
        Touch();
    }

    private void ValidateDueDate(DateTimeOffset dueDate)
    {
        if (dueDate <= _timeProvider.GetUtcNow())
        {
            throw new DomainValidationException(
                "DUE_DATE_IN_PAST",
                "Due date must be in the future.");
        }
    }

    // EF Core requires a private parameterless constructor for materialization.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
    private TodoItem()
    {
        _timeProvider = TimeProvider.System;
    }
#pragma warning restore CS8618
}
