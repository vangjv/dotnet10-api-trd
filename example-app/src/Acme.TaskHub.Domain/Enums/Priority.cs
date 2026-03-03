namespace Acme.TaskHub.Domain.Enums;

/// <summary>
/// Represents the priority level of a to-do item.
/// </summary>
public enum Priority
{
    /// <summary>Low priority.</summary>
    Low = 0,

    /// <summary>Medium priority (default).</summary>
    Medium = 1,

    /// <summary>High priority.</summary>
    High = 2,

    /// <summary>Critical priority — requires immediate attention.</summary>
    Critical = 3,
}
