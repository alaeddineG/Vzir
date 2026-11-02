using System.Diagnostics;

namespace Wkeel;

/// <summary>The <see cref="Activity"/> of one message type. Its name and tags are computed once, at startup.</summary>
/// <remarks>
/// Only reached when something listens to the source, because waiting for a handler to finish takes
/// an async method. That method is also what stops <see cref="Activity.Current"/> leaking to the caller.
/// </remarks>
internal sealed class MessageTrace(string verb, Type messageType, IReadOnlyList<Type> handlerTypes)
{
    public const string Execute = nameof(Execute);
    public const string Notify = nameof(Notify);

    private static readonly ActivitySource _source = new("Wkeel");

    private readonly string _name = $"{verb} {messageType.Name}";

    private readonly KeyValuePair<string, object?>[] _tags = Tags(verb, messageType, handlerTypes);

    public static bool IsActive => _source.HasListeners();

    public async Task Run(
        Func<IServiceProvider, object, CancellationToken, Task> invoke,
        IServiceProvider services,
        object message,
        CancellationToken ct)
    {
        using var activity = Start();
        try
        {
            await invoke(services, message, ct).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Fail(activity, exception, ct);
            throw;
        }
    }

    public async Task<TResult> Run<TResult>(
        Func<IServiceProvider, object, CancellationToken, Task<TResult>> invoke,
        IServiceProvider services,
        object message,
        CancellationToken ct)
    {
        using var activity = Start();
        try
        {
            return await invoke(services, message, ct).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Fail(activity, exception, ct);
            throw;
        }
    }

    private static KeyValuePair<string, object?>[] Tags(string verb, Type messageType, IReadOnlyList<Type> handlerTypes)
    {
        KeyValuePair<string, object?>[] tags = [new("wkeel.verb", verb), new("wkeel.message", messageType.ToString())];

        return handlerTypes.Count == 0 ? tags : [.. tags, new("wkeel.handler", string.Join(", ", handlerTypes))];
    }

    private Activity? Start() => _source.StartActivity(_name, ActivityKind.Internal, default(ActivityContext), _tags);

    private static void Fail(Activity? activity, Exception exception, CancellationToken ct)
    {
        // The caller cancelling is not a failure. A handler cancelling on its own still is one.
        if (activity is null || (exception is OperationCanceledException && ct.IsCancellationRequested))
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
    }
}
