using Company.Product.Service.Application;
using Company.Product.Service.Domain;
using Microsoft.EntityFrameworkCore;

namespace Company.Product.Service.Infrastructure.Persistence;

internal sealed class CustomerStore(AppDbContext database) : ICustomerStore
{
    public Task<CustomerDetails?> ReadAsync(Guid id, CancellationToken cancellationToken) =>
        database.Customers.AsNoTracking()
            .Where(customer => customer.Id == id)
            .Select(customer => new CustomerDetails(customer.Id, customer.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Customer?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        database.Customers.SingleOrDefaultAsync(customer => customer.Id == id, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => database.SaveChangesAsync(cancellationToken);
}
