using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Wkeel.Benchmarks;

/// <summary>What a listener on the <c>Wkeel</c> source costs. With none, see <see cref="ExecuteBenchmarks"/>.</summary>
[MemoryDiagnoser]
public class TracingBenchmarks
{
    private readonly IOperation _ping = new Ping();

    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private IWkeel _wkeel = null!;
    private ActivityListener _listener = null!;

    [Params(ActivitySamplingResult.None, ActivitySamplingResult.AllDataAndRecorded)]
    public ActivitySamplingResult Sampling { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var sampling = Sampling;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Wkeel",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => sampling,
        };
        ActivitySource.AddActivityListener(_listener);

        _provider = new ServiceCollection()
            .AddWkeel(w => w.HandlerLifetime(ServiceLifetime.Scoped).AddHandler<PingHandler>())
            .BuildServiceProvider();
        _scope = _provider.CreateScope();
        _wkeel = _scope.ServiceProvider.GetRequiredService<IWkeel>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
        _listener.Dispose();
    }

    [Benchmark]
    public Task Execute() => _wkeel.Execute(_ping, CancellationToken.None);
}
