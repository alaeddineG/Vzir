using Microsoft.Extensions.DependencyInjection;

namespace Wkeel;

/// <summary>The interceptor chain of one message type, outermost first.</summary>
/// <remarks>
/// The shape of the chain is fixed at startup. Interceptors are resolved from the caller's scope
/// only when the chain reaches them, so a short-circuit does not construct the ones inside it,
/// and a retry gets fresh ones. Each link costs one <see cref="Next{TResult}"/> closure per call.
/// </remarks>
internal sealed class Pipeline<TMessage, TResult>(
    Type[] interceptors,
    Func<IServiceProvider, TMessage, CancellationToken, Task<TResult>> handler)
{
    public Task<TResult> Invoke(IServiceProvider services, object message, CancellationToken ct) =>
        Run(0, services, (TMessage)message, ct);

    private Task<TResult> Run(int index, IServiceProvider services, TMessage message, CancellationToken ct)
    {
        if (index == interceptors.Length)
        {
            return handler(services, message, ct);
        }

        var interceptor = (IInterceptor<TMessage, TResult>)services.GetRequiredService(interceptors[index]);
        return interceptor.Intercept(message, () => Run(index + 1, services, message, ct), ct);
    }
}
