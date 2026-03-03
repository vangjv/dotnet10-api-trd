using Acme.TaskHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Acme.TaskHub.Infrastructure.Persistence;

/// <summary>
/// EF Core database context for the TaskHub application.
/// </summary>
public sealed class AppDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppDbContext"/> class.
    /// </summary>
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the to-do items table.</summary>
    public DbSet<TodoItem> TodoItems => Set<TodoItem>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
