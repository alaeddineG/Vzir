using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Wkeel.Tests;

internal static class Fixture
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>What listeners journal when each finishes before the next one starts.</summary>
    public static string[] Sequence(params string[] names) => [.. names.SelectMany(name => new[] { $"{name}:in", $"{name}:out" })];

    public static ServiceProvider Provider(Action<WkeelBuilder> configure, Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection();
        services?.Invoke(collection);
        collection.AddWkeel(configure);
        return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

/// <summary>Records what handlers saw, shared across scopes as a singleton.</summary>
internal sealed class Journal
{
    public ConcurrentQueue<object> Entries { get; } = new();

    public void Add(object entry) => Entries.Enqueue(entry);
}

internal sealed class ScopedProbe;

internal sealed record Archive(int Id) : IOperation;

internal sealed class ArchiveHandler(Journal journal) : IOperationHandler<Archive>
{
    public Task Handle(Archive operation, CancellationToken ct)
    {
        journal.Add(operation);
        return Task.CompletedTask;
    }
}

internal sealed class SecondArchiveHandler : IOperationHandler<Archive>
{
    public Task Handle(Archive operation, CancellationToken ct) => Task.CompletedTask;
}

internal sealed record GetName(int Id) : IRequest<string>;

internal sealed class GetNameHandler : IRequestHandler<GetName, string>
{
    public Task<string> Handle(GetName request, CancellationToken ct) => Task.FromResult($"patient-{request.Id}");
}

internal sealed class SecondGetNameHandler : IRequestHandler<GetName, string>
{
    public Task<string> Handle(GetName request, CancellationToken ct) => Task.FromResult("second");
}

internal sealed record GetTitle(int Id) : IRequest<string>;

internal sealed class GetTitleHandler : IRequestHandler<GetTitle, string>
{
    public Task<string> Handle(GetTitle request, CancellationToken ct) => Task.FromResult($"title-{request.Id}");
}

internal readonly record struct Touch(int Id) : IOperation;

internal sealed class TouchHandler(Journal journal) : IOperationHandler<Touch>
{
    public Task Handle(Touch operation, CancellationToken ct)
    {
        journal.Add(operation);
        return Task.CompletedTask;
    }
}

internal sealed record CaptureToken : IRequest<CancellationToken>;

internal sealed class CaptureTokenHandler : IRequestHandler<CaptureToken, CancellationToken>
{
    public Task<CancellationToken> Handle(CaptureToken request, CancellationToken ct) => Task.FromResult(ct);
}

internal sealed record Fail : IOperation;

internal sealed class FailHandler : IOperationHandler<Fail>
{
    public Task Handle(Fail operation, CancellationToken ct) => throw new TimeoutException("from handler");
}

internal sealed record FailRequest : IRequest<int>;

internal sealed class FailRequestHandler : IRequestHandler<FailRequest, int>
{
    public async Task<int> Handle(FailRequest request, CancellationToken ct)
    {
        await Task.Yield();
        throw new TimeoutException("from handler");
    }
}

internal record BaseMessage : IOperation;

internal sealed record DerivedMessage : BaseMessage;

internal sealed class BaseMessageHandler : IOperationHandler<BaseMessage>
{
    public Task Handle(BaseMessage operation, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>One class handling several messages must be routed for all of them.</summary>
internal sealed class MultiHandler(Journal journal) : IOperationHandler<Archive>, IRequestHandler<GetName, string>
{
    public Task Handle(Archive operation, CancellationToken ct)
    {
        journal.Add(operation);
        return Task.CompletedTask;
    }

    public Task<string> Handle(GetName request, CancellationToken ct) => Task.FromResult("multi");
}

internal sealed record TwoResults : IRequest<int>, IRequest<string>;

internal sealed class TwoResultsIntHandler : IRequestHandler<TwoResults, int>
{
    public Task<int> Handle(TwoResults request, CancellationToken ct) => Task.FromResult(1);
}

internal sealed class TwoResultsStringHandler : IRequestHandler<TwoResults, string>
{
    public Task<string> Handle(TwoResults request, CancellationToken ct) => Task.FromResult("text");
}

internal sealed record BothKinds : IOperation, IRequest<int>;

internal sealed class BothKindsRequestHandler : IRequestHandler<BothKinds, int>
{
    public Task<int> Handle(BothKinds request, CancellationToken ct) => Task.FromResult(1);
}

internal sealed record WhoIsScoped : IRequest<ScopedProbe>;

internal sealed class WhoIsScopedHandler(ScopedProbe probe) : IRequestHandler<WhoIsScoped, ScopedProbe>
{
    public Task<ScopedProbe> Handle(WhoIsScoped request, CancellationToken ct) => Task.FromResult(probe);
}

internal sealed record WhoIsScopedNested : IRequest<ScopedProbe>;

internal sealed class WhoIsScopedNestedHandler(IWkeel wkeel) : IRequestHandler<WhoIsScopedNested, ScopedProbe>
{
    public Task<ScopedProbe> Handle(WhoIsScopedNested request, CancellationToken ct) => wkeel.Execute(new WhoIsScoped(), ct);
}

internal sealed record Instance : IRequest<StatefulHandler>;

internal sealed class StatefulHandler : IRequestHandler<Instance, StatefulHandler>
{
    public Task<StatefulHandler> Handle(Instance request, CancellationToken ct) => Task.FromResult(this);
}

internal sealed record OtherInstance : IRequest<OtherStatefulHandler>;

internal sealed class OtherStatefulHandler : IRequestHandler<OtherInstance, OtherStatefulHandler>
{
    public Task<OtherStatefulHandler> Handle(OtherInstance request, CancellationToken ct) => Task.FromResult(this);
}

internal sealed record Echo(int Value) : IRequest<int>;

internal sealed class EchoHandler : IRequestHandler<Echo, int>
{
    public async Task<int> Handle(Echo request, CancellationToken ct)
    {
        await Task.Yield();
        return request.Value;
    }
}

internal sealed class NotAHandler;

internal abstract class AbstractHandler : IOperationHandler<Archive>
{
    public abstract Task Handle(Archive operation, CancellationToken ct);
}

/// <summary>
/// An <c>ActivityListener</c> is process-wide. These tests are kept apart from the rest so that one
/// cannot put another test on the traced path, least of all one that is measuring allocations.
/// </summary>
[CollectionDefinition(nameof(ActivityListeners), DisableParallelization = true)]
public sealed class ActivityListeners;
