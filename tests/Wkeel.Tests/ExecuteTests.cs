using Microsoft.Extensions.DependencyInjection;
using static Wkeel.Tests.Fixture;

namespace Wkeel.Tests;

public sealed class ExecuteTests
{
    [Fact]
    public async Task Operation_reaches_its_handler()
    {
        var journal = new Journal();
        using var provider = Provider(w => w.AddHandler<ArchiveHandler>(), s => s.AddSingleton(journal));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Archive(7), Ct);

        Assert.Equal(new Archive(7), Assert.Single(journal.Entries));
    }

    [Fact]
    public async Task Request_returns_its_handlers_result()
    {
        using var provider = Provider(w => w.AddHandler<GetNameHandler>());
        using var scope = provider.CreateScope();

        var name = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(7), Ct);

        Assert.Equal("patient-7", name);
    }

    [Fact]
    public async Task Value_type_messages_are_supported()
    {
        var journal = new Journal();
        using var provider = Provider(w => w.AddHandler<TouchHandler>(), s => s.AddSingleton(journal));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Touch(3), Ct);

        Assert.Equal(new Touch(3), Assert.Single(journal.Entries));
    }

    [Fact]
    public async Task Token_reaches_the_handler()
    {
        using var source = new CancellationTokenSource();
        using var provider = Provider(w => w.AddHandler<CaptureTokenHandler>());
        using var scope = provider.CreateScope();

        var seen = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new CaptureToken(), source.Token);

        Assert.Equal(source.Token, seen);
    }

    [Fact]
    public async Task Handler_exceptions_propagate_unchanged()
    {
        using var provider = Provider(w => w.AddHandler<FailHandler>().AddHandler<FailRequestHandler>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await Assert.ThrowsAsync<TimeoutException>(() => wkeel.Execute(new Fail(), Ct));
        await Assert.ThrowsAsync<TimeoutException>(() => wkeel.Execute(new FailRequest(), Ct));
    }

    [Fact]
    public async Task Null_messages_are_rejected()
    {
        using var provider = Provider(_ => { });
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => wkeel.Execute((IOperation)null!, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => wkeel.Execute((IRequest<int>)null!, Ct));
    }

    [Fact]
    public async Task Missing_handler_names_the_message()
    {
        using var provider = Provider(_ => { });
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        var operation = await Assert.ThrowsAsync<HandlerNotFoundException>(() => wkeel.Execute(new Archive(1), Ct));
        var request = await Assert.ThrowsAsync<HandlerNotFoundException>(() => wkeel.Execute(new GetName(1), Ct));

        Assert.Equal(typeof(Archive), operation.MessageType);
        Assert.Contains(nameof(Archive), operation.Message, StringComparison.Ordinal);
        Assert.Equal(typeof(GetName), request.MessageType);
    }

    [Fact]
    public async Task Multiple_handlers_name_every_competitor()
    {
        using var provider = Provider(w => w
            .AddHandler<ArchiveHandler>()
            .AddHandler<SecondArchiveHandler>()
            .AddHandler<GetNameHandler>()
            .AddHandler<SecondGetNameHandler>(), s => s.AddSingleton(new Journal()));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        var operation = await Assert.ThrowsAsync<AmbiguousHandlerException>(() => wkeel.Execute(new Archive(1), Ct));
        var request = await Assert.ThrowsAsync<AmbiguousHandlerException>(() => wkeel.Execute(new GetName(1), Ct));

        Assert.Equal(typeof(Archive), operation.MessageType);
        Assert.Equal([typeof(ArchiveHandler), typeof(SecondArchiveHandler)], operation.HandlerTypes);
        Assert.Contains(nameof(SecondArchiveHandler), operation.Message, StringComparison.Ordinal);
        Assert.Equal([typeof(GetNameHandler), typeof(SecondGetNameHandler)], request.HandlerTypes);
    }

    [Fact]
    public async Task Routing_uses_the_exact_runtime_type()
    {
        using var provider = Provider(w => w.AddHandler<BaseMessageHandler>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await wkeel.Execute(new BaseMessage(), Ct);
        await Assert.ThrowsAsync<HandlerNotFoundException>(() => wkeel.Execute(new DerivedMessage(), Ct));
    }

    [Fact]
    public async Task Message_with_two_result_types_only_routes_the_handled_one()
    {
        using var provider = Provider(w => w.AddHandler<TwoResultsIntHandler>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        Assert.Equal(1, await wkeel.Execute<int>(new TwoResults(), Ct));
        await Assert.ThrowsAsync<HandlerNotFoundException>(() => wkeel.Execute<string>(new TwoResults(), Ct));
    }

    [Fact]
    public async Task Operation_and_request_routes_do_not_cross()
    {
        using var provider = Provider(w => w.AddHandler<BothKindsRequestHandler>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        Assert.Equal(1, await wkeel.Execute<int>(new BothKinds(), Ct));
        await Assert.ThrowsAsync<HandlerNotFoundException>(() => wkeel.Execute((IOperation)new BothKinds(), Ct));
    }
}
