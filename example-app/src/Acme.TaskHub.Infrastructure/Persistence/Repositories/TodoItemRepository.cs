using Acme.TaskHub.Application.Interfaces;
using Acme.TaskHub.Domain.Entities;
using Acme.TaskHub.Domain.Enums;
using Acme.TaskHub.Domain.Exceptions;
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
        // FindAsync checks the identity map before hitting the database (TRD §11.4).
        return await _db.TodoItems.FindAsync([id], cancellationToken).ConfigureAwait(false);
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
            // EF.Functions.Like is index-friendly and case-insensitive on SQLite/SQL Server
            // (SQLite LIKE is case-insensitive for ASCII; SQL Server uses column collation).
            // Avoid ToLower().Contains() which forces a per-row function, bypassing indexes (TRD §11.4).
            string pattern = $"%{searchTerm.Trim()}%";
            query = query.Where(x => EF.Functions.Like(x.Title, pattern));
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
        // The entity was loaded via FindAsync and is already tracked by EF Core.
        // Calling Update() would mark every column dirty — let the change tracker
        // detect only the actually modified properties instead (TRD §11.4).
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Translate to a domain exception so the Application layer does not need
            // an EF Core reference. The global handler maps DomainException → 409 (TRD §11.4).
            throw new DomainException(
                "CONCURRENCY_CONFLICT",
                "The item was modified by another request. Fetch the latest version and retry.");
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // ExecuteDeleteAsync issues a single DELETE statement with no SELECT round-trip (TRD §11.4).
        int affected = await _db.TodoItems
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return affected > 0;
    }
}
