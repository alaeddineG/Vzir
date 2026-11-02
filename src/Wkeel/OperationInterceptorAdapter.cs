namespace Wkeel;

/// <summary>Puts an <see cref="IOperationInterceptor{TOperation}"/> on the chain, which runs over <see cref="Nothing"/>.</summary>
internal sealed class OperationInterceptorAdapter<TOperation, TInterceptor>(TInterceptor interceptor)
    : IInterceptor<TOperation, Nothing>
    where TOperation : IOperation
    where TInterceptor : IOperationInterceptor<TOperation>
{
    public async Task<Nothing> Intercept(TOperation message, Next<Nothing> next, CancellationToken ct)
    {
        await interceptor.Intercept(message, next.Invoke, ct).ConfigureAwait(false);
        return default;
    }
}
