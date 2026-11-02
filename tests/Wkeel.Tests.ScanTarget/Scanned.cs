namespace Wkeel.Tests.ScanTarget;

// A real assembly to scan, because visibility is what the runtime's GetTypes decides. Every message
// here has a handler, so it also passes validation. Shapes that must fail are tested from Wkeel.Tests.
public sealed record ScannedRequest : IRequest<string>;

public sealed record ScannedOperation(ICollection<string> Seen) : IOperation;

public sealed record ScannedNotice(ICollection<string> Seen) : INotice;

public sealed class PublicRequestHandler : IRequestHandler<ScannedRequest, string>
{
    public Task<string> Handle(ScannedRequest request, CancellationToken ct) => Task.FromResult(nameof(PublicRequestHandler));
}

internal sealed class InternalOperationHandler : IOperationHandler<ScannedOperation>
{
    public Task Handle(ScannedOperation operation, CancellationToken ct)
    {
        operation.Seen.Add(nameof(InternalOperationHandler));
        return Task.CompletedTask;
    }
}

public static class Container
{
    private sealed class NestedPrivateListener : INoticeHandler<ScannedNotice>
    {
        public Task Handle(ScannedNotice notice, CancellationToken ct)
        {
            notice.Seen.Add(nameof(NestedPrivateListener));
            return Task.CompletedTask;
        }
    }
}

internal abstract class ListenerBase<TNotice> : INoticeHandler<TNotice>
    where TNotice : ScannedNoticeBase
{
    public abstract Task Handle(TNotice notice, CancellationToken ct);
}

public abstract record ScannedNoticeBase : INotice;

public sealed record DerivedScannedNotice(ICollection<string> Seen) : ScannedNoticeBase;

internal sealed class ClosedListener : ListenerBase<DerivedScannedNotice>
{
    public override Task Handle(DerivedScannedNotice notice, CancellationToken ct)
    {
        notice.Seen.Add(nameof(ClosedListener));
        return Task.CompletedTask;
    }
}

public sealed class Bystander;
