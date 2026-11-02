using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Wkeel.Benchmarks;

/// <summary>N3 and N4: what <c>Execute</c> costs over calling the handler, with no interceptors or listener.</summary>
[MemoryDiagnoser]
public class ExecuteBenchmarks
{
    private readonly IOperationHandler<Ping> _handler = new PingHandler();
    private readonly IOperation _ping = new Ping();
    private readonly Echo _echo = new();

    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private IServiceProvider _services = null!;
    private IWkeel _scoped = null!;
    private IWkeel _transient = null!;
    private IServiceScope _transientScope = null!;
    private ServiceProvider _transientProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        _provider = new ServiceCollection()
            .AddWkeel(w => w
                .HandlerLifetime(ServiceLifetime.Scoped)
                .AddHandler<PingHandler>()
                .AddHandler<EchoHandler>())
            .BuildServiceProvider();
        _scope = _provider.CreateScope();
        _services = _scope.ServiceProvider;
        _scoped = _services.GetRequiredService<IWkeel>();

        _transientProvider = new ServiceCollection()
            .AddWkeel(w => w.AddHandler<PingHandler>())
            .BuildServiceProvider();
        _transientScope = _transientProvider.CreateScope();
        _transient = _transientScope.ServiceProvider.GetRequiredService<IWkeel>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
        _transientScope.Dispose();
        _transientProvider.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Handler called directly")]
    public Task Direct() => _handler.Handle((Ping)_ping, CancellationToken.None);

    [Benchmark(Description = "Handler resolved from DI, then called")]
    public Task Resolved() => _services.GetRequiredService<IOperationHandler<Ping>>().Handle((Ping)_ping, CancellationToken.None);

    [Benchmark(Description = "Execute operation, scoped handler")]
    public Task ExecuteScoped() => _scoped.Execute(_ping, CancellationToken.None);

    [Benchmark(Description = "Execute request, scoped handler")]
    public Task<int> ExecuteRequest() => _scoped.Execute(_echo, CancellationToken.None);

    [Benchmark(Description = "Execute operation, transient handler")]
    public Task ExecuteTransient() => _transient.Execute(_ping, CancellationToken.None);
}
