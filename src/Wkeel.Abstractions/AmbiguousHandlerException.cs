namespace Wkeel;

/// <summary>Thrown when a message is executed and more than one handler is registered for its type.</summary>
public sealed class AmbiguousHandlerException : InvalidOperationException
{
    /// <summary>Creates the exception for a message type and its competing handlers.</summary>
    /// <param name="messageType">The message type with more than one handler.</param>
    /// <param name="handlerTypes">The competing handler types.</param>
    public AmbiguousHandlerException(Type messageType, IReadOnlyList<Type> handlerTypes)
        : base($"Message type '{messageType}' has {handlerTypes.Count} handlers, expected one: {string.Join(", ", handlerTypes)}.")
    {
        MessageType = messageType;
        HandlerTypes = handlerTypes;
    }

    /// <summary>Gets the message type with more than one handler.</summary>
    public Type MessageType { get; }

    /// <summary>Gets the competing handler types.</summary>
    public IReadOnlyList<Type> HandlerTypes { get; }
}
