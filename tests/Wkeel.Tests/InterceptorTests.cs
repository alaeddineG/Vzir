using Microsoft.Extensions.DependencyInjection;
using static Wkeel.Tests.Fixture;

namespace Wkeel.Tests;

public sealed class InterceptorTests
{
    private readonly Journal _journal = new();

    private ServiceProvider Configured(Action<WkeelBuilder> configure, Action<IServiceCollection>? services = null) =>
        Provider(configure, collection =>
        {
            collection.AddSingleton(_journal);
            services?.Invoke(collection);
        });

    [Fact]
    public async Task Open_interceptor_wraps_a_request()
    {
        using var provider = Configured(w => w.AddHandler<GetNameHandler>().Intercept(typeof(CapturesResult<,>)));
        using var scope = provider.CreateScope();

        var name = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(7), Ct);

        Assert.Equal("patient-7", name);
        Assert.Equal("patient-7", Assert.Single(_journal.Entries));
    }

    [Fact]
    public async Task Open_interceptor_wraps_an_operation_and_the_handler_still_runs()
    {
        using var provider = Configured(w => w.AddHandler<ArchiveHandler>().Intercept(typeof(Outer<,>)));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Archive(7), Ct);

        Assert.Equal(["outer:in", new Archive(7), "outer:out"], _journal.Entries);
    }

    [Fact]
    public async Task Interceptor_sees_the_message_with_its_exact_type()
    {
        using var provider = Configured(w => w.AddHandler<GetNameHandler>().Intercept(typeof(CapturesTypes<,>)));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(7), Ct);

        Assert.Equal((typeof(GetName), new GetName(7)), Assert.Single(_journal.Entries));
    }

    [Fact]
    public async Task Open_interceptor_skips_messages_outside_its_message_constraint()
    {
        using var provider = Configured(w => w
            .AddHandler<ArchiveHandler>()
            .AddHandler<AuditedArchiveHandler>()
            .Intercept(typeof(OnlyAudited<,>)));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await wkeel.Execute(new Archive(1), Ct);
        Assert.Equal([new Archive(1)], _journal.Entries);

        await wkeel.Execute(new AuditedArchive(), Ct);
        Assert.Equal([new Archive(1), "audited:in", "audited:out"], _journal.Entries);
    }

    [Fact]
    public async Task Open_interceptor_skips_messages_outside_its_result_constraint()
    {
        using var provider = Configured(w => w
            .AddHandler<GetNameHandler>()
            .AddHandler<EchoHandler>()
            .AddHandler<ArchiveHandler>()
            .Intercept(typeof(OnlyClassResults<,>)));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await wkeel.Execute(new Echo(1), Ct);
        await wkeel.Execute(new Archive(1), Ct);
        Assert.Equal([new Archive(1)], _journal.Entries);

        await wkeel.Execute(new GetName(1), Ct);
        Assert.Equal([new Archive(1), "class-result:in", "class-result:out"], _journal.Entries);
    }

    [Fact]
    public async Task Open_interceptor_may_declare_its_parameters_in_either_order()
    {
        using var provider = Configured(w => w.AddHandler<GetNameHandler>().Intercept(typeof(SwappedParameters<,>)));
        using var scope = provider.CreateScope();

        var name = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(7), Ct);

        Assert.Equal("patient-7", name);
        Assert.Equal(["swapped:in", "swapped:out"], _journal.Entries);
    }

    [Fact]
    public async Task Closed_request_interceptor_applies_to_its_message_only()
    {
        using var provider = Configured(w => w
            .AddHandler<GetNameHandler>()
            .AddHandler<GetTitleHandler>()
            .Intercept<GetName, string, GetNameTrace>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        // Same result type as GetName, so only the message type can tell them apart.
        await wkeel.Execute(new GetTitle(1), Ct);
        Assert.Empty(_journal.Entries);

        await wkeel.Execute(new GetName(1), Ct);
        Assert.Equal(["closed:in", "closed:out"], _journal.Entries);
    }

    [Fact]
    public async Task Closed_request_interceptor_ignores_another_result_of_the_same_message()
    {
        using var provider = Configured(w => w
            .AddHandler<TwoResultsStringHandler>()
            .Intercept<TwoResults, int, TwoResultsIntTrace>());
        using var scope = provider.CreateScope();

        var text = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute<string>(new TwoResults(), Ct);

        Assert.Equal("text", text);
        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task Closed_operation_interceptor_applies_to_its_operation_only()
    {
        using var provider = Configured(w => w
            .AddHandler<ArchiveHandler>()
            .AddHandler<AuditedArchiveHandler>()
            .Intercept<Archive, ArchiveTrace>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await wkeel.Execute(new AuditedArchive(), Ct);
        Assert.Empty(_journal.Entries);

        await wkeel.Execute(new Archive(1), Ct);
        Assert.Equal(["closed:in", new Archive(1), "closed:out"], _journal.Entries);
    }

    [Fact]
    public async Task Closed_operation_interceptor_does_not_touch_the_request_route_of_the_same_type()
    {
        using var provider = Configured(w => w
            .AddHandler<BothKindsRequestHandler>()
            .Intercept<BothKinds, BothKindsOperationTrace>());
        using var scope = provider.CreateScope();

        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute<int>(new BothKinds(), Ct));

        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task Request_chain_runs_in_registration_order_across_open_and_closed()
    {
        using var provider = Configured(w => w
            .AddHandler<GetNameHandler>()
            .Intercept(typeof(Outer<,>))
            .Intercept<GetName, string, GetNameTrace>()
            .Intercept(typeof(Inner<,>)));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(1), Ct);

        Assert.Equal(["outer:in", "closed:in", "inner:in", "inner:out", "closed:out", "outer:out"], _journal.Entries);
    }

    [Fact]
    public async Task Operation_chain_runs_in_registration_order_across_open_and_closed()
    {
        using var provider = Configured(w => w
            .AddHandler<ArchiveHandler>()
            .Intercept(typeof(Outer<,>))
            .Intercept<Archive, ArchiveTrace>()
            .Intercept(typeof(Inner<,>)));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Archive(1), Ct);

        Assert.Equal(
            ["outer:in", "closed:in", "inner:in", new Archive(1), "inner:out", "closed:out", "outer:out"],
            _journal.Entries);
    }

    [Fact]
    public async Task Handlers_registered_after_interceptors_are_still_wrapped()
    {
        using var provider = Configured(w => w.Intercept(typeof(Outer<,>)).AddHandler<GetNameHandler>());
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(1), Ct);

        Assert.Equal(["outer:in", "outer:out"], _journal.Entries);
    }

    [Fact]
    public async Task Short_circuit_skips_the_handler_and_returns_the_interceptors_result()
    {
        using var provider = Configured(w => w.AddHandler<GetNameHandler>().Intercept<GetName, string, GetNameCache>());
        using var scope = provider.CreateScope();

        var name = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(1), Ct);

        Assert.Equal("cached", name);
    }

    [Fact]
    public async Task Short_circuit_on_an_operation_skips_the_handler_from_an_open_interceptor()
    {
        using var provider = Configured(w => w.AddHandler<ArchiveHandler>().Intercept(typeof(ShortCircuit<,>)));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Archive(1), Ct);

        Assert.Equal(["short-circuit"], _journal.Entries);
    }

    [Fact]
    public async Task Short_circuit_on_an_operation_skips_the_handler_from_a_closed_interceptor()
    {
        using var provider = Configured(w => w.AddHandler<ArchiveHandler>().Intercept<Archive, ArchiveVeto>());
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Archive(1), Ct);

        Assert.Equal(["veto"], _journal.Entries);
    }

    [Fact]
    public async Task Short_circuit_never_constructs_the_interceptors_inside_it()
    {
        using var provider = Configured(w => w
            .AddHandler<GetNameHandler>()
            .Intercept(typeof(ShortCircuit<,>))
            .Intercept(typeof(RecordsConstruction<,>)));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(1), Ct);

        Assert.Equal(["short-circuit"], _journal.Entries);
    }

    [Fact]
    public async Task Calling_next_twice_runs_the_rest_of_the_chain_twice()
    {
        using var provider = Configured(w => w
            .AddHandler<ArchiveHandler>()
            .Intercept(typeof(RunsNextTwice<,>))
            .Intercept(typeof(Inner<,>)));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Archive(1), Ct);

        Assert.Equal(["inner:in", new Archive(1), "inner:out", "inner:in", new Archive(1), "inner:out"], _journal.Entries);
    }

    [Fact]
    public async Task Interceptor_can_catch_and_translate_handler_failures()
    {
        using var provider = Configured(w => w
            .AddHandler<FailHandler>()
            .AddHandler<FailRequestHandler>()
            .Intercept(typeof(TranslatesFailures<,>)));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        var operation = await Assert.ThrowsAsync<InvalidOperationException>(() => wkeel.Execute(new Fail(), Ct));
        var request = await Assert.ThrowsAsync<InvalidOperationException>(() => wkeel.Execute(new FailRequest(), Ct));

        Assert.IsType<TimeoutException>(operation.InnerException);
        Assert.IsType<TimeoutException>(request.InnerException);
    }

    [Fact]
    public async Task Handler_failures_propagate_unchanged_through_interceptors()
    {
        using var provider = Configured(w => w.AddHandler<FailHandler>().Intercept(typeof(Outer<,>)));
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<TimeoutException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Fail(), Ct));

        Assert.Equal(["outer:in"], _journal.Entries);
    }

    [Fact]
    public async Task Missing_handler_throws_before_any_interceptor_runs()
    {
        using var provider = Configured(w => w.Intercept(typeof(Outer<,>)));
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<HandlerNotFoundException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(1), Ct));

        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task Ambiguous_handlers_throw_before_any_interceptor_runs()
    {
        using var provider = Configured(w => w
            .AddHandler<GetNameHandler>()
            .AddHandler<SecondGetNameHandler>()
            .Intercept(typeof(Outer<,>)));
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<AmbiguousHandlerException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(1), Ct));

        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task Interceptor_and_handler_receive_the_callers_token()
    {
        using var source = new CancellationTokenSource();
        using var provider = Configured(w => w.AddHandler<CaptureTokenHandler>().Intercept(typeof(CapturesToken<,>)));
        using var scope = provider.CreateScope();

        var handlers = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new CaptureToken(), source.Token);

        Assert.Equal(source.Token, handlers);
        Assert.Equal(source.Token, Assert.Single(_journal.Entries));
    }

    [Fact]
    public async Task Interceptor_gets_the_callers_scoped_instance()
    {
        using var provider = Configured(
            w => w.AddHandler<WhoIsScopedHandler>().Intercept(typeof(CapturesScope<,>)),
            s => s.AddScoped<ScopedProbe>());
        using var scope = provider.CreateScope();
        var callers = scope.ServiceProvider.GetRequiredService<ScopedProbe>();

        var handlers = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new WhoIsScoped(), Ct);

        Assert.Same(callers, handlers);
        Assert.Same(callers, Assert.Single(_journal.Entries));
    }

    [Fact]
    public async Task Interceptors_are_resolved_per_execute()
    {
        using var provider = Configured(w => w.AddHandler<GetNameHandler>().Intercept(typeof(CapturesInstance<,>)));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await wkeel.Execute(new GetName(1), Ct);
        await wkeel.Execute(new GetName(1), Ct);

        Assert.Equal(2, _journal.Entries.Distinct().Count());
    }

    [Fact]
    public async Task Application_registration_of_an_interceptor_wins_over_the_transient_default()
    {
        using var provider = Configured(
            w => w.AddHandler<GetNameHandler>().Intercept(typeof(CapturesInstance<,>)),
            s => s.AddScoped<CapturesInstance<GetName, string>>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await wkeel.Execute(new GetName(1), Ct);
        await wkeel.Execute(new GetName(1), Ct);

        Assert.Single(_journal.Entries.Distinct());
    }

    [Fact]
    public async Task Each_message_type_gets_its_own_chain()
    {
        using var provider = Configured(w => w
            .AddHandler<GetNameHandler>()
            .AddHandler<GetTitleHandler>()
            .Intercept<GetName, string, GetNameCache>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        Assert.Equal("cached", await wkeel.Execute(new GetName(1), Ct));
        Assert.Equal("title-5", await wkeel.Execute(new GetTitle(5), Ct));
    }

    [Fact]
    public async Task Concurrent_execute_keeps_each_calls_message_on_its_own_chain()
    {
        using var provider = Configured(w => w
            .AddHandler<EchoHandler>()
            .AddHandler<ArchiveHandler>()
            .Intercept(typeof(Outer<,>))
            .Intercept(typeof(Inner<,>)));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();
        var values = Enumerable.Range(0, 500).ToArray();

        // Enough calls for the container to compile its resolvers, which must cope with the internal Nothing.
        var requests = values.Select(value => Task.Run(() => wkeel.Execute(new Echo(value), Ct), Ct));
        var operations = values.Select(value => Task.Run(() => wkeel.Execute(new Archive(value), Ct), Ct));
        var results = Task.WhenAll(requests);
        await Task.WhenAll(results, Task.WhenAll(operations));

        Assert.Equal(values, await results);
        Assert.Equal(values, _journal.Entries.OfType<Archive>().Select(archive => archive.Id).Order());
    }

    [Theory]
    [InlineData(typeof(NotAnInterceptor<,>))]
    [InlineData(typeof(AbstractInterceptor<,>))]
    [InlineData(typeof(OneParameter<>))]
    [InlineData(typeof(ThreeParameters<,,>))]
    [InlineData(typeof(SameParameterTwice<,>))]
    [InlineData(typeof(OpenOperationInterceptor<>))]
    [InlineData(typeof(GetNameCache))]
    [InlineData(typeof(Outer<GetName, string>))]
    public void Unusable_open_generic_is_rejected_at_registration(Type interceptor)
    {
        var exception = Assert.Throws<ArgumentException>(() => Configured(w => w.Intercept(interceptor)));

        Assert.Contains(interceptor.Name, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_interceptor_type_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => Configured(w => w.Intercept(null!)));

    [Fact]
    public void Container_validation_covers_interceptor_dependencies()
    {
        var exception = Assert.Throws<AggregateException>(() => new ServiceCollection()
            .AddSingleton(_journal)
            .AddWkeel(w => w.AddHandler<WhoIsScopedHandler>().Intercept(typeof(Outer<,>)))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }));

        Assert.Contains(nameof(ScopedProbe), exception.Message, StringComparison.Ordinal);
    }
}
