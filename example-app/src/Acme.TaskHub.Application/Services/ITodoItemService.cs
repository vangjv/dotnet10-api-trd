using Acme.TaskHub.Application.Models;

namespace Acme.TaskHub.Application.Services;

/// <summary>
/// Application service for managing to-do items.
/// </summary>
public interface ITodoItemService
{
    /// <summary>Creates a new to-do item.</summary>
    Task<TodoItemResponse> CreateAsync(CreateTodoItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets a single to-do item by id.</summary>
    Task<TodoItemResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Gets a paged list of to-do items.</summary>
    Task<PagedResult<TodoItemResponse>> GetPagedAsync(
        TodoItemQueryParameters query,
        CancellationToken cancellationToken = default);

    /// <summary>Updates an existing to-do item.</summary>
    Task<TodoItemResponse?> UpdateAsync(
        Guid id,
        UpdateTodoItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Transitions the status of a to-do item.</summary>
    Task<TodoItemResponse?> UpdateStatusAsync(
        Guid id,
        UpdateTodoStatusRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a to-do item.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
