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

    public Delegate CreateInvoker() =>
        ResultType is null
            ? Method(nameof(InvokeOperation))
                .MakeGenericMethod(MessageType)
                .CreateDelegate<Func<IServiceProvider, object, CancellationToken, Task>>()
            : Method(nameof(InvokeRequest))
                .MakeGenericMethod(MessageType, ResultType)
                .CreateDelegate(typeof(Func<,,,>).MakeGenericType(
                    typeof(IServiceProvider), typeof(object), typeof(CancellationToken), typeof(Task<>).MakeGenericType(ResultType)));

    private static MethodInfo Method(string name) =>
        typeof(HandlerContract).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Task InvokeOperation<TOperation>(IServiceProvider services, object operation, CancellationToken ct)
        where TOperation : IOperation =>
        services.GetRequiredService<IOperationHandler<TOperation>>().Handle((TOperation)operation, ct);

    private static Task<TResult> InvokeRequest<TRequest, TResult>(IServiceProvider services, object request, CancellationToken ct)
        where TRequest : IRequest<TResult> =>
        services.GetRequiredService<IRequestHandler<TRequest, TResult>>().Handle((TRequest)request, ct);
}
