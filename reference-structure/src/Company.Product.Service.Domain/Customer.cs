namespace Company.Product.Service.Domain;

public sealed class Customer
{
    public Customer(Guid id, string displayName)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("DisplayName is required.", nameof(displayName))
            : displayName.Trim();

        Id = id;
    }

    public Guid Id { get; }
    public string DisplayName { get; private set; }

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("Name is required.", nameof(newName));
        DisplayName = newName.Trim();
    }
}
