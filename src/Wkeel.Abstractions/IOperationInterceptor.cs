namespace Wkeel;

/// <summary>Wraps <c>Execute</c> for one <see cref="IOperation"/> type.</summary>
/// <remarks>
/// Exists because an operation has no result type to close <see cref="IInterceptor{TMessage, TResult}"/>
/// over. Open-generic interceptors cover operations without it.
/// </remarks>
/// <typeparam name="TOperation">The operation type.</typeparam>
public interface IOperationInterceptor<in TOperation>
    where TOperation : IOperation
{
    /// <summary>Runs around the rest of the chain.</summary>
    /// <param name="operation">The operation being executed.</param>
    /// <param name="next">Continues the chain. May be called zero, one or several times.</param>
    /// <param name="ct">The token to observe for cancellation.</param>
    /// <returns>A task that completes when the operation has been handled.</returns>
    Task Intercept(TOperation operation, Next next, CancellationToken ct);
}
