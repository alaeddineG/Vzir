namespace Wkeel;

/// <summary>Handles one <see cref="IRequest{TResult}"/> type.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult">The type of the result.</typeparam>
public interface IRequestHandler<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    /// <summary>Handles the request.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">The token to observe for cancellation.</param>
    /// <returns>A task that completes with the result.</returns>
    Task<TResult> Handle(TRequest request, CancellationToken ct);
}
