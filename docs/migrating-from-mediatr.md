# Migrating from MediatR

Wkeel is not a drop-in replacement. Most of a migration is mechanical renaming, and the compiler finds nearly all of it. The risk is in the [behavior that changes without a compile error](#behavior-that-changes-without-a-compile-error), so read that section before you start.

The MediatR behavior described here was checked against MediatR 14.2.0.

## Check that Wkeel fits

Stay on MediatR, or plan a workaround first, if any of these applies:

| You rely on | In Wkeel |
|---|---|
| A target framework older than .NET 10 | Not supported. Both packages target `net10.0` |
| `IStreamRequest<T>` and `CreateStream` | No equivalent. Streaming is out of scope for v1 |
| A custom `INotificationPublisher` | No equivalent. Notices run sequentially or in parallel, and `Notify` is not intercepted in v1 |
| `INotificationHandler<INotification>`, or a handler of a base notification | Rejected or not called. Write one handler per concrete notice |
| Open-generic handlers such as `Handler<T> : IRequestHandler<Query<T>, R>` | Rejected. Close each one as its own class |
| `Send(object)` or `Publish(object)` on a value typed as `object` | Cast to `IOperation`, `IRequest<T>` or `INotice` first |

## Migration order

`IRequest<T>` and `IRequestHandler<,>` have the same names in both libraries, so a file importing both gets CS0104. Migrate one assembly at a time, and let the two libraries run side by side until the last one is done.

1. Add the `Wkeel` package to the host and call `AddWkeel` alongside `AddMediatR`.
2. In one assembly, replace the `MediatR` reference with `Wkeel.Abstractions`. Reference `Wkeel` only where `AddWkeel` is called.
3. Rename messages, handlers and interceptors using the [mapping](#mapping).
4. Fix each call site, which the compiler lists for you.
5. Work through the [behavior changes](#behavior-that-changes-without-a-compile-error) for that assembly.
6. Add `.ValidateOnBuild()` in development and test environments, so missing and duplicate handlers fail at startup.
7. Repeat for the next assembly, and remove `AddMediatR` after the last one.

A migrated file sometimes still has to call something that has not moved. Keep the reference and import only the one type with an alias, so the two namespaces never meet in that file: `using IMediator = MediatR.IMediator;`. The same works the other way round with `using IWkeel = Wkeel.IWkeel;`.

## Mapping

| MediatR | Wkeel |
|---|---|
| `IMediator`, `ISender`, `IPublisher` | `IWkeel` |
| `Send` | `Execute` |
| `Publish` | `Notify` |
| `IRequest` | `IOperation` |
| `IRequest<Unit>` | `IOperation` |
| `IRequest<T>` | `IRequest<T>` |
| `IRequestHandler<TRequest>` | `IOperationHandler<TOperation>` |
| `IRequestHandler<TRequest, TResponse>` | `IRequestHandler<TRequest, TResult>` |
| `INotification` | `INotice` |
| `INotificationHandler<T>` | `INoticeHandler<T>` |
| `IPipelineBehavior<TRequest, TResponse>` | `IInterceptor<TMessage, TResult>` |
| `RequestHandlerDelegate<T>` | `Next<T>` |
| `RegisterServicesFromAssembly` | `ScanAssembly` |
| `RegisterServicesFromAssemblyContaining<T>` | `ScanAssemblyContaining<T>` |
| `AddOpenBehavior(typeof(X<,>))` | `Intercept(typeof(X<,>))` |
| `AddBehavior<IPipelineBehavior<Q, R>, X>()` | `Intercept<Q, R, X>()` |
| `ForeachAwaitPublisher` | `Notices(n => n.Sequential())`, the default |
| `TaskWhenAllPublisher` | `Notices(n => n.Parallel())`, or `Notices<T>` for one notice type |

## Messages and handlers

A request with no result becomes an operation, and its handler gets its own interface.

MediatR:

```csharp
public sealed record ArchivePatient(Guid Id) : IRequest;
public sealed record GetPatient(Guid Id) : IRequest<Patient>;
public sealed record PatientArchived(Guid Id) : INotification;

sealed class ArchivePatientHandler(IPatientStore store, IPublisher publisher) : IRequestHandler<ArchivePatient>
{
    public async Task Handle(ArchivePatient request, CancellationToken cancellationToken)
    {
        await store.Archive(request.Id, cancellationToken);
        await publisher.Publish(new PatientArchived(request.Id), cancellationToken);
    }
}

sealed class GetPatientHandler(IPatientStore store) : IRequestHandler<GetPatient, Patient>
{
    public Task<Patient> Handle(GetPatient request, CancellationToken cancellationToken) =>
        store.Get(request.Id, cancellationToken);
}

sealed class AuditOnArchive(IAuditLog audit) : INotificationHandler<PatientArchived>
{
    public Task Handle(PatientArchived notification, CancellationToken cancellationToken) =>
        audit.Write(notification.Id, cancellationToken);
}
```

Wkeel:

```csharp
public sealed record ArchivePatient(Guid Id) : IOperation;
public sealed record GetPatient(Guid Id) : IRequest<Patient>;
public sealed record PatientArchived(Guid Id) : INotice;

sealed class ArchivePatientHandler(IPatientStore store, IWkeel wkeel) : IOperationHandler<ArchivePatient>
{
    public async Task Handle(ArchivePatient operation, CancellationToken ct)
    {
        await store.Archive(operation.Id, ct);
        await wkeel.Notify(new PatientArchived(operation.Id), ct);
    }
}

sealed class GetPatientHandler(IPatientStore store) : IRequestHandler<GetPatient, Patient>
{
    public Task<Patient> Handle(GetPatient request, CancellationToken ct) => store.Get(request.Id, ct);
}

sealed class AuditOnArchive(IAuditLog audit) : INoticeHandler<PatientArchived>
{
    public Task Handle(PatientArchived notice, CancellationToken ct) => audit.Write(notice.Id, ct);
}
```

## Call sites

`CancellationToken` is required, so a call that relied on the default stops compiling. Pass the real token. Use `CancellationToken.None` only where nothing can cancel the work, and make that choice visibly.

MediatR:

```csharp
sealed class PatientService(IMediator mediator)
{
    public async Task<Patient> Archive(Guid id)
    {
        await mediator.Send(new ArchivePatient(id));
        return await mediator.Send(new GetPatient(id));
    }
}
```

Wkeel:

```csharp
sealed class PatientService(IWkeel wkeel)
{
    public async Task<Patient> Archive(Guid id, CancellationToken ct)
    {
        await wkeel.Execute(new ArchivePatient(id), ct);
        return await wkeel.Execute(new GetPatient(id), ct);
    }
}
```

`IWkeel` is scoped, so inject it where you injected `IMediator`, but never into a singleton. Inside a hosted service or other singleton, create a scope and resolve it from there, which is what you would do for a `DbContext`.

## Pipeline behaviors

An open-generic behavior becomes an open-generic interceptor, with the same two type parameters and the same order of registration. It wraps operations too. `TResult` is an internal type for them, so treat it as opaque and never name it.

`next` takes no arguments in Wkeel. MediatR's delegate accepts a `CancellationToken`, and Wkeel always passes the caller's token, so an interceptor cannot swap the token or the message.

Check the constraints as you go. A behavior declared `where TRequest : IRequest<TResponse>` skips every `IOperation` after the migration, including messages that were `IRequest<Unit>`. Remove the constraint if it should wrap them.

MediatR:

```csharp
sealed class LoggingBehavior<TRequest, TResponse>(ILogger<TRequest> log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.LogInformation("Handling {Request}", typeof(TRequest).Name);
        var response = await next(cancellationToken);
        log.LogInformation("Handled {Request}", typeof(TRequest).Name);
        return response;
    }
}
```

Wkeel:

```csharp
sealed class LoggingInterceptor<TMessage, TResult>(ILogger<TMessage> log) : IInterceptor<TMessage, TResult>
{
    public async Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct)
    {
        log.LogInformation("Handling {Message}", typeof(TMessage).Name);
        var result = await next();
        log.LogInformation("Handled {Message}", typeof(TMessage).Name);
        return result;
    }
}
```

A closed behavior on a request maps directly to `Intercept<TRequest, TResult, TInterceptor>()`. A closed behavior on a void request becomes an `IOperationInterceptor<TOperation>`, because the operation has no result type to close over.

MediatR's other pipeline hooks all become interceptors:

| MediatR | Wkeel |
|---|---|
| `IRequestPreProcessor<T>` | Code before `await next()` |
| `IRequestPostProcessor<T, R>` | Code after `await next()` |
| `IRequestExceptionHandler<T, R, E>` | `try`/`catch` around `await next()`, returning a result |
| `IRequestExceptionAction<T, E>` | `try`/`catch` around `await next()`, rethrowing |

Interceptors run in registration order, so decide where each of these belongs in the chain.

## Registration

Everything moves into one `AddWkeel` call, and it can only be called once. A behavior registered directly on the service collection as `IPipelineBehavior<,>` has to move here, because a scan does not find interceptors.

MediatR:

```csharp
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<GetPatient>();
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.NotificationPublisher = new TaskWhenAllPublisher();
});
```

Wkeel:

```csharp
services.AddWkeel(w => w
    .ScanAssemblyContaining<GetPatient>()
    .Intercept(typeof(LoggingInterceptor<,>))
    .Notices(n => n.Parallel())
    .ValidateOnBuild());
```

## Behavior that changes without a compile error

These compile and run after the rename, and do something different.

| Situation | MediatR 14.2.0 | Wkeel |
|---|---|---|
| Two handlers of one request in a scan | One is used and the other is silently ignored | `Execute` throws `AmbiguousHandlerException`, and `ValidateOnBuild` reports it at startup |
| No handler | `InvalidOperationException`, after the outer behaviors have run | `HandlerNotFoundException`, a subclass of `InvalidOperationException`, before any interceptor runs |
| Handler of a base notification | Receives derived notifications | Receives only its exact type, so a derived notice can reach nobody, and no error says so |
| `INotificationHandler<INotification>` | Receives every notification | Rejected at registration |
| Order of scanned notification handlers | Follows the order types appear in the assembly | Ordinal order of full type names. If order matters, register with `AddHandler` before the scan |
| One failure in parallel publish | A handler that throws before its first `await` stops the rest from starting. Otherwise the first exception is rethrown | Every handler runs to completion, and failures arrive as one `AggregateException`, even for a single failure |
| Lifetime of the mediator | Transient | Scoped. Resolving `IWkeel` from the root provider fails under scope validation |
| Handler that keeps state in a singleton | Works if you register it that way yourself | Handlers are only Transient or Scoped, and `AddHandler` rejects Singleton. Move the state into a singleton service the handler depends on |

The notification rows are the ones to search for. Before you migrate, list every `INotificationHandler<T>` whose `T` has derived types. A catch-all handler fails loudly at startup, but a base-class handler goes quiet, because a notice with no handler is not an error.

In parallel mode, code that caught a specific exception type from `Publish` now has to look inside `AggregateException.InnerExceptions`.

## Tests

Replace mocks of `IMediator` with mocks of `IWkeel`. Its methods have no default for `CancellationToken`, so setups have to match it. Tests that build a real container call `AddWkeel` once, and a test that resolves `IWkeel` needs a scope.

`.ValidateOnBuild()` is worth one dedicated test: build your real registration and let it throw. Messages handled by another service are reported as missing, so scan only the assemblies this application is responsible for.

`IWkeelRegistry` lists every message and handler after startup. To check a migration, compare it with the handlers you expected:

```csharp
var registry = provider.GetRequiredService<IWkeelRegistry>();
foreach (var (message, handlers) in registry.Notices)
{
    Console.WriteLine($"{message.Name}: {string.Join(", ", handlers.Select(handler => handler.Name))}");
}
```
