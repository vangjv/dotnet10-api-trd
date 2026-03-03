using Acme.TaskHub.Domain.Entities;
using Acme.TaskHub.Domain.Enums;

namespace Acme.TaskHub.Application.Interfaces;

/// <summary>
/// Repository abstraction for <see cref="TodoItem"/> persistence.
/// Defined in the Application layer; implemented in Infrastructure.
/// </summary>
public interface ITodoItemRepository
{
    /// <summary>
    /// Gets a single to-do item by its unique identifier.
    /// </summary>
    /// <param name="id">The item id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <c>null</c> if not found.</returns>
    Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paged list of to-do items with optional filtering.
    /// </summary>
    /// <param name="status">Optional status filter.</param>
    /// <param name="priority">Optional priority filter.</param>
    /// <param name="searchTerm">Optional search term to match against title.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple of (items, totalCount).</returns>
    Task<(IReadOnlyList<TodoItem> Items, int TotalCount)> GetPagedAsync(
        TodoStatus? status,
        Priority? priority,
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new to-do item to the store.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(TodoItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes to an existing to-do item.
    /// </summary>
    /// <param name="item">The modified item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateAsync(TodoItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a to-do item from the store.
    /// </summary>
    /// <param name="id">The item id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the item was found and deleted; otherwise <c>false</c>.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
