namespace Company.Product.Service.Domain;

public sealed class Customer
{
    public const int MaxDisplayNameLength = 200;

    public Customer(Guid id, string displayName)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
        DisplayName = NormalizeDisplayName(displayName);
        Id = id;
    }

    public Guid Id { get; }
    public string DisplayName { get; private set; }

    public void Rename(string newName) => DisplayName = NormalizeDisplayName(newName);

    private static string NormalizeDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var normalized = displayName.Trim();
        if (normalized.Length > MaxDisplayNameLength)
            throw new ArgumentException("Display name is too long.", nameof(displayName));

        return normalized;
    }
}
