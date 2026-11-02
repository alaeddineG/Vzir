using Microsoft.Extensions.DependencyInjection;
using Wkeel.Tests.ScanTarget;
using static Wkeel.Tests.Fixture;

namespace Wkeel.Tests;

public sealed class ScanningTests
{
    private readonly Journal _journal = new();

    private ServiceProvider Configured(Action<WkeelBuilder> configure) =>
        Provider(configure, collection => collection.AddSingleton(_journal));

    private static ServiceDescriptor Registration<TService>(IServiceCollection services) =>
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TService));

    [Fact]
    public async Task Scan_finds_public_internal_and_nested_handlers_in_a_real_assembly()
    {
        using var provider = Provider(w => w.ScanAssembly(typeof(ScannedRequest).Assembly));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();
        var seen = new List<string>();

        var result = await wkeel.Execute(new ScannedRequest(), Ct);
        await wkeel.Execute(new ScannedOperation(seen), Ct);
        await wkeel.Notify(new ScannedNotice(seen), Ct);
        await wkeel.Notify(new DerivedScannedNotice(seen), Ct);

        Assert.Equal("PublicRequestHandler", result);
        Assert.Equal(["InternalOperationHandler", "NestedPrivateListener", "ClosedListener"], seen);
    }

    [Fact]
    public async Task ScanAssemblyContaining_scans_the_assembly_that_defines_the_type()
    {
        using var provider = Provider(w => w.ScanAssemblyContaining<Bystander>());
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new ScannedRequest(), Ct);

        Assert.Equal("PublicRequestHandler", result);
    }

    [Fact]
    public async Task Scan_registers_handlers_only_so_a_scanned_interceptor_stays_out_of_the_chain()
    {
        using var provider = Configured(w => w.ScanAssembly(new TypesAssembly(
            typeof(Archive),
            typeof(ArchiveHandler),
            typeof(NotAHandler),
            typeof(Outer<,>))));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Archive(1), Ct);

        Assert.Equal([new Archive(1)], _journal.Entries);
    }

    [Fact]
    public async Task Scan_registers_every_contract_of_a_handler()
    {
        using var provider = Configured(w => w.ScanAssembly(new TypesAssembly(typeof(HandlesEveryKind))));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await wkeel.Execute(new Archive(1), Ct);
        await wkeel.Notify(new OtherNotice(), Ct);

        Assert.Equal("every-kind", await wkeel.Execute(new GetName(1), Ct));
        Assert.Equal([new Archive(1), new OtherNotice()], _journal.Entries);
    }

    [Fact]
    public void Scan_rejects_what_AddHandler_rejects_instead_of_leaving_it_unable_to_run()
    {
        var open = Assert.Throws<ArgumentException>(() => Provider(w => w.ScanAssembly(new TypesAssembly(typeof(OpenListener<>)))));
        var value = Assert.Throws<ArgumentException>(() => Provider(w => w.ScanAssembly(new TypesAssembly(typeof(StructHandler)))));
        var unreachable = Assert.Throws<ArgumentException>(() => Provider(w => w.ScanAssembly(new TypesAssembly(typeof(HandlesEveryNotice)))));

        Assert.Contains(nameof(OpenListener<>), open.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(StructHandler), value.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(INotice), unreachable.Message, StringComparison.Ordinal);
        Assert.All([open, value, unreachable], exception => Assert.Equal("assembly", exception.ParamName));
    }

    [Fact]
    public async Task Scanned_listeners_run_in_ordinal_name_order_whatever_the_metadata_order()
    {
        using var provider = Configured(w => w.ScanAssembly(new TypesAssembly(
            typeof(ThirdListener),
            typeof(CasedListenerb),
            typeof(FirstListener),
            typeof(CasedListenerC),
            typeof(SecondListener))));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new PatientMoved(1), Ct);

        Assert.Equal(Sequence("C", "b", "first", "second", "third"), _journal.Entries);
    }

    [Fact]
    public async Task Listener_added_before_a_scan_keeps_its_place()
    {
        using var provider = Configured(w => w
            .AddHandler<ThirdListener>()
            .ScanAssembly(new TypesAssembly(typeof(FirstListener), typeof(SecondListener), typeof(ThirdListener))));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new PatientMoved(1), Ct);

        Assert.Equal(Sequence("third", "first", "second"), _journal.Entries);
    }

    [Fact]
    public async Task Listener_added_after_a_scan_keeps_the_place_the_scan_gave_it()
    {
        using var provider = Configured(w => w
            .ScanAssembly(new TypesAssembly(typeof(FirstListener), typeof(SecondListener)))
            .AddHandler<FirstListener>());
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new PatientMoved(1), Ct);

        Assert.Equal(Sequence("first", "second"), _journal.Entries);
    }

    [Fact]
    public async Task Scan_and_AddHandler_of_one_handler_are_one_registration_in_either_order()
    {
        var assembly = new TypesAssembly(typeof(GetNameHandler));
        using var scanLast = Provider(w => w.AddHandler<GetNameHandler>().ScanAssembly(assembly));
        using var scanFirst = Provider(w => w.ScanAssembly(assembly).AddHandler<GetNameHandler>());
        using var lastScope = scanLast.CreateScope();
        using var firstScope = scanFirst.CreateScope();

        await lastScope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(1), Ct);
        await firstScope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(1), Ct);
    }

    [Fact]
    public void Scan_keeps_an_explicit_lifetime_in_either_order()
    {
        var assembly = new TypesAssembly(typeof(GetNameHandler));

        var scanLast = new ServiceCollection().AddWkeel(w => w
            .AddHandler<GetNameHandler>(ServiceLifetime.Scoped)
            .ScanAssembly(assembly));
        var scanFirst = new ServiceCollection().AddWkeel(w => w
            .ScanAssembly(assembly)
            .AddHandler<GetNameHandler>(ServiceLifetime.Scoped));

        Assert.Equal(ServiceLifetime.Scoped, Registration<IRequestHandler<GetName, string>>(scanLast).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Registration<IRequestHandler<GetName, string>>(scanFirst).Lifetime);
    }

    [Fact]
    public void Scanned_handlers_take_the_default_lifetime_in_either_order()
    {
        var assembly = new TypesAssembly(typeof(GetNameHandler));

        var defaultFirst = new ServiceCollection().AddWkeel(w => w.HandlerLifetime(ServiceLifetime.Scoped).ScanAssembly(assembly));
        var defaultLast = new ServiceCollection().AddWkeel(w => w.ScanAssembly(assembly).HandlerLifetime(ServiceLifetime.Scoped));

        Assert.Equal(ServiceLifetime.Scoped, Registration<IRequestHandler<GetName, string>>(defaultFirst).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Registration<IRequestHandler<GetName, string>>(defaultLast).Lifetime);
    }

    [Fact]
    public async Task Scanning_an_assembly_twice_registers_its_handlers_once()
    {
        var assembly = new TypesAssembly(typeof(InstanceListener));
        using var provider = Configured(w => w.ScanAssembly(assembly).ScanAssembly(assembly));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new InstanceNotice(), Ct);

        Assert.Single(_journal.Entries);
    }

    [Fact]
    public void Null_assembly_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Provider(w => w.ScanAssembly(null!)));
    }
}
