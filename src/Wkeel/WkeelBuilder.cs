using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Wkeel;

/// <summary>Configures Wkeel inside <see cref="WkeelServiceCollectionExtensions.AddWkeel"/>.</summary>
public sealed class WkeelBuilder
{
    private readonly IServiceCollection _services;

    // Keyed by handler type so registering a handler twice is one registration, and the last lifetime wins.
    private readonly OrderedDictionary<Type, ServiceLifetime?> _handlers = [];
    private ServiceLifetime _defaultLifetime = ServiceLifetime.Transient;

    // One entry per Intercept call, in registration order. Each returns the closed interceptor
    // for a message and result, or null when the registration does not apply to that message.
    private readonly List<Func<Type, Type, Type?>> _interceptors = [];

    internal WkeelBuilder(IServiceCollection services)
    {
        _services = services;
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

    /// <summary>
    /// Adds an open-generic interceptor around every executed message that satisfies its constraints.
    /// Interceptors run in registration order, first registered outermost.
    /// </summary>
    /// <param name="interceptorType">
    /// A concrete class with two type parameters that implements <see cref="IInterceptor{TMessage, TResult}"/>
    /// over them, such as <c>typeof(LoggingInterceptor&lt;,&gt;)</c>.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    public WkeelBuilder Intercept(Type interceptorType)
    {
        ArgumentNullException.ThrowIfNull(interceptorType);

        _interceptors.Add(OpenInterceptor.Of(interceptorType).Close);
        return this;
    }

    /// <summary>
    /// Adds an interceptor around one request type.
    /// Interceptors run in registration order, first registered outermost.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <typeparam name="TInterceptor">The interceptor.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public WkeelBuilder Intercept<TRequest, TResult, TInterceptor>()
        where TRequest : IRequest<TResult>
        where TInterceptor : class, IInterceptor<TRequest, TResult>
    {
        _interceptors.Add((message, result) =>
            message == typeof(TRequest) && result == typeof(TResult) ? typeof(TInterceptor) : null);

        return this;
    }

    /// <summary>
    /// Adds an interceptor around one operation type.
    /// Interceptors run in registration order, first registered outermost.
    /// </summary>
    /// <typeparam name="TOperation">The operation type.</typeparam>
    /// <typeparam name="TInterceptor">The interceptor.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public WkeelBuilder Intercept<TOperation, TInterceptor>()
        where TOperation : IOperation
        where TInterceptor : class, IOperationInterceptor<TOperation>
    {
        _services.TryAddTransient<TInterceptor>();
        _interceptors.Add((message, result) =>
            message == typeof(TOperation) && result == typeof(Nothing)
                ? typeof(OperationInterceptorAdapter<TOperation, TInterceptor>)
                : null);

        return this;
    }

    internal Registry Build()
    {
        var operations = new Dictionary<Type, List<HandlerContract>>();
        var requests = new Dictionary<Type, List<HandlerContract>>();

        foreach (var (handlerType, lifetime) in _handlers)
        {
            foreach (var contract in HandlerContract.Of(handlerType))
            {
                _services.Add(new ServiceDescriptor(contract.ServiceType, handlerType, lifetime ?? _defaultLifetime));

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

    private FrozenDictionary<Type, Route> ToRoutes(Dictionary<Type, List<HandlerContract>> contracts) =>
        contracts.ToFrozenDictionary(
            entry => entry.Key,
            entry => new Route(
                entry.Value[0].CreateInvoker(InterceptorsFor(entry.Value[0])),
                [.. entry.Value.Select(contract => contract.HandlerType)]));

    private Type[] InterceptorsFor(HandlerContract contract)
    {
        var interceptors = _interceptors
            .Select(close => close(contract.MessageType, contract.ResultType ?? typeof(Nothing)))
            .OfType<Type>()
            .ToArray();

        foreach (var interceptor in interceptors)
        {
            _services.TryAddTransient(interceptor);
        }

        return interceptors;
    }

    // A singleton handler would capture root-scope dependencies, which breaks the scoping rule.
    private static void EnsureSupported(ServiceLifetime lifetime, string parameterName)
    {
        if (lifetime is not (ServiceLifetime.Transient or ServiceLifetime.Scoped))
        {
            throw new ArgumentOutOfRangeException(parameterName, lifetime, "Handlers must be Transient or Scoped.");
        }
    }
}
