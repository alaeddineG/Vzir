namespace Wkeel;

/// <summary>Handles one <see cref="IOperation"/> type.</summary>
/// <typeparam name="TOperation">The operation type.</typeparam>
public interface IOperationHandler<in TOperation>
    where TOperation : IOperation
{
    /// <summary>Handles the operation.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="ct">The token to observe for cancellation.</param>
    /// <returns>A task that completes when the operation has been handled.</returns>
    Task Handle(TOperation operation, CancellationToken ct);
}
