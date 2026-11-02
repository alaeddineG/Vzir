namespace Wkeel;

/// <summary>Continues the interceptor chain with the message and token it was entered with.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <returns>A task that completes with the result of the rest of the chain.</returns>
public delegate Task<TResult> Next<TResult>();

/// <summary>Continues the interceptor chain from an <see cref="IOperationInterceptor{TOperation}"/>.</summary>
/// <returns>A task that completes when the rest of the chain has finished.</returns>
public delegate Task Next();
