using Microsoft.Extensions.DependencyInjection;
using static Wkeel.Tests.Fixture;

namespace Wkeel.Tests;

public sealed class ScopingTests
{
    private static ServiceProvider ScopedProvider() =>
        Provider(w => w.AddHandler<WhoIsScopedHandler>().AddHandler<WhoIsScopedNestedHandler>(), s => s.AddScoped<ScopedProbe>());

    [Fact]
    public void IWkeel_is_registered_as_scoped()
    {
        var services = new ServiceCollection().AddWkeel(_ => { });

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(IWkeel)).Lifetime);
    }

    [Fact]
    public async Task Handler_gets_the_callers_scoped_instance()
    {
        using var provider = ScopedProvider();
        using var scope = provider.CreateScope();
        var callers = scope.ServiceProvider.GetRequiredService<ScopedProbe>();

        var handlers = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new WhoIsScoped(), Ct);

        Assert.Same(callers, handlers);
    }

    [Fact]
    public async Task Nested_execute_stays_in_the_callers_scope()
    {
        using var provider = ScopedProvider();
        using var scope = provider.CreateScope();
        var callers = scope.ServiceProvider.GetRequiredService<ScopedProbe>();

        var handlers = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new WhoIsScopedNested(), Ct);

        Assert.Same(callers, handlers);
    }

    [Fact]
    public async Task Separate_scopes_stay_isolated()
    {
        using var provider = ScopedProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var firstProbe = await first.ServiceProvider.GetRequiredService<IWkeel>().Execute(new WhoIsScoped(), Ct);
        var secondProbe = await second.ServiceProvider.GetRequiredService<IWkeel>().Execute(new WhoIsScoped(), Ct);

        Assert.NotSame(firstProbe, secondProbe);
    }

    [Fact]
    public async Task Concurrent_execute_within_a_scope_is_safe()
    {
        using var provider = Provider(w => w.AddHandler<EchoHandler>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();
        var values = Enumerable.Range(0, 500).ToArray();

        var results = await Task.WhenAll(values.Select(value => Task.Run(() => wkeel.Execute(new Echo(value), Ct), Ct)));

        Assert.Equal(values, results);
    }
}
