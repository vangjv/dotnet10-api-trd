using Company.Product.Service.Domain;

namespace Company.Product.Service.Application;

public sealed record CustomerDetails(Guid Id, string DisplayName);

public interface ICustomerService
{
    Task<CustomerDetails?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerDetails?> RenameAsync(Guid id, string displayName, CancellationToken cancellationToken);
}

public interface ICustomerStore
{
    Task<CustomerDetails?> ReadAsync(Guid id, CancellationToken cancellationToken);
    Task<Customer?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal sealed class CustomerService(ICustomerStore store) : ICustomerService
{
    public Task<CustomerDetails?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        store.ReadAsync(id, cancellationToken);

    public async Task<CustomerDetails?> RenameAsync(Guid id, string displayName, CancellationToken cancellationToken)
    {
        var customer = await store.FindForUpdateAsync(id, cancellationToken);
        if (customer is null)
            return null;

        customer.Rename(displayName);
        await store.SaveChangesAsync(cancellationToken);
        return new CustomerDetails(customer.Id, customer.DisplayName);
    }
}
