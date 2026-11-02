namespace Wkeel.Tests;

internal interface IAudited;

internal sealed record AuditedArchive : IOperation, IAudited;

internal sealed class AuditedArchiveHandler : IOperationHandler<AuditedArchive>
{
    public Task Handle(AuditedArchive operation, CancellationToken ct) => Task.CompletedTask;
}

internal abstract class Trace<TMessage, TResult>(Journal journal, string name) : IInterceptor<TMessage, TResult>
{
    public async Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct)
    {
        journal.Add($"{name}:in");
        var result = await next();
        journal.Add($"{name}:out");
        return result;
    }
}

internal sealed class Outer<TMessage, TResult>(Journal journal) : Trace<TMessage, TResult>(journal, "outer");

internal sealed class Inner<TMessage, TResult>(Journal journal) : Trace<TMessage, TResult>(journal, "inner");

internal sealed class OnlyAudited<TMessage, TResult>(Journal journal) : Trace<TMessage, TResult>(journal, "audited")
    where TMessage : IAudited;

internal sealed class OnlyClassResults<TMessage, TResult>(Journal journal) : Trace<TMessage, TResult>(journal, "class-result")
    where TResult : class;

internal sealed class SwappedParameters<TResult, TMessage>(Journal journal) : Trace<TMessage, TResult>(journal, "swapped");

internal sealed class CapturesTypes<TMessage, TResult>(Journal journal) : IInterceptor<TMessage, TResult>
{
    public Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct)
    {
        journal.Add((typeof(TMessage), message));
        return next();
    }
}

internal sealed class CapturesResult<TMessage, TResult>(Journal journal) : IInterceptor<TMessage, TResult>
{
    public async Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct)
    {
        var result = await next();
        journal.Add(result!);
        return result;
    }
}

internal sealed class CapturesToken<TMessage, TResult>(Journal journal) : IInterceptor<TMessage, TResult>
{
    public Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct)
    {
        journal.Add(ct);
        return next();
    }
}

internal sealed class CapturesScope<TMessage, TResult>(Journal journal, ScopedProbe probe) : IInterceptor<TMessage, TResult>
{
    public Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct)
    {
        journal.Add(probe);
        return next();
    }
}

internal sealed class CapturesInstance<TMessage, TResult>(Journal journal) : IInterceptor<TMessage, TResult>
{
    public Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct)
    {
        journal.Add(this);
        return next();
    }
}

internal sealed class ShortCircuit<TMessage, TResult>(Journal journal) : IInterceptor<TMessage, TResult>
{
    public Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct)
    {
        journal.Add("short-circuit");
        return Task.FromResult(default(TResult)!);
    }
}

/// <summary>Records its own construction, to prove a short-circuit never builds what is inside it.</summary>
internal sealed class RecordsConstruction<TMessage, TResult> : IInterceptor<TMessage, TResult>
{
    public RecordsConstruction(Journal journal) => journal.Add("constructed");

    public Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct) => next();
}

internal sealed class RunsNextTwice<TMessage, TResult> : IInterceptor<TMessage, TResult>
{
    public async Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct)
    {
        await next();
        return await next();
    }
}

internal sealed class TranslatesFailures<TMessage, TResult> : IInterceptor<TMessage, TResult>
{
    public async Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct)
    {
        try
        {
            return await next();
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException("translated", exception);
        }
    }
}

internal sealed class GetNameCache : IInterceptor<GetName, string>
{
    public Task<string> Intercept(GetName message, Next<string> next, CancellationToken ct) => Task.FromResult("cached");
}

internal sealed class GetNameTrace(Journal journal) : Trace<GetName, string>(journal, "closed");

internal sealed class TwoResultsIntTrace(Journal journal) : Trace<TwoResults, int>(journal, "closed");

internal sealed class ArchiveTrace(Journal journal) : IOperationInterceptor<Archive>
{
    public async Task Intercept(Archive operation, Next next, CancellationToken ct)
    {
        journal.Add("closed:in");
        await next();
        journal.Add("closed:out");
    }
}

internal sealed class ArchiveVeto(Journal journal) : IOperationInterceptor<Archive>
{
    public Task Intercept(Archive operation, Next next, CancellationToken ct)
    {
        journal.Add("veto");
        return Task.CompletedTask;
    }
}

internal sealed class BothKindsOperationTrace(Journal journal) : IOperationInterceptor<BothKinds>
{
    public Task Intercept(BothKinds operation, Next next, CancellationToken ct)
    {
        journal.Add("closed");
        return next();
    }
}

internal sealed class NotAnInterceptor<TMessage, TResult>;

internal abstract class AbstractInterceptor<TMessage, TResult> : IInterceptor<TMessage, TResult>
{
    public abstract Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct);
}

internal sealed class OneParameter<TMessage> : IInterceptor<TMessage, int>
{
    public Task<int> Intercept(TMessage message, Next<int> next, CancellationToken ct) => next();
}

internal sealed class ThreeParameters<TMessage, TResult, TExtra> : IInterceptor<TMessage, TResult>
{
    public Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct) => next();
}

internal sealed class SameParameterTwice<TMessage, TUnused> : IInterceptor<TMessage, TMessage>
{
    public Task<TMessage> Intercept(TMessage message, Next<TMessage> next, CancellationToken ct) => next();
}

internal sealed class OpenOperationInterceptor<TOperation> : IOperationInterceptor<TOperation>
    where TOperation : IOperation
{
    public Task Intercept(TOperation operation, Next next, CancellationToken ct) => next();
}
