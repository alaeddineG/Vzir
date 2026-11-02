using Microsoft.Extensions.DependencyInjection;
using Wkeel.Tests.ScanTarget;
using static Wkeel.Tests.Fixture;

namespace Wkeel.Tests;

public sealed class ValidationTests
{
    private static IServiceCollection Validated(Action<WkeelBuilder> configure) =>
        new ServiceCollection().AddWkeel(w =>
        {
            configure(w);
            w.ValidateOnBuild();
        });

    private static AggregateException Failure(Action<WkeelBuilder> configure) =>
        Assert.Throws<AggregateException>(() => Validated(configure));

    private static void Missing<TMessage>(Exception exception) =>
        Assert.Equal(typeof(TMessage), Assert.IsType<HandlerNotFoundException>(exception).MessageType);

    [Fact]
    public void Every_missing_and_duplicate_handler_is_reported_at_once_by_name()
    {
        var exception = Failure(w => w
            .AddHandler<GetNameHandler>()
            .AddHandler<SecondGetNameHandler>()
            .ScanAssembly(new TypesAssembly(typeof(Touch), typeof(GetName), typeof(Archive))));

        Assert.Collection(
            exception.InnerExceptions,
            Missing<Archive>,
            Missing<Touch>,
            third =>
            {
                var ambiguous = Assert.IsType<AmbiguousHandlerException>(third);
                Assert.Equal(typeof(GetName), ambiguous.MessageType);
                Assert.Equal([typeof(GetNameHandler), typeof(SecondGetNameHandler)], ambiguous.HandlerTypes);
            });

        Assert.All(
            [nameof(Archive), nameof(Touch), nameof(GetNameHandler), nameof(SecondGetNameHandler)],
            name => Assert.Contains(name, exception.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicates_are_found_for_both_kinds_without_a_scan()
    {
        var exception = Failure(w => w
            .AddHandler<GetNameHandler>()
            .AddHandler<SecondGetNameHandler>()
            .AddHandler<ArchiveHandler>()
            .AddHandler<SecondArchiveHandler>());

        Assert.Equal(
            [typeof(Archive), typeof(GetName)],
            exception.InnerExceptions.Cast<AmbiguousHandlerException>().Select(ambiguous => ambiguous.MessageType));
    }

    [Fact]
    public void One_problem_is_still_aggregated()
    {
        var exception = Failure(w => w.ScanAssembly(new TypesAssembly(typeof(Archive))));

        Missing<Archive>(Assert.Single(exception.InnerExceptions));
    }

    [Fact]
    public void Validation_covers_scans_made_after_it_was_requested()
    {
        Assert.Throws<AggregateException>(() => new ServiceCollection().AddWkeel(w => w
            .ValidateOnBuild()
            .ScanAssembly(new TypesAssembly(typeof(Archive)))));
    }

    [Fact]
    public void Scanning_an_assembly_twice_reports_its_problems_once()
    {
        var assembly = new TypesAssembly(typeof(Archive));

        var exception = Failure(w => w.ScanAssembly(assembly).ScanAssembly(assembly));

        Assert.Single(exception.InnerExceptions);
    }

    [Fact]
    public void A_real_assembly_with_every_message_handled_passes()
    {
        Validated(w => w.ScanAssembly(typeof(ScannedRequest).Assembly));
    }

    [Fact]
    public void A_handler_outside_the_scanned_assembly_counts()
    {
        Validated(w => w.AddHandler<ArchiveHandler>().ScanAssembly(new TypesAssembly(typeof(Archive))));
    }

    [Fact]
    public void Types_that_cannot_be_checked_or_need_no_handler_pass()
    {
        Validated(w => w.ScanAssembly(new TypesAssembly(
            typeof(UnhandledNotice),
            typeof(AbstractOperation),
            typeof(AbstractRequest),
            typeof(OpenMessage<>),
            typeof(NotAHandler))));
    }

    [Fact]
    public void A_request_implemented_twice_needs_the_answer_Execute_would_give()
    {
        var assembly = new TypesAssembly(typeof(TwoResults));

        var oneHandler = Failure(w => w.AddHandler<TwoResultsIntHandler>().ScanAssembly(assembly));
        var twoHandlers = Failure(w => w
            .AddHandler<TwoResultsIntHandler>()
            .AddHandler<TwoResultsStringHandler>()
            .ScanAssembly(assembly));

        Missing<TwoResults>(Assert.Single(oneHandler.InnerExceptions));
        Assert.IsType<AmbiguousHandlerException>(Assert.Single(twoHandlers.InnerExceptions));
    }

    [Fact]
    public void A_message_of_both_kinds_needs_both_handlers()
    {
        var assembly = new TypesAssembly(typeof(BothKinds));

        var noOperation = Failure(w => w.AddHandler<BothKindsRequestHandler>().ScanAssembly(assembly));
        var noRequest = Failure(w => w.AddHandler<BothKindsOperationHandler>().ScanAssembly(assembly));

        Missing<BothKinds>(Assert.Single(noOperation.InnerExceptions));
        Missing<BothKinds>(Assert.Single(noRequest.InnerExceptions));
        Validated(w => w.AddHandler<BothKindsRequestHandler>().AddHandler<BothKindsOperationHandler>().ScanAssembly(assembly));
    }

    [Fact]
    public async Task Nothing_is_checked_at_startup_without_opting_in_and_Execute_still_throws_a_typed_exception()
    {
        using var provider = Provider(w => w
            .AddHandler<GetNameHandler>()
            .AddHandler<SecondGetNameHandler>()
            .ScanAssembly(new TypesAssembly(typeof(Archive))));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await Assert.ThrowsAsync<HandlerNotFoundException>(() => wkeel.Execute(new Archive(1), Ct));
        await Assert.ThrowsAsync<AmbiguousHandlerException>(() => wkeel.Execute(new GetName(1), Ct));
    }
}
