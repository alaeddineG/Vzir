using Microsoft.Extensions.DependencyInjection;
using static Wkeel.Tests.Fixture;

namespace Wkeel.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public async Task Handler_is_routed_for_every_message_it_handles()
    {
        var journal = new Journal();
        using var provider = Provider(w => w.AddHandler<MultiHandler>(), s => s.AddSingleton(journal));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await wkeel.Execute(new Archive(1), Ct);
        var name = await wkeel.Execute(new GetName(1), Ct);

        Assert.Single(journal.Entries);
        Assert.Equal("multi", name);
    }

    [Fact]
    public async Task Registering_a_handler_twice_is_not_ambiguous()
    {
        using var provider = Provider(w => w.AddHandler<GetNameHandler>().AddHandler<GetNameHandler>());
        using var scope = provider.CreateScope();

        var name = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(1), Ct);

        Assert.Equal("patient-1", name);
    }

    [Fact]
    public void Type_without_a_handler_interface_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => Provider(w => w.AddHandler<NotAHandler>()));

        Assert.Contains(nameof(NotAHandler), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Abstract_handler_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => Provider(w => w.AddHandler<AbstractHandler>()));

        Assert.Contains(nameof(AbstractHandler), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Handler_of_an_abstract_message_is_rejected_because_it_could_never_run()
    {
        var notice = Assert.Throws<ArgumentException>(() => Provider(w => w.AddHandler<HandlesEveryNotice>()));
        var operation = Assert.Throws<ArgumentException>(() => Provider(w => w.AddHandler<HandlesAbstractOperation>()));
        var request = Assert.Throws<ArgumentException>(() => Provider(w => w.AddHandler<HandlesAbstractRequest>()));

        Assert.Contains(nameof(HandlesEveryNotice), notice.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(AbstractOperation), operation.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(AbstractRequest), request.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Handlers_are_transient_by_default()
    {
        var descriptors = new ServiceCollection().AddWkeel(w => w.AddHandler<StatefulHandler>());

        Assert.Equal(ServiceLifetime.Transient, HandlerDescriptor<Instance, StatefulHandler>(descriptors).Lifetime);
    }

    [Fact]
    public void Default_lifetime_applies_regardless_of_call_order()
    {
        var descriptors = new ServiceCollection().AddWkeel(w => w
            .AddHandler<StatefulHandler>()
            .HandlerLifetime(ServiceLifetime.Scoped));

        Assert.Equal(ServiceLifetime.Scoped, HandlerDescriptor<Instance, StatefulHandler>(descriptors).Lifetime);
    }

    [Fact]
    public void Per_registration_lifetime_overrides_the_default()
    {
        var descriptors = new ServiceCollection().AddWkeel(w => w
            .HandlerLifetime(ServiceLifetime.Scoped)
            .AddHandler<StatefulHandler>(ServiceLifetime.Transient)
            .AddHandler<OtherStatefulHandler>());

        Assert.Equal(ServiceLifetime.Transient, HandlerDescriptor<Instance, StatefulHandler>(descriptors).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, HandlerDescriptor<OtherInstance, OtherStatefulHandler>(descriptors).Lifetime);
    }

    [Fact]
    public async Task Transient_handlers_are_new_per_execute_and_scoped_ones_are_shared()
    {
        using var provider = Provider(w => w
            .AddHandler<StatefulHandler>()
            .AddHandler<OtherStatefulHandler>(ServiceLifetime.Scoped));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        Assert.NotSame(await wkeel.Execute(new Instance(), Ct), await wkeel.Execute(new Instance(), Ct));
        Assert.Same(await wkeel.Execute(new OtherInstance(), Ct), await wkeel.Execute(new OtherInstance(), Ct));
    }

    [Fact]
    public void Singleton_handlers_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Provider(w => w.HandlerLifetime(ServiceLifetime.Singleton)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Provider(w => w.AddHandler<GetNameHandler>(ServiceLifetime.Singleton)));
    }

    [Fact]
    public void AddWkeel_can_only_be_called_once()
    {
        var services = new ServiceCollection().AddWkeel(w => w.AddHandler<GetNameHandler>());

        Assert.Throws<InvalidOperationException>(() => services.AddWkeel(w => w.AddHandler<SecondGetNameHandler>()));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddWkeel(_ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddWkeel(null!));
    }

    private static ServiceDescriptor HandlerDescriptor<TRequest, TResult>(IServiceCollection services)
        where TRequest : IRequest<TResult> =>
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRequestHandler<TRequest, TResult>));
}
