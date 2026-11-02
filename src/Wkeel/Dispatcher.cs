namespace Wkeel;

/// <summary>
/// The scoped <see cref="IWkeel"/>. <paramref name="services"/> is the provider of the scope this
/// instance was resolved from, which is what gives handlers the caller's scoped services.
/// </summary>
internal sealed class Dispatcher(IServiceProvider services, Registry registry) : IWkeel
{
    public Task Execute(IOperation operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var invoke = (Func<IServiceProvider, object, CancellationToken, Task>)registry.Operation(operation.GetType());
        return invoke(services, operation, ct);
    }

    public Task<TResult> Execute<TResult>(IRequest<TResult> request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var messageType = request.GetType();

        // The route is typed by the handler's result. A message implementing IRequest<> twice can
        // be executed as the result nobody handles, which is a missing handler, not a bad cast.
        return registry.Request(messageType) is Func<IServiceProvider, object, CancellationToken, Task<TResult>> invoke
            ? invoke(services, request, ct)
            : throw new HandlerNotFoundException(messageType);
    }

    public Task Notify(INotice notice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return registry.Notice(notice.GetType()) is { } invoke ? invoke(services, notice, ct) : Task.CompletedTask;
    }
}
