using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Wkeel.Benchmarks;

/// <summary>The fan-out. Parallel pays for capturing each handler's failure, which sequential does not.</summary>
[MemoryDiagnoser]
public class NotifyBenchmarks
{
    private readonly Tick _tick = new();

    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private IWkeel _wkeel = null!;

    [Params(1, 3)]
    public int Handlers { get; set; }

    [Params(false, true)]
    public bool Parallel { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _provider = new ServiceCollection()
            .AddWkeel(w =>
            {
                w.HandlerLifetime(ServiceLifetime.Scoped).AddHandler<FirstTickHandler>();

                if (Handlers == 3)
                {
                    w.AddHandler<SecondTickHandler>().AddHandler<ThirdTickHandler>();
                }

                if (Parallel)
                {
                    w.Notices(options => options.Parallel());
                }
            })
            .BuildServiceProvider();
        _scope = _provider.CreateScope();
        _wkeel = _scope.ServiceProvider.GetRequiredService<IWkeel>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Benchmark]
    public Task Notify() => _wkeel.Notify(_tick, CancellationToken.None);
}
