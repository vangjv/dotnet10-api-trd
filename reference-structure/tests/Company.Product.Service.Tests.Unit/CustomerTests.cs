using Company.Product.Service.Domain;
using AwesomeAssertions;

namespace Company.Product.Service.Tests.Unit;

public class CustomerTests
{
    [Fact]
    public void Constructor_requires_non_empty_id()
    {
        var act = () => new Customer(Guid.Empty, "Acme");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_updates_display_name()
    {
        var c = new Customer(Guid.NewGuid(), "Old");
        c.Rename("New");
        c.DisplayName.Should().Be("New");
    }
}
