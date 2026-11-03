namespace Wkeel;

/// <summary>Lists the messages Wkeel routes and their handlers, for tooling such as documentation generators.</summary>
/// <remarks>
/// Registered as a singleton, and fixed once <c>AddWkeel</c> returns. Each property maps a message type to
/// its handler types in registration order. A message with several handlers is listed with all of them,
/// even where <see cref="IWkeel"/> would reject it. A message with no handler is not listed.
/// </remarks>
public interface IWkeelRegistry
{
    /// <summary>Gets the operation types that have a handler.</summary>
    IReadOnlyDictionary<Type, IReadOnlyList<Type>> Operations { get; }

    /// <summary>Gets the request types that have a handler.</summary>
    IReadOnlyDictionary<Type, IReadOnlyList<Type>> Requests { get; }

    /// <summary>Gets the notice types that have a handler.</summary>
    IReadOnlyDictionary<Type, IReadOnlyList<Type>> Notices { get; }
}
