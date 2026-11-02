namespace Wkeel.Tests;

/// <summary>Takes long enough that overlapping handlers cannot finish before the next one starts.</summary>
internal abstract class Listener<TNotice>(Journal journal, string name) : INoticeHandler<TNotice>
    where TNotice : INotice
{
    public async Task Handle(TNotice notice, CancellationToken ct)
    {
        journal.Add($"{name}:in");
        await Task.Delay(20, ct);
        journal.Add($"{name}:out");
    }
}

internal sealed record PatientMoved(int Id) : INotice;

internal sealed class FirstListener(Journal journal) : Listener<PatientMoved>(journal, "first");

internal sealed class SecondListener(Journal journal) : Listener<PatientMoved>(journal, "second");

internal sealed class ThirdListener(Journal journal) : Listener<PatientMoved>(journal, "third");

internal sealed class CannotBeConstructed : INoticeHandler<PatientMoved>
{
    public CannotBeConstructed() => throw new InvalidOperationException("constructor");

    public Task Handle(PatientMoved notice, CancellationToken ct) => Task.CompletedTask;
}

internal sealed record ThreadNotice : INotice;

internal sealed class ThreadListener(Journal journal) : INoticeHandler<ThreadNotice>
{
    public Task Handle(ThreadNotice notice, CancellationToken ct)
    {
        journal.Add(Environment.CurrentManagedThreadId);
        return Task.CompletedTask;
    }
}

internal sealed record OtherNotice : INotice;

internal sealed class OtherListener(Journal journal) : Listener<OtherNotice>(journal, "other");

internal readonly record struct ValueNotice(int Id) : INotice;

internal sealed class ValueListener(Journal journal) : INoticeHandler<ValueNotice>
{
    public Task Handle(ValueNotice notice, CancellationToken ct)
    {
        journal.Add(notice);
        return Task.CompletedTask;
    }
}

internal record BaseNotice : INotice;

internal sealed record DerivedNotice : BaseNotice;

internal sealed class BaseNoticeListener(Journal journal) : INoticeHandler<BaseNotice>
{
    public Task Handle(BaseNotice notice, CancellationToken ct)
    {
        journal.Add(notice);
        return Task.CompletedTask;
    }
}

/// <summary>Each side waits for the other, so these only finish when they really overlap.</summary>
internal sealed class Rendezvous
{
    public TaskCompletionSource Left { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Right { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record Meet : INotice;

internal sealed class MeetLeft(Rendezvous rendezvous) : INoticeHandler<Meet>
{
    public async Task Handle(Meet notice, CancellationToken ct)
    {
        rendezvous.Left.TrySetResult();
        await rendezvous.Right.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
    }
}

internal sealed class MeetRight(Rendezvous rendezvous) : INoticeHandler<Meet>
{
    public async Task Handle(Meet notice, CancellationToken ct)
    {
        rendezvous.Right.TrySetResult();
        await rendezvous.Left.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
    }
}

internal sealed record Trouble : INotice;

internal sealed class BeforeTrouble(Journal journal) : Listener<Trouble>(journal, "before");

internal sealed class AfterTrouble(Journal journal) : Listener<Trouble>(journal, "after");

internal sealed class ThrowsBeforeReturningTask : INoticeHandler<Trouble>
{
    public Task Handle(Trouble notice, CancellationToken ct) => throw new InvalidOperationException("synchronous");
}

internal sealed class ThrowsAfterAwaiting : INoticeHandler<Trouble>
{
    public async Task Handle(Trouble notice, CancellationToken ct)
    {
        await Task.Yield();
        throw new TimeoutException("asynchronous");
    }
}

internal sealed class ObservesToken : INoticeHandler<Trouble>
{
    public Task Handle(Trouble notice, CancellationToken ct) => Task.Delay(TimeSpan.FromSeconds(10), ct);
}

internal sealed class ObservesTokenToo : INoticeHandler<Trouble>
{
    public Task Handle(Trouble notice, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

/// <summary>Cancels on its own account, such as a timeout inside an HTTP call.</summary>
internal sealed class CancelsItself : INoticeHandler<Trouble>
{
    public Task Handle(Trouble notice, CancellationToken ct) => throw new OperationCanceledException("own timeout");
}

internal sealed record TokenNotice : INotice;

internal sealed class TokenListener(Journal journal) : INoticeHandler<TokenNotice>
{
    public Task Handle(TokenNotice notice, CancellationToken ct)
    {
        journal.Add(ct);
        return Task.CompletedTask;
    }
}

internal sealed record ScopeNotice : INotice;

internal sealed class ScopeListener(Journal journal, ScopedProbe probe) : INoticeHandler<ScopeNotice>
{
    public Task Handle(ScopeNotice notice, CancellationToken ct)
    {
        journal.Add(probe);
        return Task.CompletedTask;
    }
}

internal sealed class SecondScopeListener(Journal journal, ScopedProbe probe) : INoticeHandler<ScopeNotice>
{
    public Task Handle(ScopeNotice notice, CancellationToken ct)
    {
        journal.Add(probe);
        return Task.CompletedTask;
    }
}

internal sealed record InstanceNotice : INotice;

internal sealed class InstanceListener(Journal journal) : INoticeHandler<InstanceNotice>
{
    public Task Handle(InstanceNotice notice, CancellationToken ct)
    {
        journal.Add(this);
        return Task.CompletedTask;
    }
}

internal sealed record Announce(int Id) : IOperation;

/// <summary>The PRD's flow: an operation handler that raises a notice in the same scope.</summary>
internal sealed class AnnounceHandler(IWkeel wkeel) : IOperationHandler<Announce>
{
    public Task Handle(Announce operation, CancellationToken ct) => wkeel.Notify(new PatientMoved(operation.Id), ct);
}

internal sealed class HandlesEveryKind(Journal journal)
    : IOperationHandler<Archive>, IRequestHandler<GetName, string>, INoticeHandler<PatientMoved>, INoticeHandler<OtherNotice>
{
    public Task Handle(Archive operation, CancellationToken ct) => Record(operation);

    public Task<string> Handle(GetName request, CancellationToken ct) => Task.FromResult("every-kind");

    public Task Handle(PatientMoved notice, CancellationToken ct) => Record(notice);

    public Task Handle(OtherNotice notice, CancellationToken ct) => Record(notice);

    private Task Record(object message)
    {
        journal.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class HandlesEveryNotice : INoticeHandler<INotice>
{
    public Task Handle(INotice notice, CancellationToken ct) => Task.CompletedTask;
}

internal abstract record AbstractOperation : IOperation;

internal sealed class HandlesAbstractOperation : IOperationHandler<AbstractOperation>
{
    public Task Handle(AbstractOperation operation, CancellationToken ct) => Task.CompletedTask;
}

internal abstract record AbstractRequest : IRequest<int>;

internal sealed class HandlesAbstractRequest : IRequestHandler<AbstractRequest, int>
{
    public Task<int> Handle(AbstractRequest request, CancellationToken ct) => Task.FromResult(1);
}

/// <summary>Ordinal puts C before b. A culture-aware comparison would put them the other way round.</summary>
internal sealed class CasedListenerC(Journal journal) : Listener<PatientMoved>(journal, "C");

internal sealed class CasedListenerb(Journal journal) : Listener<PatientMoved>(journal, "b");
