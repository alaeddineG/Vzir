namespace Wkeel;

/// <summary>Thrown when a message is executed and no handler is registered for its type.</summary>
public sealed class HandlerNotFoundException : InvalidOperationException
{
    /// <summary>Creates the exception for a message type.</summary>
    /// <param name="messageType">The message type that has no handler.</param>
    public HandlerNotFoundException(Type messageType)
        : base($"No handler is registered for message type '{messageType}'.")
    {
        MessageType = messageType;
    }

    /// <summary>Gets the message type that has no handler.</summary>
    public Type MessageType { get; }
}
