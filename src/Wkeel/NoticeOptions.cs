namespace Wkeel;

/// <summary>Chooses how notice handlers run, inside <see cref="WkeelBuilder.Notices(Action{NoticeOptions})"/>.</summary>
public sealed class NoticeOptions
{
    internal NoticeOptions()
    {
    }

    /// <summary>Gets whether handlers run in parallel, or <see langword="null"/> when nothing was chosen.</summary>
    internal bool? IsParallel { get; private set; }

    /// <summary>Runs handlers one at a time, in registration order. The first exception stops the rest.</summary>
    public void Sequential() => IsParallel = false;

    /// <summary>Runs handlers concurrently and waits for all of them. Failures surface as one <see cref="AggregateException"/>.</summary>
    /// <remarks>
    /// Handlers start in registration order on the calling thread and overlap at their awaits.
    /// They share the caller's scope, so they must not use non-thread-safe scoped services such as a <c>DbContext</c>.
    /// </remarks>
    public void Parallel() => IsParallel = true;
}
