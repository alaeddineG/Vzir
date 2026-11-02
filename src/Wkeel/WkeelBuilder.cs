using System.Collections.Frozen;
using System.Reflection;
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

    private readonly NoticeOptions _notices = new();
    private readonly Dictionary<Type, NoticeOptions> _noticeOverrides = [];

    private readonly HashSet<Assembly> _scanned = [];
    private bool _validate;

    internal WkeelBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>Registers a handler for every message type it handles.</summary>
    /// <typeparam name="THandler">
    /// A concrete, closed type implementing <see cref="IOperationHandler{TOperation}"/>,
    /// <see cref="IRequestHandler{TRequest, TResult}"/> or <see cref="INoticeHandler{TNotice}"/>
    /// for concrete message types.
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

        EnsureHandler(typeof(THandler), nameof(THandler));

        _handlers[typeof(THandler)] = lifetime;
        return this;
    }

    /// <summary>Registers every handler in an assembly, public or not, as <see cref="AddHandler"/> would.</summary>
    /// <remarks>
    /// Abstract classes are skipped as base classes, and anything else <see cref="AddHandler"/> rejects
    /// is rejected here too. Interceptors are not scanned. Handlers register in ordinal order of their
    /// full names, which is the order they run in for a sequential notice. A handler that was
    /// registered with <see cref="AddHandler"/>, before or after, keeps its explicit lifetime.
    /// </remarks>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>This builder, for chaining.</returns>
    public WkeelBuilder ScanAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        _scanned.Add(assembly);

        // Sorted because GetTypes follows compiler output, which would decide the notice order otherwise.
        foreach (var handlerType in assembly.GetTypes()
            .Where(type => !type.IsAbstract && HandlerContract.Of(type).Any())
            .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            EnsureHandler(handlerType, nameof(assembly));
            _handlers.TryAdd(handlerType, null);
        }

        return this;
    }

    /// <summary>Registers every handler in the assembly that defines <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">Any type in the assembly.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public WkeelBuilder ScanAssemblyContaining<T>() => ScanAssembly(typeof(T).Assembly);

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

    /// <summary>Sets how notice handlers run for every notice type. Sequential by default.</summary>
    /// <param name="configure">Chooses <see cref="NoticeOptions.Sequential"/> or <see cref="NoticeOptions.Parallel"/>.</param>
    /// <returns>This builder, for chaining.</returns>
    public WkeelBuilder Notices(Action<NoticeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(_notices);
        return this;
    }

    /// <summary>Sets how notice handlers run for one notice type, overriding <see cref="Notices(Action{NoticeOptions})"/>.</summary>
    /// <typeparam name="TNotice">The concrete notice type.</typeparam>
    /// <param name="configure">Chooses <see cref="NoticeOptions.Sequential"/> or <see cref="NoticeOptions.Parallel"/>.</param>
    /// <returns>This builder, for chaining.</returns>
    public WkeelBuilder Notices<TNotice>(Action<NoticeOptions> configure)
        where TNotice : INotice
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Same trap as a handler of an abstract message: this would quietly apply to nothing.
        if (typeof(TNotice).IsAbstract)
        {
            throw new ArgumentException(
                $"'{typeof(TNotice)}' is abstract, so this would apply to nothing: messages route on their exact runtime type.",
                nameof(TNotice));
        }

        if (!_noticeOverrides.TryGetValue(typeof(TNotice), out var options))
        {
            _noticeOverrides[typeof(TNotice)] = options = new NoticeOptions();
        }

        configure(options);
        return this;
    }

    /// <summary>
    /// Makes <c>AddWkeel</c> throw if handlers are missing or duplicated, reporting every problem at once
    /// as an <see cref="AggregateException"/> of <see cref="HandlerNotFoundException"/> and
    /// <see cref="AmbiguousHandlerException"/>.
    /// </summary>
    /// <remarks>
    /// Duplicates are checked across all handlers. Missing handlers are checked for every concrete operation
    /// and request in the scanned assemblies, so messages defined in an assembly that was not scanned are not
    /// checked. Open-generic messages and notices are not checked either.
    /// </remarks>
    /// <returns>This builder, for chaining.</returns>
    public WkeelBuilder ValidateOnBuild()
    {
        _validate = true;
        return this;
    }

    internal Registry Build()
    {
        var operations = new Dictionary<Type, List<HandlerContract>>();
        var requests = new Dictionary<Type, List<HandlerContract>>();
        var notices = new Dictionary<Type, List<HandlerContract>>();

        foreach (var (handlerType, lifetime) in _handlers)
        {
            foreach (var contract in HandlerContract.Of(handlerType))
            {
                _services.Add(new ServiceDescriptor(contract.ServiceType, handlerType, lifetime ?? _defaultLifetime));

                var routes = contract.Kind switch
                {
                    MessageKind.Operation => operations,
                    MessageKind.Request => requests,
                    _ => notices,
                };

                if (!routes.TryGetValue(contract.MessageType, out var contracts))
                {
                    routes[contract.MessageType] = contracts = [];
                }

                contracts.Add(contract);
            }
        }

        if (_validate)
        {
            HandlerValidation.Validate(_scanned.SelectMany(assembly => assembly.GetTypes()), operations, requests);
        }

        return new Registry(
            ToRoutes(operations, MessageTrace.Execute, Invoker),
            ToRoutes(requests, MessageTrace.Execute, Invoker),
            ToRoutes(notices, MessageTrace.Notify, NoticeInvoker));
    }

    private bool IsParallel(Type noticeType) =>
        _noticeOverrides.GetValueOrDefault(noticeType)?.IsParallel ?? _notices.IsParallel ?? false;

    private static FrozenDictionary<Type, Route> ToRoutes(
        Dictionary<Type, List<HandlerContract>> contracts,
        string verb,
        Func<HandlerContract, Delegate> invoker) =>
        contracts.ToFrozenDictionary(
            entry => entry.Key,
            entry =>
            {
                Type[] handlerTypes = [.. entry.Value.Select(contract => contract.HandlerType)];
                return new Route(invoker(entry.Value[0]), handlerTypes, new MessageTrace(verb, entry.Key, handlerTypes));
            });

    private Delegate Invoker(HandlerContract contract) => contract.CreateInvoker(InterceptorsFor(contract));

    private Delegate NoticeInvoker(HandlerContract contract) =>
        NoticeDispatch.Create(contract.MessageType, IsParallel(contract.MessageType));

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

    private static void EnsureHandler(Type handlerType, string parameterName)
    {
        if (handlerType.IsAbstract || !handlerType.IsClass || handlerType.ContainsGenericParameters)
        {
            throw new ArgumentException(
                $"Handler '{handlerType}' must be a concrete, closed class. A scan skips abstract classes, so a generic base class has to be abstract.",
                parameterName);
        }

        var contracts = HandlerContract.Of(handlerType).ToArray();
        if (contracts.Length == 0)
        {
            throw new ArgumentException(
                $"'{handlerType}' does not implement {nameof(IOperationHandler<>)}<>, {nameof(IRequestHandler<,>)}<,> or {nameof(INoticeHandler<>)}<>.",
                parameterName);
        }

        // Routing uses a message's exact runtime type, which is never abstract. Such a handler could
        // not run, and for a notice nothing would ever say so, because no handlers is not an error.
        if (contracts.FirstOrDefault(contract => contract.MessageType.IsAbstract) is { } unreachable)
        {
            throw new ArgumentException(
                $"'{handlerType}' handles abstract type '{unreachable.MessageType}', and could never run: messages route on their exact runtime type.",
                parameterName);
        }
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
