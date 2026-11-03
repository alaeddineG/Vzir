using System.Collections.Frozen;

namespace Wkeel;

/// <summary>The cached invoker for one message type. <c>Invoker</c> is typed by the message's result.</summary>
internal sealed record Route(Delegate Invoker, IReadOnlyList<Type> HandlerTypes, MessageTrace Trace);

/// <summary>Message type to route, built once at startup.</summary>
/// <remarks>
/// Operations and requests are kept apart so that a type implementing both markers cannot reach
/// the wrong handler kind.
/// </remarks>
internal sealed class Registry(
    FrozenDictionary<Type, Route> operations,
    FrozenDictionary<Type, Route> requests,
    FrozenDictionary<Type, Route> notices) : IWkeelRegistry
{
    public IReadOnlyDictionary<Type, IReadOnlyList<Type>> Operations { get; } = HandlersOf(operations);

    public IReadOnlyDictionary<Type, IReadOnlyList<Type>> Requests { get; } = HandlersOf(requests);

    public IReadOnlyDictionary<Type, IReadOnlyList<Type>> Notices { get; } = HandlersOf(notices);

    public Route Operation(Type messageType) => Find(operations, messageType);

    public Route Request(Type messageType) => Find(requests, messageType);

    /// <summary>Finds the fan-out for a notice, or <see langword="null"/> when nothing handles it, which is not an error.</summary>
    public Route? Notice(Type noticeType) => notices.GetValueOrDefault(noticeType);

    private static FrozenDictionary<Type, IReadOnlyList<Type>> HandlersOf(FrozenDictionary<Type, Route> routes) =>
        routes.ToFrozenDictionary(entry => entry.Key, entry => entry.Value.HandlerTypes);

    private static Route Find(FrozenDictionary<Type, Route> routes, Type messageType)
    {
        if (!routes.TryGetValue(messageType, out var route))
        {
            throw new HandlerNotFoundException(messageType);
        }

        if (route.HandlerTypes.Count > 1)
        {
            throw new AmbiguousHandlerException(messageType, route.HandlerTypes);
        }

        return route;
    }
}
