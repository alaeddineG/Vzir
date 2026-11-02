namespace Wkeel.Benchmarks;

// Handlers return cached tasks, so what is measured is the dispatch and not the Task.
internal sealed record Ping : IOperation;

internal sealed class PingHandler : IOperationHandler<Ping>
{
    public Task Handle(Ping operation, CancellationToken ct) => Task.CompletedTask;
}

internal sealed record Echo : IRequest<int>;

internal sealed class EchoHandler : IRequestHandler<Echo, int>
{
    private static readonly Task<int> _result = Task.FromResult(1);

    public Task<int> Handle(Echo request, CancellationToken ct) => _result;
}

internal sealed record Tick : INotice;

internal sealed class FirstTickHandler : INoticeHandler<Tick>
{
    public Task Handle(Tick notice, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class SecondTickHandler : INoticeHandler<Tick>
{
    public Task Handle(Tick notice, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class ThirdTickHandler : INoticeHandler<Tick>
{
    public Task Handle(Tick notice, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class PassThrough<TMessage, TResult> : IInterceptor<TMessage, TResult>
{
    public Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct) => next();
}
