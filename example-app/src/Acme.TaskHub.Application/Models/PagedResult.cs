namespace Acme.TaskHub.Application.Models;

/// <summary>
/// A generic paged result set.
/// </summary>
/// <typeparam name="T">The type of items in the page.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>Gets the items for the current page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Gets the total number of items across all pages.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Gets the current page number (1-based).</summary>
    public required int Page { get; init; }

    /// <summary>Gets the page size.</summary>
    public required int PageSize { get; init; }

    /// <summary>Gets the total number of pages.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}
