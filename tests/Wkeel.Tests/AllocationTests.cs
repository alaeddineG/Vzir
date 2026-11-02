using Microsoft.Extensions.DependencyInjection;
using static Wkeel.Tests.Fixture;

namespace Wkeel.Tests;

/// <summary>Guards N3. A slip onto the pipeline changes no behavior, so only measuring catches it.</summary>
[Collection(nameof(ActivityListeners))]
public sealed class AllocationTests
{
    [Fact]
    public void Execute_without_interceptors_allocates_nothing()
    {
        // Scoped, so the handler instance is not part of the measurement.
        using var provider = Provider(w => w
            .HandlerLifetime(ServiceLifetime.Scoped)
            .AddHandler<CachedOperationHandler>()
            .AddHandler<CachedRequestHandler>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();
        IOperation operation = new CachedOperation();
        var request = new CachedRequest();

        Measure(() => wkeel.Execute(operation, CancellationToken.None));
        Measure(() => wkeel.Execute(request, CancellationToken.None));

        Assert.True(Measure(() => wkeel.Execute(operation, CancellationToken.None)) < 1);
        Assert.True(Measure(() => wkeel.Execute(request, CancellationToken.None)) < 1);
    }

    // An async wrapper can hide from the measurement above: it allocates nothing when it finishes
    // synchronously with a cached result. N3 promises more than that, so check the task itself.
    [Fact]
    public void Execute_without_interceptors_or_a_listener_returns_the_handlers_own_task()
    {
        using var provider = Provider(w => w.AddHandler<CachedOperationHandler>().AddHandler<CachedRequestHandler>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        Assert.Same(CachedOperationHandler.Result, wkeel.Execute(new CachedOperation(), CancellationToken.None));
        Assert.Same(CachedRequestHandler.Result, wkeel.Execute(new CachedRequest(), CancellationToken.None));
    }

    private static double Measure(Func<Task> execute)
    {
        const int Calls = 20_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Calls; i++)
        {
            execute();
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)Calls;
    }
}

internal sealed record CachedOperation : IOperation;

internal sealed class CachedOperationHandler : IOperationHandler<CachedOperation>
{
    public static Task Result { get; } = Task.FromResult(1);

    public Task Handle(CachedOperation operation, CancellationToken ct) => Result;
}

internal sealed record CachedRequest : IRequest<int>;

internal sealed class CachedRequestHandler : IRequestHandler<CachedRequest, int>
{
    public static Task<int> Result { get; } = Task.FromResult(1);

    public Task<int> Handle(CachedRequest request, CancellationToken ct) => Result;
}
