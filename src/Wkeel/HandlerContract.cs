using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Wkeel;

/// <summary>One handler interface implemented by a handler type. Startup only: this is the reflection boundary.</summary>
internal sealed record HandlerContract(Type HandlerType, Type ServiceType, Type MessageType, Type? ResultType)
{
    public bool IsOperation => ResultType is null;

    public static IEnumerable<HandlerContract> Of(Type handlerType)
    {
        foreach (var serviceType in handlerType.GetInterfaces().Where(type => type.IsConstructedGenericType))
        {
            var definition = serviceType.GetGenericTypeDefinition();
            var arguments = serviceType.GetGenericArguments();

            if (definition == typeof(IOperationHandler<>))
            {
                yield return new HandlerContract(handlerType, serviceType, arguments[0], null);
            }
            else if (definition == typeof(IRequestHandler<,>))
            {
                yield return new HandlerContract(handlerType, serviceType, arguments[0], arguments[1]);
            }
        }
    }

    /// <summary>Builds the cached invoker. With no interceptors it skips the pipeline, which is what keeps that path allocation-free.</summary>
    public Delegate CreateInvoker(Type[] interceptors)
    {
        if (interceptors.Length > 0)
        {
            var pipeline = ResultType is null
                ? Method(nameof(OperationPipeline)).MakeGenericMethod(MessageType)
                : Method(nameof(RequestPipeline)).MakeGenericMethod(MessageType, ResultType);

            return (Delegate)pipeline.Invoke(null, [interceptors])!;
        }

        return ResultType is null
            ? Method(nameof(InvokeOperation))
                .MakeGenericMethod(MessageType)
                .CreateDelegate<Func<IServiceProvider, object, CancellationToken, Task>>()
            : Method(nameof(InvokeRequest))
                .MakeGenericMethod(MessageType, ResultType)
                .CreateDelegate(typeof(Func<,,,>).MakeGenericType(
                    typeof(IServiceProvider), typeof(object), typeof(CancellationToken), typeof(Task<>).MakeGenericType(ResultType)));
    }

    private static MethodInfo Method(string name) =>
        typeof(HandlerContract).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Task InvokeOperation<TOperation>(IServiceProvider services, object operation, CancellationToken ct)
        where TOperation : IOperation =>
        services.GetRequiredService<IOperationHandler<TOperation>>().Handle((TOperation)operation, ct);

    private static Task<TResult> InvokeRequest<TRequest, TResult>(IServiceProvider services, object request, CancellationToken ct)
        where TRequest : IRequest<TResult> =>
        services.GetRequiredService<IRequestHandler<TRequest, TResult>>().Handle((TRequest)request, ct);

    private static Func<IServiceProvider, object, CancellationToken, Task<Nothing>> OperationPipeline<TOperation>(Type[] interceptors)
        where TOperation : IOperation =>
        new Pipeline<TOperation, Nothing>(interceptors, RunOperationHandler<TOperation>).Invoke;

    private static Func<IServiceProvider, object, CancellationToken, Task<TResult>> RequestPipeline<TRequest, TResult>(Type[] interceptors)
        where TRequest : IRequest<TResult> =>
        new Pipeline<TRequest, TResult>(interceptors, RunRequestHandler<TRequest, TResult>).Invoke;

    private static async Task<Nothing> RunOperationHandler<TOperation>(IServiceProvider services, TOperation operation, CancellationToken ct)
        where TOperation : IOperation
    {
        await services.GetRequiredService<IOperationHandler<TOperation>>().Handle(operation, ct).ConfigureAwait(false);
        return default;
    }

    private static Task<TResult> RunRequestHandler<TRequest, TResult>(IServiceProvider services, TRequest request, CancellationToken ct)
        where TRequest : IRequest<TResult> =>
        services.GetRequiredService<IRequestHandler<TRequest, TResult>>().Handle(request, ct);
}
