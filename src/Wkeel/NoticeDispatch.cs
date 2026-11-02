using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace Wkeel;

/// <summary>The fan-out for one notice type. Notices have no pipeline: interceptors wrap <c>Execute</c> only.</summary>
internal static class NoticeDispatch
{
    public static Func<IServiceProvider, object, CancellationToken, Task> Create(Type noticeType, bool parallel) =>
        typeof(NoticeDispatch)
            .GetMethod(parallel ? nameof(InParallel) : nameof(InSequence), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(noticeType)
            .CreateDelegate<Func<IServiceProvider, object, CancellationToken, Task>>();

    private static async Task InSequence<TNotice>(IServiceProvider services, object notice, CancellationToken ct)
        where TNotice : INotice
    {
        foreach (var handler in services.GetServices<INoticeHandler<TNotice>>())
        {
            await handler.Handle((TNotice)notice, ct).ConfigureAwait(false);
        }
    }

    private static async Task InParallel<TNotice>(IServiceProvider services, object notice, CancellationToken ct)
        where TNotice : INotice
    {
        var outcomes = await Task
            .WhenAll(services.GetServices<INoticeHandler<TNotice>>().Select(handler => Run(handler, (TNotice)notice, ct)))
            .ConfigureAwait(false);

        var failures = outcomes.OfType<Exception>().ToArray();
        if (failures.Length == 0)
        {
            return;
        }

        // The caller cancelling is not a failure, and wrapping it would hide it from anything
        // catching OperationCanceledException. A handler cancelling on its own still is one.
        if (ct.IsCancellationRequested && failures.All(failure => failure is OperationCanceledException))
        {
            ExceptionDispatchInfo.Throw(failures[0]);
        }

        throw new AggregateException(failures);
    }

    // Capturing here, rather than reading Task.WhenAll's exception, keeps a handler that throws before
    // returning a Task from stopping the others, and keeps failures in registration order.
    private static async Task<Exception?> Run<TNotice>(INoticeHandler<TNotice> handler, TNotice notice, CancellationToken ct)
        where TNotice : INotice
    {
        try
        {
            await handler.Handle(notice, ct).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
