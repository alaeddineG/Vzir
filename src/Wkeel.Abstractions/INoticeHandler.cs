namespace Wkeel;

/// <summary>Handles one <see cref="INotice"/> type. A notice may have any number of handlers.</summary>
/// <typeparam name="TNotice">The notice type.</typeparam>
public interface INoticeHandler<in TNotice>
    where TNotice : INotice
{
    /// <summary>Handles the notice.</summary>
    /// <param name="notice">The notice.</param>
    /// <param name="ct">The token to observe for cancellation.</param>
    /// <returns>A task that completes when the notice has been handled.</returns>
    Task Handle(TNotice notice, CancellationToken ct);
}
