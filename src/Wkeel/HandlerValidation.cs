namespace Wkeel;

/// <summary>What <see cref="WkeelBuilder.ValidateOnBuild"/> checks. Startup only.</summary>
internal static class HandlerValidation
{
    /// <summary>Reports every missing and duplicate handler at once.</summary>
    /// <param name="scannedTypes">
    /// Where messages are found. Nothing else can say a message exists, so one outside these is not checked.
    /// </param>
    /// <param name="operations">The registered operation handlers, by message type.</param>
    /// <param name="requests">The registered request handlers, by message type.</param>
    public static void Validate(
        IEnumerable<Type> scannedTypes,
        Dictionary<Type, List<HandlerContract>> operations,
        Dictionary<Type, List<HandlerContract>> requests)
    {
        var missing = scannedTypes
            .Where(type => !type.IsAbstract && !type.ContainsGenericParameters)
            .Where(type => !IsHandled(type, operations, requests))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new HandlerNotFoundException(type));

        var duplicates = operations
            .Concat(requests)
            .Where(route => route.Value.Count > 1)
            .Select(route => new AmbiguousHandlerException(route.Key, [.. route.Value.Select(contract => contract.HandlerType)]));

        Exception[] failures = [.. missing, .. duplicates];
        if (failures.Length > 0)
        {
            throw new AggregateException("Wkeel handler validation failed.", failures);
        }
    }

    // Answers for what Execute would do, so a type that is no message passes, and one implementing
    // IRequest<> twice fails unless each result has a handler, which is then reported as a duplicate.
    private static bool IsHandled(
        Type type,
        Dictionary<Type, List<HandlerContract>> operations,
        Dictionary<Type, List<HandlerContract>> requests)
    {
        if (typeof(IOperation).IsAssignableFrom(type) && !operations.ContainsKey(type))
        {
            return false;
        }

        return type.GetInterfaces()
            .Where(contract => contract.IsConstructedGenericType && contract.GetGenericTypeDefinition() == typeof(IRequest<>))
            .All(contract => requests.TryGetValue(type, out var handlers)
                && handlers.Any(handler => handler.ResultType == contract.GetGenericArguments()[0]));
    }
}
