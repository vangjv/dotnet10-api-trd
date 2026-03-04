using Acme.TaskHub.Application.Interfaces;
using Acme.TaskHub.Application.Mapping;
using Acme.TaskHub.Application.Models;
using Acme.TaskHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Acme.TaskHub.Application.Services;

/// <summary>
/// Default implementation of <see cref="ITodoItemService"/>.
/// Coordinates domain operations and persistence through the repository.
/// </summary>
public sealed class TodoItemService : ITodoItemService
{
    private readonly ITodoItemRepository _repository;
    private readonly ILogger<TodoItemService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoItemService"/> class.
    /// </summary>
    public TodoItemService(
        ITodoItemRepository repository,
        ILogger<TodoItemService> logger,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<TodoItemResponse> CreateAsync(
        CreateTodoItemRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = new TodoItem(
            Guid.NewGuid(),
            request.Title,
            request.Description,
            request.Priority,
            request.DueDate,
            _timeProvider);

        await _repository.AddAsync(item, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Created TodoItem {TodoItemId} with title '{Title}'",
            item.Id,
            item.Title);

        return item.ToResponse();
    }

    /// <inheritdoc />
    public async Task<TodoItemResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return item?.ToResponse();
    }

    /// <inheritdoc />
    public async Task<PagedResult<TodoItemResponse>> GetPagedAsync(
        TodoItemQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        int pageSize = Math.Clamp(query.PageSize, 1, TodoItemQueryParameters.MaxPageSize);
        int page = Math.Max(query.Page, 1);

        var (items, totalCount) = await _repository.GetPagedAsync(
            query.Status,
            query.Priority,
            query.Search,
            page,
            pageSize,
            cancellationToken).ConfigureAwait(false);

        return new PagedResult<TodoItemResponse>
        {
            Items = items.ToResponseList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <inheritdoc />
    public async Task<TodoItemResponse?> UpdateAsync(
        Guid id,
        UpdateTodoItemRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        item.EnsureMutable();
        item.SetTitle(request.Title);
        item.SetDescription(request.Description);
        item.SetPriority(request.Priority);
        item.SetDueDate(request.DueDate);

        await _repository.UpdateAsync(item, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Updated TodoItem {TodoItemId}", item.Id);

        return item.ToResponse();
    }

    /// <inheritdoc />
    public async Task<TodoItemResponse?> UpdateStatusAsync(
        Guid id,
        UpdateTodoStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        item.TransitionTo(request.Status);

        await _repository.UpdateAsync(item, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Transitioned TodoItem {TodoItemId} to status '{Status}'",
            item.Id,
            request.Status);

        return item.ToResponse();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        bool deleted = await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        if (deleted)
        {
            _logger.LogInformation("Deleted TodoItem {TodoItemId}", id);
        }

        return deleted;
    }
}
