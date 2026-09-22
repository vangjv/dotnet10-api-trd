using Microsoft.Extensions.DependencyInjection;

namespace Company.Product.Service.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register use-cases, handlers, validators (if not registered in API), etc.
        return services;
    }
}
