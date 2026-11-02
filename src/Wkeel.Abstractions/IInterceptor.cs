namespace Wkeel;

/// <summary>Wraps <c>Execute</c>. Sees the message and result, and may short-circuit by not calling next.</summary>
/// <remarks>
/// Declare it open generic to cover every message that satisfies its constraints, or close it
/// over one request. To intercept one operation, use <see cref="IOperationInterceptor{TOperation}"/>.
/// </remarks>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResult">The type of the result.</typeparam>
public interface IInterceptor<in TMessage, TResult>
{
    /// <summary>Runs around the rest of the chain.</summary>
    /// <param name="message">The message being executed.</param>
    /// <param name="next">Continues the chain. May be called zero, one or several times.</param>
    /// <param name="ct">The token to observe for cancellation.</param>
    /// <returns>A task that completes with the result.</returns>
    Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct);
}
