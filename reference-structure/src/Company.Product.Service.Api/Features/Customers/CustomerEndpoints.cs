using Company.Product.Service.Application;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Company.Product.Service.Api.Features.Customers;

internal static class CustomerEndpoints
{
    public static void MapCustomers(this WebApplication app)
    {
        var customers = app.MapGroup("/v1/customers").WithTags("Customers");
        customers.MapGet("/{customerId:guid}", GetAsync)
            .WithName("GetCustomer")
            .WithSummary("Get a customer")
            .WithDescription("Returns the customer with the specified ID.")
            .ProducesProblem(401)
            .ProducesProblem(404);
        customers.MapPut("/{customerId:guid}/display-name", RenameAsync)
            .WithName("SetCustomerDisplayName")
            .WithSummary("Replace a customer's display name")
            .WithDescription("Sets the display name to the normalized value. Repeating the same request has the same effect.")
            .ProducesProblem(401)
            .ProducesProblem(404);
    }

    private static async Task<Results<Ok<CustomerResponse>, ProblemHttpResult>> GetAsync(
        Guid customerId, ICustomerService service, CancellationToken cancellationToken)
    {
        var customer = await service.GetAsync(customerId, cancellationToken);
        return customer is null ? TypedResults.Problem(statusCode: 404) : TypedResults.Ok(ToResponse(customer));
    }

    private static async Task<Results<Ok<CustomerResponse>, ValidationProblem, ProblemHttpResult>> RenameAsync(
        Guid customerId, RenameCustomerRequest request, IValidator<RenameCustomerRequest> validator,
        ICustomerService service, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return TypedResults.ValidationProblem(validation.ToDictionary());

        var customer = await service.RenameAsync(customerId, request.DisplayName!, cancellationToken);
        return customer is null ? TypedResults.Problem(statusCode: 404) : TypedResults.Ok(ToResponse(customer));
    }

    private static CustomerResponse ToResponse(CustomerDetails customer) => new(customer.Id, customer.DisplayName);
}

/// <summary>The replacement display name for a customer.</summary>
public sealed record RenameCustomerRequest
{
    /// <summary>A nonblank display name of at most 200 characters after trimming.</summary>
    public string? DisplayName { get; init; }
}

/// <summary>A customer and their current display name.</summary>
public sealed record CustomerResponse(Guid Id, string DisplayName);

internal sealed class RenameCustomerValidator : AbstractValidator<RenameCustomerRequest>
{
    public RenameCustomerValidator() =>
        RuleFor(request => request.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(name => name!.Trim().Length <= Domain.Customer.MaxDisplayNameLength)
            .WithMessage("Display name must have at most 200 characters.")
            .OverridePropertyName("displayName");
}
