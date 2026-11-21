using Company.Product.Service.Domain;
using Microsoft.EntityFrameworkCore;

namespace Company.Product.Service.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.DisplayName).HasMaxLength(Customer.MaxDisplayNameLength).IsRequired();
        });
    }
}
