using System.Reflection;

namespace Wkeel.Tests;

/// <summary>An assembly of chosen types, for shapes that could not live side by side in a real one.</summary>
internal sealed class TypesAssembly(params Type[] types) : Assembly
{
    public override Type[] GetTypes() => types;
}

internal sealed class OpenListener<TNotice> : INoticeHandler<TNotice>
    where TNotice : INotice
{
    public Task Handle(TNotice notice, CancellationToken ct) => Task.CompletedTask;
}

internal readonly struct StructHandler : IOperationHandler<Archive>
{
    public Task Handle(Archive operation, CancellationToken ct) => Task.CompletedTask;
}

internal sealed record OpenMessage<T> : IRequest<T>;

internal sealed record UnhandledNotice : INotice;

internal sealed class BothKindsOperationHandler : IOperationHandler<BothKinds>
{
    public Task Handle(BothKinds operation, CancellationToken ct) => Task.CompletedTask;
}
