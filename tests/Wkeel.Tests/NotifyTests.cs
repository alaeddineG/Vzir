using Microsoft.Extensions.DependencyInjection;
using static Wkeel.Tests.Fixture;

namespace Wkeel.Tests;

public sealed class NotifyTests
{
    private readonly Journal _journal = new();

    private ServiceProvider Configured(Action<WkeelBuilder> configure, Action<IServiceCollection>? services = null) =>
        Provider(configure, collection =>
        {
            collection.AddSingleton(_journal);
            collection.AddSingleton(new Rendezvous());
            services?.Invoke(collection);
        });

    private static void Mode(WkeelBuilder builder, bool parallel) =>
        builder.Notices(options =>
        {
            if (parallel)
            {
                options.Parallel();
            }
            else
            {
                options.Sequential();
            }
        });

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public async Task Every_registered_handler_runs_exactly_once(int handlers, bool parallel)
    {
        using var provider = Configured(w =>
        {
            Mode(w, parallel);

            if (handlers >= 1)
            {
                w.AddHandler<FirstListener>();
            }

            if (handlers == 3)
            {
                w.AddHandler<SecondListener>().AddHandler<ThirdListener>();
            }
        });
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new PatientMoved(1), Ct);

        Assert.Equal(handlers * 2, _journal.Entries.Count);
        Assert.Equal(handlers * 2, _journal.Entries.Distinct().Count());
    }

    [Fact]
    public async Task Handlers_run_one_at_a_time_in_registration_order_by_default()
    {
        using var provider = Configured(w => w
            .AddHandler<ThirdListener>()
            .AddHandler<FirstListener>()
            .AddHandler<SecondListener>());
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new PatientMoved(1), Ct);

        Assert.Equal(Sequence("third", "first", "second"), _journal.Entries);
    }

    [Fact]
    public async Task Parallel_handlers_overlap()
    {
        using var provider = Configured(w => w
            .AddHandler<MeetLeft>()
            .AddHandler<MeetRight>()
            .Notices(options => options.Parallel()));
        using var scope = provider.CreateScope();

        // Each handler waits for the other, so run one at a time this would time out.
        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new Meet(), Ct);
    }

    [Fact]
    public async Task Parallel_handlers_start_on_the_calling_thread()
    {
        using var provider = Configured(w => w.AddHandler<ThreadListener>().Notices(options => options.Parallel()));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();
        var caller = Environment.CurrentManagedThreadId;

        var notify = wkeel.Notify(new ThreadNotice(), Ct);

        Assert.Equal(caller, Assert.Single(_journal.Entries));
        await notify;
    }

    [Fact]
    public async Task Per_type_parallel_leaves_other_notices_sequential()
    {
        using var provider = Configured(w => w
            .AddHandler<MeetLeft>()
            .AddHandler<MeetRight>()
            .AddHandler<FirstListener>()
            .AddHandler<SecondListener>()
            .Notices<Meet>(options => options.Parallel()));
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await wkeel.Notify(new Meet(), Ct);
        await wkeel.Notify(new PatientMoved(1), Ct);

        Assert.Equal(Sequence("first", "second"), _journal.Entries);
    }

    [Fact]
    public async Task Per_type_sequential_overrides_global_parallel_in_either_call_order()
    {
        using var overrideLast = Configured(w => w
            .AddHandler<FirstListener>()
            .AddHandler<SecondListener>()
            .Notices(options => options.Parallel())
            .Notices<PatientMoved>(options => options.Sequential()));
        using var scope = overrideLast.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new PatientMoved(1), Ct);
        Assert.Equal(Sequence("first", "second"), _journal.Entries);

        _journal.Entries.Clear();
        using var overrideFirst = Configured(w => w
            .AddHandler<FirstListener>()
            .AddHandler<SecondListener>()
            .Notices<PatientMoved>(options => options.Sequential())
            .Notices(options => options.Parallel()));
        using var otherScope = overrideFirst.CreateScope();
        await otherScope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new PatientMoved(1), Ct);
        Assert.Equal(Sequence("first", "second"), _journal.Entries);
    }

    [Fact]
    public async Task Per_type_override_that_chooses_nothing_keeps_the_global_mode()
    {
        using var provider = Configured(w => w
            .AddHandler<MeetLeft>()
            .AddHandler<MeetRight>()
            .Notices(options => options.Parallel())
            .Notices<Meet>(_ => { }));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new Meet(), Ct);
    }

    [Fact]
    public async Task Repeating_a_per_type_call_that_chooses_nothing_keeps_the_earlier_choice()
    {
        using var provider = Configured(w => w
            .AddHandler<MeetLeft>()
            .AddHandler<MeetRight>()
            .Notices<Meet>(options => options.Parallel())
            .Notices<Meet>(_ => { }));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new Meet(), Ct);
    }

    [Fact]
    public async Task Last_global_choice_wins()
    {
        using var provider = Configured(w => w
            .AddHandler<FirstListener>()
            .AddHandler<SecondListener>()
            .Notices(options => options.Parallel())
            .Notices(options => options.Sequential()));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new PatientMoved(1), Ct);

        Assert.Equal(Sequence("first", "second"), _journal.Entries);
    }

    [Fact]
    public async Task Sequential_failure_stops_the_rest_and_propagates_unchanged()
    {
        using var provider = Configured(w => w
            .AddHandler<BeforeTrouble>()
            .AddHandler<ThrowsAfterAwaiting>()
            .AddHandler<AfterTrouble>());
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<TimeoutException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new Trouble(), Ct));

        Assert.Equal(Sequence("before"), _journal.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handler_that_cannot_be_constructed_fails_notify_before_any_handler_runs(bool parallel)
    {
        using var provider = Configured(w =>
        {
            Mode(w, parallel);
            w.AddHandler<FirstListener>().AddHandler<CannotBeConstructed>();
        });
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new PatientMoved(1), Ct));

        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task Parallel_failures_all_surface_in_registration_order_after_every_handler_finished()
    {
        using var provider = Configured(w => w
            .AddHandler<ThrowsAfterAwaiting>()
            .AddHandler<BeforeTrouble>()
            .AddHandler<ThrowsBeforeReturningTask>()
            .AddHandler<AfterTrouble>()
            .Notices(options => options.Parallel()));
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new Trouble(), Ct));

        Assert.Collection(
            exception.InnerExceptions,
            first => Assert.IsType<TimeoutException>(first),
            second => Assert.IsType<InvalidOperationException>(second));
        Assert.Equal(4, _journal.Entries.Count);
    }

    [Fact]
    public async Task Single_parallel_failure_is_still_aggregated()
    {
        using var provider = Configured(w => w
            .AddHandler<ThrowsBeforeReturningTask>()
            .AddHandler<AfterTrouble>()
            .Notices(options => options.Parallel()));
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new Trouble(), Ct));

        Assert.IsType<InvalidOperationException>(Assert.Single(exception.InnerExceptions));
    }

    [Fact]
    public async Task Parallel_caller_cancellation_propagates_as_a_cancellation()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        using var provider = Configured(w => w
            .AddHandler<ObservesToken>()
            .AddHandler<ObservesTokenToo>()
            .Notices(options => options.Parallel()));
        using var scope = provider.CreateScope();

        var notify = scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new Trouble(), source.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => notify);
        Assert.True(notify.IsCanceled);
    }

    [Fact]
    public async Task Parallel_real_failure_next_to_a_cancellation_is_aggregated_with_it()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        using var provider = Configured(w => w
            .AddHandler<ObservesTokenToo>()
            .AddHandler<ThrowsAfterAwaiting>()
            .Notices(options => options.Parallel()));
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new Trouble(), source.Token));

        Assert.Collection(
            exception.InnerExceptions,
            first => Assert.IsType<OperationCanceledException>(first),
            second => Assert.IsType<TimeoutException>(second));
    }

    [Fact]
    public async Task Handler_cancelling_itself_is_a_failure_when_the_caller_did_not_cancel()
    {
        using var provider = Configured(w => w.AddHandler<CancelsItself>().Notices(options => options.Parallel()));
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new Trouble(), Ct));

        Assert.IsType<OperationCanceledException>(Assert.Single(exception.InnerExceptions));
    }

    [Fact]
    public async Task Interceptors_do_not_run_on_notify()
    {
        using var provider = Configured(w => w.AddHandler<FirstListener>().Intercept(typeof(Outer<,>)));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new PatientMoved(1), Ct);

        Assert.Equal(Sequence("first"), _journal.Entries);
    }

    [Fact]
    public async Task Notify_inside_an_operation_handler_runs_within_its_interceptors()
    {
        using var provider = Configured(w => w
            .AddHandler<AnnounceHandler>()
            .AddHandler<FirstListener>()
            .Intercept(typeof(Outer<,>)));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Announce(1), Ct);

        Assert.Equal(["outer:in", "first:in", "first:out", "outer:out"], _journal.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handlers_get_the_callers_scoped_instance(bool parallel)
    {
        using var provider = Configured(
            w =>
            {
                Mode(w, parallel);
                w.AddHandler<ScopeListener>().AddHandler<SecondScopeListener>();
            },
            s => s.AddScoped<ScopedProbe>());
        using var scope = provider.CreateScope();
        var callers = scope.ServiceProvider.GetRequiredService<ScopedProbe>();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new ScopeNotice(), Ct);

        Assert.Equal(2, _journal.Entries.Count);
        Assert.All(_journal.Entries, entry => Assert.Same(callers, entry));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handlers_receive_the_callers_token(bool parallel)
    {
        using var source = new CancellationTokenSource();
        using var provider = Configured(w =>
        {
            Mode(w, parallel);
            w.AddHandler<TokenListener>();
        });
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new TokenNotice(), source.Token);

        Assert.Equal(source.Token, Assert.Single(_journal.Entries));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Value_type_notices_are_supported(bool parallel)
    {
        using var provider = Configured(w =>
        {
            Mode(w, parallel);
            w.AddHandler<ValueListener>();
        });
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new ValueNotice(3), Ct);

        Assert.Equal(new ValueNotice(3), Assert.Single(_journal.Entries));
    }

    [Fact]
    public async Task Routing_uses_the_exact_runtime_type()
    {
        using var provider = Configured(w => w.AddHandler<BaseNoticeListener>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await wkeel.Notify(new DerivedNotice(), Ct);
        Assert.Empty(_journal.Entries);

        await wkeel.Notify(new BaseNotice(), Ct);
        Assert.Equal(new BaseNotice(), Assert.Single(_journal.Entries));
    }

    [Fact]
    public async Task Registering_a_listener_twice_runs_it_once()
    {
        using var provider = Configured(w => w.AddHandler<InstanceListener>().AddHandler<InstanceListener>());
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new InstanceNotice(), Ct);

        Assert.Single(_journal.Entries);
    }

    [Fact]
    public async Task Listener_lifetime_follows_handler_registration()
    {
        using var transient = Configured(w => w.AddHandler<InstanceListener>());
        using var transientScope = transient.CreateScope();
        var wkeel = transientScope.ServiceProvider.GetRequiredService<IWkeel>();
        await wkeel.Notify(new InstanceNotice(), Ct);
        await wkeel.Notify(new InstanceNotice(), Ct);
        Assert.Equal(2, _journal.Entries.Distinct().Count());

        _journal.Entries.Clear();
        using var scoped = Configured(w => w.AddHandler<InstanceListener>(ServiceLifetime.Scoped));
        using var scopedScope = scoped.CreateScope();
        var scopedWkeel = scopedScope.ServiceProvider.GetRequiredService<IWkeel>();
        await scopedWkeel.Notify(new InstanceNotice(), Ct);
        await scopedWkeel.Notify(new InstanceNotice(), Ct);
        Assert.Single(_journal.Entries.Distinct());
    }

    [Fact]
    public async Task One_class_can_handle_every_message_kind()
    {
        using var provider = Configured(w => w.AddHandler<HandlesEveryKind>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await wkeel.Execute(new Archive(1), Ct);
        await wkeel.Notify(new PatientMoved(2), Ct);
        await wkeel.Notify(new OtherNotice(), Ct);

        Assert.Equal("every-kind", await wkeel.Execute(new GetName(1), Ct));
        Assert.Equal([new Archive(1), new PatientMoved(2), new OtherNotice()], _journal.Entries);
    }

    [Fact]
    public async Task Each_notice_type_reaches_its_own_handlers_only()
    {
        using var provider = Configured(w => w.AddHandler<FirstListener>().AddHandler<OtherListener>());
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new OtherNotice(), Ct);

        Assert.Equal(Sequence("other"), _journal.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_notify_within_a_scope_is_safe(bool parallel)
    {
        using var provider = Configured(w =>
        {
            Mode(w, parallel);
            w.AddHandler<ValueListener>();
        });
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();
        var ids = Enumerable.Range(0, 500).ToArray();

        await Task.WhenAll(ids.Select(id => Task.Run(() => wkeel.Notify(new ValueNotice(id), Ct), Ct)));

        Assert.Equal(ids, _journal.Entries.Cast<ValueNotice>().Select(notice => notice.Id).Order());
    }

    [Fact]
    public async Task Null_notice_is_rejected()
    {
        using var provider = Configured(_ => { });
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(null!, Ct));
    }

    [Fact]
    public void Null_configuration_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Configured(w => w.Notices(null!)));
        Assert.Throws<ArgumentNullException>(() => Configured(w => w.Notices<Meet>(null!)));
    }

    [Fact]
    public void Per_type_option_on_an_abstract_notice_is_rejected_because_it_would_apply_to_nothing()
    {
        Assert.Throws<ArgumentException>(() => Configured(w => w.Notices<INotice>(options => options.Parallel())));
    }
}
