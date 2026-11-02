# Wkeel

[![License: MIT](https://img.shields.io/github/license/alaeddineG/Wkeel)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Status: design](https://img.shields.io/badge/status-design-orange)](docs/PRD.md)

An in-process mediator for .NET. Route a message to its one handler, wrap the call in interceptors, fan a notice out to whoever listens. No transport, no framework, one dependency.

**Wkeel** is the Tunisian pronunciation of وكيل (*wakīl*), the person you delegate to. You hand it the request, it deals with the party you never see.

> [!IMPORTANT]
> Wkeel is at the design stage: there is no code and no NuGet package yet. The snippets below show the API proposed in the [PRD](docs/PRD.md), and names may still change. Design feedback is the most useful contribution right now, see [Contributing](#contributing).

## Why

Teams that only need in-process dispatch with a few cross-cutting concerns currently pick a compromise:

| Option | Cost |
|---|---|
| MediatR | Dual-licensed since v13 (RPL-1.5 or commercial) |
| Wolverine | A full messaging framework for a job that needs a fraction of it |
| Micro-libraries | Often no pipeline, no diagnostics, no startup validation |

The sharper problem is vocabulary. In a codebase that also uses MassTransit, `mediator.Send(x)` and `bus.Send(x)` sit on adjacent lines and mean different things, and the variable name is the only disambiguator. Wkeel stays away from `Send` and `Publish`:

```csharp
await wkeel.Execute(new ArchivePatient(id), ct);                   // in-process, void
var patient = await wkeel.Execute(new GetPatient(id), ct);         // in-process, returns Patient
await wkeel.Notify(new PatientArchived(id), ct);                   // in-process, fan-out
await bus.Publish(new PatientArchivedIntegrationEvent(id), ct);    // network (MassTransit)
```

Three verbs, three meanings. A reader never needs the receiver's variable name.

## Concepts

| Term | Meaning |
|---|---|
| `IWkeel` | The only object application code talks to |
| `Execute` | Route one message to exactly one handler |
| `Notify` | Route one message to zero or more handlers |
| `IOperation` | Executed, returns nothing |
| `IRequest<TResult>` | Executed, returns `TResult` |
| `INotice` | Notified, returns nothing. Notices are the domain-event mechanism |
| `IInterceptor<TMessage, TResult>` | Wraps `Execute`, sees the message and result, may short-circuit |

Each message kind has a matching handler interface: `IOperationHandler<T>`, `IRequestHandler<T, TResult>` and `INoticeHandler<T>`.

## Intended API

Define messages and handlers:

```csharp
public sealed record GetPatient(Guid Id) : IRequest<Patient>;
public sealed record ArchivePatient(Guid Id) : IOperation;
public sealed record PatientArchived(Guid Id, DateTimeOffset At) : INotice;

sealed class GetPatientHandler(IPatientRepository repo) : IRequestHandler<GetPatient, Patient>
{
    public Task<Patient> Handle(GetPatient r, CancellationToken ct) => repo.Get(r.Id, ct);
}

sealed class ArchivePatientHandler(IPatientRepository repo, IWkeel wkeel) : IOperationHandler<ArchivePatient>
{
    public async Task Handle(ArchivePatient op, CancellationToken ct)
    {
        await repo.Archive(op.Id, ct);
        await wkeel.Notify(new PatientArchived(op.Id, DateTimeOffset.UtcNow), ct);
    }
}

sealed class AuditOnArchive(IAuditLog audit) : INoticeHandler<PatientArchived>
{
    public Task Handle(PatientArchived n, CancellationToken ct) => audit.Write(n, ct);
}
```

Register them:

```csharp
services.AddWkeel(w => w
    .ScanAssemblyContaining<GetPatient>()
    .Intercept(typeof(LoggingInterceptor<,>))      // open generic, all messages, outermost
    .Intercept(typeof(ValidationInterceptor<,>))); // runs inside LoggingInterceptor
```

## Design rules

- **Handlers share the caller's scope.** `IWkeel` is scoped and never creates a scope of its own, so a handler's `DbContext` is the caller's `DbContext`.
- **Interceptor order is registration order.** No `Order` property, no attributes. Order is visible in exactly one place.
- **`CancellationToken` is required** on every verb and every handler method.
- **Configuration bugs fail at startup.** Missing or duplicate handlers are reported together, with type names. Validation is on by default in Development, and `Execute` still throws a typed exception at runtime.
- **Notice handlers run sequentially by default.** The first exception stops the rest. `Notify` is not intercepted in v1.
- **Reflection at startup, cached delegates at runtime.** No source generators in v1.

## Not in scope

Wkeel is not a message bus: no transport, serialization, retry, outbox or scheduling. It is not a CQRS framework, so nothing enforces a command/query split. It is not a MediatR drop-in. Streaming and bus integration are out of scope for v1.

## Coming from MediatR

| MediatR | Wkeel |
|---|---|
| `Send` | `Execute` |
| `Publish` | `Notify` |
| `INotification` | `INotice` |
| `IRequest` (no result) | `IOperation` |
| `IRequest<T>` | `IRequest<T>` |
| `IPipelineBehavior<,>` | `IInterceptor<,>` |

`IRequest<T>` and `IRequestHandler<,>` deliberately keep MediatR's names. A file importing both libraries gets CS0104, so **migrate per assembly, not per file**. A full migration guide is planned for 1.0.

## Known trade-offs

- `IOperation` and `IRequest<T>` differ by return type, not by meaning. Neither says anything about reads or writes. That is a convention, and the type system does not enforce it.
- MassTransit Courier has its own `bus.Execute(routingSlip)`. It only takes a `RoutingSlip`, so the two calls cannot be swapped by accident, but `Execute` is not unique across every MassTransit API.
- `Notice` does not announce itself as a domain event. The docs have to say so.

## Roadmap

| Milestone | Scope | Status |
|---|---|---|
| M0 Skeleton | Abstractions, `IWkeel`, `Execute`, explicit registration | Planned |
| M1 Pipeline | `IInterceptor`, open and closed registration, ordering | Planned |
| M2 Notify | `INotice`, sequential and parallel, exception semantics | Planned |
| M3 Scanning and validation | `ScanAssembly`, `ValidateOnBuild`, typed exceptions | Planned |
| M4 Performance and diagnostics | Delegate cache, `Activity`, BenchmarkDotNet suite | Planned |
| M5 Release | Migration guide, NuGet `1.0.0` | Planned |

The [PRD](docs/PRD.md) holds the requirements, the full vocabulary rationale, the decision log and the open questions.

## Contributing

Issues and design proposals are welcome, read [CONTRIBUTING.md](CONTRIBUTING.md) first. The open questions in [PRD section 8](docs/PRD.md#8-open-questions) are a good place to start.

This project follows the [Code of Conduct](CODE_OF_CONDUCT.md). To report a vulnerability, see [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE)
