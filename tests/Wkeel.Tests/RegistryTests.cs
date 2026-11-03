using Microsoft.Extensions.DependencyInjection;
using static Wkeel.Tests.Fixture;

namespace Wkeel.Tests;

public sealed class RegistryTests
{
    private static IWkeelRegistry Registry(ServiceProvider provider) => provider.GetRequiredService<IWkeelRegistry>();

    private static ServiceProvider Configured(Action<WkeelBuilder> configure) =>
        Provider(configure, collection => collection.AddSingleton<Journal>());

    [Fact]
    public void Lists_each_message_kind_under_its_own_property()
    {
        using var provider = Configured(w => w
            .AddHandler<ArchiveHandler>()
            .AddHandler<GetNameHandler>()
            .AddHandler<FirstListener>());

        var registry = Registry(provider);

        Assert.Equal([typeof(ArchiveHandler)], Assert.Single(registry.Operations, entry => entry.Key == typeof(Archive)).Value);
        Assert.Equal([typeof(GetNameHandler)], Assert.Single(registry.Requests, entry => entry.Key == typeof(GetName)).Value);
        Assert.Equal([typeof(FirstListener)], Assert.Single(registry.Notices, entry => entry.Key == typeof(PatientMoved)).Value);
    }

    [Fact]
    public void Lists_notice_handlers_in_registration_order()
    {
        using var provider = Configured(w => w.AddHandler<SecondListener>().AddHandler<FirstListener>());

        Assert.Equal([typeof(SecondListener), typeof(FirstListener)], Registry(provider).Notices[typeof(PatientMoved)]);
    }

    [Fact]
    public void Lists_every_handler_of_a_message_that_Execute_would_reject()
    {
        using var provider = Configured(w => w.AddHandler<ArchiveHandler>().AddHandler<SecondArchiveHandler>());

        Assert.Equal([typeof(ArchiveHandler), typeof(SecondArchiveHandler)], Registry(provider).Operations[typeof(Archive)]);
    }

    [Fact]
    public void Keeps_a_type_that_is_two_message_kinds_apart()
    {
        using var provider = Configured(w => w.AddHandler<BothKindsOperationHandler>().AddHandler<BothKindsRequestHandler>());

        var registry = Registry(provider);

        Assert.Equal([typeof(BothKindsOperationHandler)], registry.Operations[typeof(BothKinds)]);
        Assert.Equal([typeof(BothKindsRequestHandler)], registry.Requests[typeof(BothKinds)]);
    }

    [Fact]
    public void Is_empty_when_nothing_is_registered()
    {
        using var provider = Configured(_ => { });

        var registry = Registry(provider);

        Assert.Empty(registry.Operations);
        Assert.Empty(registry.Requests);
        Assert.Empty(registry.Notices);
    }

    [Fact]
    public void Is_a_singleton_that_resolves_from_the_root()
    {
        using var provider = Configured(w => w.AddHandler<ArchiveHandler>());
        using var scope = provider.CreateScope();

        Assert.Same(Registry(provider), scope.ServiceProvider.GetRequiredService<IWkeelRegistry>());
    }
}
