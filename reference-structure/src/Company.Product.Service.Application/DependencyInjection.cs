using Microsoft.Extensions.DependencyInjection;

namespace Company.Product.Service.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) =>
        services.AddScoped<ICustomerService, CustomerService>();
}
