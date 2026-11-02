using Microsoft.Extensions.DependencyInjection;

namespace Wkeel;

/// <summary>Registers Wkeel with an <see cref="IServiceCollection"/>.</summary>
public static class WkeelServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IWkeel"/> as scoped, along with the configured handlers.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures handlers and their lifetimes.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException"><c>AddWkeel</c> was already called on this collection.</exception>
    public static IServiceCollection AddWkeel(this IServiceCollection services, Action<WkeelBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(Registry)))
        {
            throw new InvalidOperationException(
                $"{nameof(AddWkeel)} can only be called once. A second call would silently replace the first call's routes.");
        }

        var builder = new WkeelBuilder();
        configure(builder);

        services.AddSingleton(builder.Build(services));
        services.AddScoped<IWkeel, Dispatcher>();
        return services;
    }
}
