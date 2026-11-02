namespace Wkeel;

/// <summary>
/// The scoped <see cref="IWkeel"/>. <paramref name="services"/> is the provider of the scope this
/// instance was resolved from, which is what gives handlers the caller's scoped services.
/// </summary>
internal sealed class Dispatcher(IServiceProvider services, Registry registry) : IWkeel
{
    private static readonly Func<IServiceProvider, object, CancellationToken, Task> _nobody = (_, _, _) => Task.CompletedTask;

    public Task Execute(IOperation operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var route = registry.Operation(operation.GetType());
        var invoke = (Func<IServiceProvider, object, CancellationToken, Task>)route.Invoker;

        return MessageTrace.IsActive ? route.Trace.Run(invoke, services, operation, ct) : invoke(services, operation, ct);
    }

    public Task<TResult> Execute<TResult>(IRequest<TResult> request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var messageType = request.GetType();
        var route = registry.Request(messageType);

        // The route is typed by the handler's result. A message implementing IRequest<> twice can
        // be executed as the result nobody handles, which is a missing handler, not a bad cast.
        if (route.Invoker is not Func<IServiceProvider, object, CancellationToken, Task<TResult>> invoke)
        {
            throw new HandlerNotFoundException(messageType);
        }

        return MessageTrace.IsActive ? route.Trace.Run(invoke, services, request, ct) : invoke(services, request, ct);
    }

    public Task Notify(INotice notice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notice);

        var noticeType = notice.GetType();
        if (registry.Notice(noticeType) is { } route)
        {
            var invoke = (Func<IServiceProvider, object, CancellationToken, Task>)route.Invoker;

            return MessageTrace.IsActive ? route.Trace.Run(invoke, services, notice, ct) : invoke(services, notice, ct);
        }

        // Nobody listening is not an error, so a trace is the only place that can show it happened.
        return MessageTrace.IsActive
            ? new MessageTrace(MessageTrace.Notify, noticeType, []).Run(_nobody, services, notice, ct)
            : Task.CompletedTask;
    }
}
