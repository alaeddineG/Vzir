using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;

namespace Wkeel;

/// <summary>Configures Wkeel inside <see cref="WkeelServiceCollectionExtensions.AddWkeel"/>.</summary>
public sealed class WkeelBuilder
{
    // Keyed by handler type so registering a handler twice is one registration, and the last lifetime wins.
    private readonly OrderedDictionary<Type, ServiceLifetime?> _handlers = [];
    private ServiceLifetime _defaultLifetime = ServiceLifetime.Transient;

    internal WkeelBuilder()
    {
    }

    /// <summary>Registers a handler for every message type it handles.</summary>
    /// <typeparam name="THandler">
    /// A concrete, closed type implementing <see cref="IOperationHandler{TOperation}"/>
    /// or <see cref="IRequestHandler{TRequest, TResult}"/>.
    /// </typeparam>
    /// <param name="lifetime">
    /// Overrides the default set by <see cref="HandlerLifetime"/>. Transient or Scoped.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    public WkeelBuilder AddHandler<THandler>(ServiceLifetime? lifetime = null)
        where THandler : class
    {
        if (lifetime is { } value)
        {
            EnsureSupported(value, nameof(lifetime));
        }

        var handlerType = typeof(THandler);
        if (handlerType.IsAbstract || handlerType.ContainsGenericParameters)
        {
            throw new ArgumentException($"Handler '{handlerType}' must be a concrete, closed type.", nameof(THandler));
        }

        if (!HandlerContract.Of(handlerType).Any())
        {
            throw new ArgumentException(
                $"'{handlerType}' does not implement {nameof(IOperationHandler<>)}<> or {nameof(IRequestHandler<,>)}<,>.",
                nameof(THandler));
        }

        _handlers[handlerType] = lifetime;
        return this;
    }

    /// <summary>Sets the lifetime of handlers registered without an explicit one. Defaults to Transient.</summary>
    /// <param name="lifetime">Transient or Scoped.</param>
    /// <returns>This builder, for chaining.</returns>
    public WkeelBuilder HandlerLifetime(ServiceLifetime lifetime)
    {
        EnsureSupported(lifetime, nameof(lifetime));
        _defaultLifetime = lifetime;
        return this;
    }

    internal Registry Build(IServiceCollection services)
    {
        var operations = new Dictionary<Type, List<HandlerContract>>();
        var requests = new Dictionary<Type, List<HandlerContract>>();

        foreach (var (handlerType, lifetime) in _handlers)
        {
            foreach (var contract in HandlerContract.Of(handlerType))
            {
                services.Add(new ServiceDescriptor(contract.ServiceType, handlerType, lifetime ?? _defaultLifetime));

                var routes = contract.IsOperation ? operations : requests;
                if (!routes.TryGetValue(contract.MessageType, out var contracts))
                {
                    routes[contract.MessageType] = contracts = [];
                }

                contracts.Add(contract);
            }
        }

        return new Registry(ToRoutes(operations), ToRoutes(requests));
    }

    private static FrozenDictionary<Type, Route> ToRoutes(Dictionary<Type, List<HandlerContract>> contracts) =>
        contracts.ToFrozenDictionary(
            entry => entry.Key,
            entry => new Route(entry.Value[0].CreateInvoker(), [.. entry.Value.Select(contract => contract.HandlerType)]));

    // A singleton handler would capture root-scope dependencies, which breaks the scoping rule.
    private static void EnsureSupported(ServiceLifetime lifetime, string parameterName)
    {
        if (lifetime is not (ServiceLifetime.Transient or ServiceLifetime.Scoped))
        {
            throw new ArgumentOutOfRangeException(parameterName, lifetime, "Handlers must be Transient or Scoped.");
        }
    }
}
