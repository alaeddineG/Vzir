namespace Wkeel;

/// <summary>The entry point application code talks to.</summary>
/// <remarks>
/// Registered as scoped. Handlers are resolved from the scope this instance was resolved in,
/// so they share the caller's scoped services. Wkeel never creates a scope of its own.
/// </remarks>
public interface IWkeel
{
    /// <summary>Routes an operation to its one handler.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="ct">The token to observe for cancellation.</param>
    /// <returns>A task that completes when the handler has finished.</returns>
    /// <exception cref="HandlerNotFoundException">No handler is registered for the operation's type.</exception>
    /// <exception cref="AmbiguousHandlerException">More than one handler is registered for the operation's type.</exception>
    Task Execute(IOperation operation, CancellationToken ct);

    /// <summary>Routes a request to its one handler.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="ct">The token to observe for cancellation.</param>
    /// <returns>A task that completes with the handler's result.</returns>
    /// <exception cref="HandlerNotFoundException">No handler is registered for the request's type.</exception>
    /// <exception cref="AmbiguousHandlerException">More than one handler is registered for the request's type.</exception>
    Task<TResult> Execute<TResult>(IRequest<TResult> request, CancellationToken ct);
}
