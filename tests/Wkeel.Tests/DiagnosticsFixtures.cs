using System.Diagnostics;

namespace Wkeel.Tests;

internal sealed record SeeActivity : IOperation;

internal sealed class SeeActivityHandler(Journal journal) : IOperationHandler<SeeActivity>
{
    public Task Handle(SeeActivity operation, CancellationToken ct)
    {
        journal.Add(Activity.Current!);
        return Task.CompletedTask;
    }
}

internal sealed class SeeActivityInterceptor<TMessage, TResult>(Journal journal) : IInterceptor<TMessage, TResult>
{
    public Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct)
    {
        journal.Add(Activity.Current!);
        return next();
    }
}

internal sealed record SeeNotice : INotice;

internal sealed class SeeNoticeListener(Journal journal) : INoticeHandler<SeeNotice>
{
    public async Task Handle(SeeNotice notice, CancellationToken ct)
    {
        await Task.Yield();
        journal.Add(Activity.Current!);
    }
}

internal sealed class SecondSeeNoticeListener(Journal journal) : INoticeHandler<SeeNotice>
{
    public async Task Handle(SeeNotice notice, CancellationToken ct)
    {
        await Task.Yield();
        journal.Add(Activity.Current!);
    }
}

internal sealed record Held : IOperation;

/// <summary>Stays running until the test lets go, to show the activity lasts as long as the handler.</summary>
internal sealed class HeldHandler(Rendezvous rendezvous) : IOperationHandler<Held>
{
    public async Task Handle(Held operation, CancellationToken ct)
    {
        rendezvous.Left.TrySetResult();
        await rendezvous.Right.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
    }
}

internal sealed record CancelledByCaller : IRequest<int>;

internal sealed class CancelledByCallerHandler : IRequestHandler<CancelledByCaller, int>
{
    public Task<int> Handle(CancelledByCaller request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(1);
    }
}
