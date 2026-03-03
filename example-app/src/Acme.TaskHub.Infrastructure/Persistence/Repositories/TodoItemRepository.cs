using Acme.TaskHub.Application.Interfaces;
using Acme.TaskHub.Domain.Entities;
using Acme.TaskHub.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Acme.TaskHub.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core-backed implementation of <see cref="ITodoItemRepository"/>.
/// </summary>
internal sealed class TodoItemRepository : ITodoItemRepository
{
    private readonly AppDbContext _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoItemRepository"/> class.
    /// </summary>
    public TodoItemRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.TodoItems
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<TodoItem> Items, int TotalCount)> GetPagedAsync(
        TodoStatus? status,
        Priority? priority,
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<TodoItem> query = _db.TodoItems.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (priority.HasValue)
        {
            query = query.Where(x => x.Priority == priority.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            string term = searchTerm.Trim().ToLowerInvariant();
            query = query.Where(x => x.Title.ToLower().Contains(term));
        }

        int totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items.AsReadOnly(), totalCount);
    }

    /// <inheritdoc />
    public async Task AddAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        _db.TodoItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        _db.TodoItems.Update(item);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _db.TodoItems
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return false;
        }

        _db.TodoItems.Remove(item);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
