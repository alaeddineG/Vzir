using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Wkeel.Tests.Fixture;

namespace Wkeel.Tests;

[Collection(nameof(ActivityListeners))]
public sealed class DiagnosticsTests : IDisposable
{
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly Journal _journal = new();
    private readonly ActivityListener _listener;
    private ActivitySamplingResult _sampling = ActivitySamplingResult.AllDataAndRecorded;

    public DiagnosticsTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Wkeel",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => _sampling,
            ActivityStopped = _stopped.Enqueue,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private ServiceProvider Configured(Action<WkeelBuilder> configure) =>
        Provider(configure, collection =>
        {
            collection.AddSingleton(_journal);
            collection.AddSingleton(new Rendezvous());
        });

    private static Dictionary<string, object?> Tags(Activity activity) =>
        activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value);

    [Fact]
    public async Task Execute_of_an_operation_reports_an_activity()
    {
        using var provider = Configured(w => w.AddHandler<ArchiveHandler>());
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Archive(1), Ct);

        var activity = Assert.Single(_stopped);
        Assert.Equal("Wkeel", activity.Source.Name);
        Assert.Equal("Execute Archive", activity.DisplayName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Equal(
            new Dictionary<string, object?>
            {
                ["wkeel.verb"] = "Execute",
                ["wkeel.message"] = "Wkeel.Tests.Archive",
                ["wkeel.handler"] = "Wkeel.Tests.ArchiveHandler",
            },
            Tags(activity));
    }

    [Fact]
    public async Task Execute_of_a_request_reports_an_activity_and_still_returns_the_result()
    {
        using var provider = Configured(w => w.AddHandler<GetNameHandler>());
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new GetName(1), Ct);

        var activity = Assert.Single(_stopped);
        Assert.Equal("patient-1", result);
        Assert.Equal("Execute GetName", activity.DisplayName);
        Assert.Equal("Wkeel.Tests.GetNameHandler", Tags(activity)["wkeel.handler"]);
    }

    [Fact]
    public async Task Notify_reports_one_activity_listing_its_handlers_in_run_order()
    {
        using var provider = Configured(w => w.AddHandler<SecondListener>().AddHandler<FirstListener>());
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new PatientMoved(1), Ct);

        var activity = Assert.Single(_stopped);
        Assert.Equal("Notify PatientMoved", activity.DisplayName);
        Assert.Equal(
            new Dictionary<string, object?>
            {
                ["wkeel.verb"] = "Notify",
                ["wkeel.message"] = "Wkeel.Tests.PatientMoved",
                ["wkeel.handler"] = "Wkeel.Tests.SecondListener, Wkeel.Tests.FirstListener",
            },
            Tags(activity));
    }

    [Fact]
    public async Task Notify_with_no_handler_still_reports_an_activity_with_no_handler_tag()
    {
        using var provider = Configured(_ => { });
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new UnhandledNotice(), Ct);

        var activity = Assert.Single(_stopped);
        Assert.Equal("Notify UnhandledNotice", activity.DisplayName);
        Assert.Equal("Wkeel.Tests.UnhandledNotice", Tags(activity)["wkeel.message"]);
        Assert.DoesNotContain("wkeel.handler", Tags(activity).Keys);
    }

    [Fact]
    public async Task Activity_lasts_until_the_handler_has_finished()
    {
        using var provider = Configured(w => w.AddHandler<HeldHandler>());
        using var scope = provider.CreateScope();
        var rendezvous = provider.GetRequiredService<Rendezvous>();

        var execute = scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Held(), Ct);
        await rendezvous.Left.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Empty(_stopped);

        rendezvous.Right.TrySetResult();
        await execute;

        Assert.Single(_stopped);
    }

    [Fact]
    public async Task Handler_and_interceptors_run_inside_the_activity()
    {
        using var provider = Configured(w => w.AddHandler<SeeActivityHandler>().Intercept(typeof(SeeActivityInterceptor<,>)));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new SeeActivity(), Ct);

        var activity = Assert.Single(_stopped);
        Assert.Equal(2, _journal.Entries.Count);
        Assert.All(_journal.Entries, entry => Assert.Same(activity, entry));
    }

    [Fact]
    public async Task Parallel_handlers_run_inside_the_notify_activity()
    {
        using var provider = Configured(w => w
            .AddHandler<SeeNoticeListener>()
            .AddHandler<SecondSeeNoticeListener>()
            .Notices(options => options.Parallel()));
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new SeeNotice(), Ct);

        var activity = Assert.Single(_stopped);
        Assert.Equal(2, _journal.Entries.Count);
        Assert.All(_journal.Entries, entry => Assert.Same(activity, entry));
    }

    [Fact]
    public async Task Notify_inside_a_handler_is_a_child_of_its_execute()
    {
        using var provider = Configured(w => w.AddHandler<AnnounceHandler>().AddHandler<FirstListener>());
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Announce(1), Ct);

        var notify = Assert.Single(_stopped, activity => activity.DisplayName == "Notify PatientMoved");
        var execute = Assert.Single(_stopped, activity => activity.DisplayName == "Execute Announce");
        Assert.Same(execute, notify.Parent);
        Assert.Null(execute.Parent);
    }

    [Fact]
    public async Task Activity_is_a_child_of_the_callers_and_does_not_leak_back_to_it()
    {
        using var provider = Configured(w => w.AddHandler<ArchiveHandler>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();
        using var caller = new Activity("caller").Start();

        // The handler finishes synchronously, so nothing but the async wrapper can restore this.
        var execute = wkeel.Execute(new Archive(1), Ct);

        Assert.Same(caller, Activity.Current);
        await execute;
        Assert.Same(caller, Assert.Single(_stopped).Parent);
    }

    [Fact]
    public async Task Handler_failure_marks_the_activity_and_propagates_unchanged()
    {
        using var provider = Configured(w => w.AddHandler<FailHandler>());
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new Fail(), Ct));

        var activity = Assert.Single(_stopped);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(exception.Message, activity.StatusDescription);

        var recorded = Assert.Single(activity.Events);
        Assert.Equal("exception", recorded.Name);
        Assert.Contains(recorded.Tags, tag => tag.Key == "exception.type" && (string?)tag.Value == exception.GetType().ToString());
    }

    [Fact]
    public async Task Request_failure_marks_the_activity()
    {
        using var provider = Configured(w => w.AddHandler<FailRequestHandler>());
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<TimeoutException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new FailRequest(), Ct));

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(_stopped).Status);
    }

    [Fact]
    public async Task Parallel_failures_mark_the_activity_with_the_aggregate()
    {
        using var provider = Configured(w => w
            .AddHandler<ThrowsAfterAwaiting>()
            .AddHandler<ThrowsBeforeReturningTask>()
            .Notices(options => options.Parallel()));
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<AggregateException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new Trouble(), Ct));

        var activity = Assert.Single(_stopped);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Contains(
            Assert.Single(activity.Events).Tags,
            tag => tag.Key == "exception.type" && (string?)tag.Value == typeof(AggregateException).ToString());
    }

    [Fact]
    public async Task Caller_cancelling_is_not_a_failure()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        using var provider = Configured(w => w.AddHandler<CancelledByCallerHandler>());
        using var scope = provider.CreateScope();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Execute(new CancelledByCaller(), source.Token));

        var activity = Assert.Single(_stopped);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Empty(activity.Events);
    }

    [Fact]
    public async Task Handler_cancelling_itself_is_a_failure_when_the_caller_did_not_cancel()
    {
        using var provider = Configured(w => w.AddHandler<CancelsItself>());
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => scope.ServiceProvider.GetRequiredService<IWkeel>().Notify(new Trouble(), Ct));

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(_stopped).Status);
    }

    [Fact]
    public async Task Failure_that_never_dispatches_reports_no_activity()
    {
        using var provider = Configured(w => w.AddHandler<GetNameHandler>().AddHandler<SecondGetNameHandler>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        await Assert.ThrowsAsync<HandlerNotFoundException>(() => wkeel.Execute(new Archive(1), Ct));
        await Assert.ThrowsAsync<AmbiguousHandlerException>(() => wkeel.Execute(new GetName(1), Ct));
        await Assert.ThrowsAsync<HandlerNotFoundException>(() => wkeel.Execute<string>(new TwoResults(), Ct));

        Assert.Empty(_stopped);
    }

    [Fact]
    public async Task A_sampled_out_message_runs_the_same_and_reports_nothing()
    {
        _sampling = ActivitySamplingResult.None;
        using var provider = Configured(w => w.AddHandler<GetNameHandler>().AddHandler<FailHandler>());
        using var scope = provider.CreateScope();
        var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();

        var result = await wkeel.Execute(new GetName(1), Ct);
        await Assert.ThrowsAsync<TimeoutException>(() => wkeel.Execute(new Fail(), Ct));

        Assert.Equal("patient-1", result);
        Assert.Empty(_stopped);
    }
}
