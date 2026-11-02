namespace Wkeel;

/// <summary>An open-generic interceptor, closed once per message at startup.</summary>
internal sealed class OpenInterceptor
{
    private readonly Type _definition;
    private readonly int _messagePosition;
    private readonly int _resultPosition;

    private OpenInterceptor(Type definition, int messagePosition, int resultPosition)
    {
        _definition = definition;
        _messagePosition = messagePosition;
        _resultPosition = resultPosition;
    }

    public static OpenInterceptor Of(Type type)
    {
        var contracts = type.GetInterfaces()
            .Where(contract => contract.IsConstructedGenericType && contract.GetGenericTypeDefinition() == typeof(IInterceptor<,>))
            .ToArray();

        // The contract's arguments must be the class's own two parameters, in either order, because
        // those are the only two types Wkeel can supply when it closes the class over a message.
        if (type is { IsGenericTypeDefinition: true, IsAbstract: false }
            && type.GetGenericArguments().Length == 2
            && contracts is [var contract]
            && contract.GetGenericArguments() is [{ IsGenericParameter: true } message, { IsGenericParameter: true } result]
            && message != result)
        {
            return new OpenInterceptor(type, message.GenericParameterPosition, result.GenericParameterPosition);
        }

        throw new ArgumentException(
            $"'{type}' must be a concrete class with two type parameters that implements {nameof(IInterceptor<,>)}<,> over them, "
                + "such as typeof(LoggingInterceptor<,>). Closed interceptors use the generic Intercept overloads.",
            nameof(type));
    }

    /// <summary>Closes the interceptor, or returns <see langword="null"/> when the message does not satisfy its constraints.</summary>
    public Type? Close(Type messageType, Type resultType)
    {
        var arguments = new Type[2];
        arguments[_messagePosition] = messageType;
        arguments[_resultPosition] = resultType;

        try
        {
            return _definition.MakeGenericType(arguments);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
