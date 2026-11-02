using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Wkeel.Benchmarks;

/// <summary>What each interceptor adds, which D15 accepts in exchange for a per-call closure.</summary>
[MemoryDiagnoser]
public class PipelineBenchmarks
{
    private readonly Echo _echo = new();

    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private IWkeel _wkeel = null!;

    [Params(0, 1, 2, 3)]
    public int Interceptors { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _provider = new ServiceCollection()
            .AddWkeel(w =>
            {
                w.HandlerLifetime(ServiceLifetime.Scoped).AddHandler<EchoHandler>();

                for (var i = 0; i < Interceptors; i++)
                {
                    w.Intercept(typeof(PassThrough<,>));
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
    public Task<int> Execute() => _wkeel.Execute(_echo, CancellationToken.None);
}
