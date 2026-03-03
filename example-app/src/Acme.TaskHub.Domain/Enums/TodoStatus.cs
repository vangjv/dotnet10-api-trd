namespace Acme.TaskHub.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of a to-do item.
/// </summary>
public enum TodoStatus
{
    /// <summary>Item is pending and not yet started.</summary>
    Pending = 0,

    /// <summary>Item is actively being worked on.</summary>
    InProgress = 1,

    /// <summary>Item has been completed successfully.</summary>
    Completed = 2,

    /// <summary>Item has been cancelled and will not be completed.</summary>
    Cancelled = 3,
}
