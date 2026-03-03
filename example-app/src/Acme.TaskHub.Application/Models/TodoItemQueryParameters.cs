using Acme.TaskHub.Domain.Enums;

namespace Acme.TaskHub.Application.Models;

/// <summary>
/// Query parameters for listing to-do items with filtering and paging.
/// </summary>
public sealed record TodoItemQueryParameters
{
    /// <summary>Default page size per TRD (section Appendix A).</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Maximum allowed page size per TRD (section 6.1).</summary>
    public const int MaxPageSize = 200;

    /// <summary>Gets the page number (1-based, defaults to 1).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Gets the page size (defaults to 50, max 200).</summary>
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Gets the optional status filter.</summary>
    public TodoStatus? Status { get; init; }

    /// <summary>Gets the optional priority filter.</summary>
    public Priority? Priority { get; init; }

    /// <summary>Gets the optional search term to match against title.</summary>
    public string? Search { get; init; }
}
