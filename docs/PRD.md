# Wkeel: Product Requirements Document

| Field | Value |
|---|---|
| Status | Draft v0.1 |
| Date | 2026-11-02 |
| Owner | Aladdin |
| Target | .NET 10, C# 14 |
| Package | `Wkeel` (runtime), `Wkeel.Abstractions` (contracts) |

---

## 1. Context

### 1.1 Problem

In-process mediation in .NET is served by MediatR (commercial license since v13), Wolverine (a full messaging framework), and a handful of micro-libraries. Teams that only need "route this message to its one handler, wrap it with a few cross-cutting concerns, fan this event out to whoever listens" are paying for one of:

- a license and a dependency tree sized for far more than that,
- a framework whose in-process verbs (`Send`, `Publish`, `Invoke`) collide with the transport library already in the solution,
- a micro-library with no pipeline, no diagnostics, and no startup validation.

The vocabulary collision is the sharpest pain. In a codebase that also uses MassTransit, `mediator.Send(x)` and `bus.Send(x)` sit on adjacent lines and mean different things. The only disambiguator is the variable name.

### 1.2 Name

**Wkeel**: Tunisian pronunciation of وكيل (wakīl), the person you delegate to. You hand it the request, it deals with the party you never see. Package `Wkeel`, interface `IWkeel`, pronounced "wkeel".

### 1.3 Non-goals

- Not a message bus. No transport, no serialization, no retry, no outbox, no scheduling.
- Not a CQRS framework. No enforced command/query split, no read/write semantics in the type system.
- Not a MediatR drop-in. Shared nouns where they are the most neutral English, different verbs everywhere it matters.
- No source generators in v1. Reflection at startup, cached delegates at runtime.
- No streaming (`IAsyncEnumerable`) in v1.
- No bus integration in v1. A separate `Wkeel.MassTransit` package is a later RFC.

---

## 2. Ubiquitous Language

Settled by one criterion: **no verb may collide with MassTransit's public API**, and secondarily not with Wolverine's. The nouns were chosen for neutrality on read/write semantics.

### 2.1 Vocabulary

| Term | Kind | Meaning |
|---|---|---|
| `IWkeel` | entry point | The only object application code talks to |
| `Execute` | verb | Route one message to exactly one handler |
| `Notify` | verb | Route one message to zero or more handlers |
| `IOperation` | message | Executed, returns nothing |
| `IRequest<TResult>` | message | Executed, returns `TResult` |
| `INotice` | message | Notified, returns nothing |
| `IOperationHandler<TOperation>` | handler | Handles one `IOperation` type |
| `IRequestHandler<TRequest, TResult>` | handler | Handles one `IRequest<TResult>` type |
| `INoticeHandler<TNotice>` | handler | Handles one `INotice` type, may be one of many |
| `IInterceptor<TMessage, TResult>` | pipeline | Wraps `Execute`, sees message and result, may short-circuit |
| `Next<TResult>` | delegate | Continues the interceptor chain |

### 2.2 Shape

```
        Execute ──► IOperation         ──► exactly one handler ──► (void)
        Execute ──► IRequest<TResult>  ──► exactly one handler ──► TResult
         Notify ──► INotice            ──► 0..n handlers       ──► (void)

                      ┌───────────────────────────────────────────┐
   Execute(msg) ─────►│ Interceptor 1 ─► Interceptor 2 ─► Handler │─────► result
                      └───────────────────────────────────────────┘
                       registration order = execution order, outermost first
```

### 2.3 Call site, which is the test

```csharp
await wkeel.Execute(new ArchivePatient(id), ct);                   // in-process, void
var p = await wkeel.Execute(new GetPatient(id), ct);               // in-process, returns Patient
await wkeel.Notify(new PatientArchived(id), ct);                   // in-process, fan-out
await bus.Publish(new PatientArchivedIntegrationEvent(id), ct);    // network (MassTransit)
```

Three different verbs, three different meanings. A reader never needs the receiver's variable name.

### 2.4 Collision map (why these words)

| Term | MassTransit | Wolverine | MediatR | Decision |
|---|---|---|---|---|
| Send | yes | yes | yes | rejected |
| Publish | yes | yes | yes | rejected |
| Request (verb) | yes (`IRequestClient`) | no | no | rejected as verb |
| Invoke | no | yes | no | rejected |
| Consume / Consumer | yes | no | no | rejected |
| Filter | yes (`IFilter<T>`) | no | no | rejected |
| Middleware | no | yes | no | rejected |
| Behavior | no | no | yes | rejected, says nothing |
| Event | implied | yes | no | rejected, is the exact confusion |
| Notification | no | no | yes | rejected, collides with application-domain "notifications" |
| Execute | no | no | no | **taken** |
| Notify | no | no | no | **taken** |
| Request (noun) | no `IRequest` interface in MT | no | yes | **taken**, see 2.5 |
| Operation | no | no | no | **taken** |
| Notice | no | no | no | **taken** |
| Interceptor | no | no | no | **taken** |
| Handler | no (uses Consumer) | yes | yes | **taken**, generic enough |

### 2.5 Accepted costs

- `IRequest<T>` / `IRequestHandler<,>` are MediatR's exact names. Harmless in a clean project. In a file that imports both `MediatR` and `Wkeel`, the compiler reports CS0104. **Migration is per assembly, not per file.** The README says so.
- `Operation` vs `Request` is a distinction by return type, not by meaning. Neither word says anything about reads or writes. **Documented as convention, not enforced by the type system.**
- `Notice` does not scream "domain event". One README line: *notices are the domain-event mechanism*.

### 2.6 Rejected alternatives (for the record)

| Candidate | Why rejected |
|---|---|
| `Ask` / `IQuestion<T>` | Reads as read-only; a `CreatePatient` returning an id becomes a lying question |
| `Call` / `IProcedure<T>` | Read/write neutral and collision-free, but "call a GetPatient" reads oddly and Procedure vs Operation is a distinction nobody can explain in one sentence |
| `Dispatch` | Connotes "put on a queue" |
| `Raise` / `ISignal` | Fine, lost to `Notify` / `INotice` on English readability |
| Fluent call site (`wkeel.With(x).Call()`) | Per-call allocation, awaitable half-built state, does not remove the naming problem, moves it to the terminal method |
| Two result verbs (`Execute` + `Ask`) | Overload resolution already distinguishes `IOperation` from `IRequest<T>`; a second verb is surface without information |

---

## 3. Requirements

### 3.1 Functional

| ID | Requirement | Priority |
|---|---|---|
| F1 | `Execute(IOperation, CancellationToken)` routes to exactly one `IOperationHandler<T>` | Must |
| F2 | `Execute(IRequest<TResult>, CancellationToken)` routes to exactly one `IRequestHandler<T, TResult>` and returns `TResult` | Must |
| F3 | `Notify(INotice, CancellationToken)` routes to all registered `INoticeHandler<T>` | Must |
| F4 | Zero notice handlers is not an error | Must |
| F5 | Notice handlers run sequentially by default, in registration order | Must |
| F6 | Notice handlers can be configured to run in parallel (`Task.WhenAll`), globally or per notice type | Should |
| F7 | On sequential notify, first handler exception aborts the remaining handlers and propagates | Must |
| F8 | On parallel notify, all handlers run; exceptions aggregate into `AggregateException` | Should |
| F9 | `IInterceptor<TMessage, TResult>` wraps `Execute` for both `IOperation` and `IRequest<T>` | Must |
| F10 | Open-generic interceptor registration applies to all executed messages | Must |
| F11 | Closed-generic interceptor registration applies to one message type | Must |
| F12 | Interceptor execution order equals registration order in `AddWkeel`; no `Order` property, no attribute | Must |
| F13 | Interceptors may short-circuit by not calling `next` | Must |
| F14 | `Notify` is not intercepted in v1 | Must (explicit non-feature) |
| F15 | Assembly scanning registers all handlers found in given assemblies | Must |
| F16 | Explicit registration of a single handler type is possible without scanning | Should |
| F17 | Handler lifetime defaults to Transient; configurable to Scoped per registration | Should |
| F18 | Startup validation (opt-in, on by default in Development): every `IOperation` and `IRequest<T>` type found in scanned assemblies has exactly one handler; missing or duplicate handlers throw at `AddWkeel` build time with the offending type names | Must |
| F19 | Runtime: `Execute` on a message with no registered handler throws `HandlerNotFoundException` naming the message type | Must |
| F20 | Runtime: `Execute` on a message with multiple handlers throws `AmbiguousHandlerException` naming all handler types | Must |
| F21 | `IWkeel` is registered as Scoped and resolves handlers from the scope it was resolved in; Wkeel never creates its own scope | Must |
| F22 | `CancellationToken` is a required parameter on every verb and every handler method (no optional default) | Must |

### 3.2 Non-functional

| ID | Requirement | Target |
|---|---|---|
| N1 | Dependencies of `Wkeel.Abstractions` | none |
| N2 | Dependencies of `Wkeel` | `Microsoft.Extensions.DependencyInjection.Abstractions` only |
| N3 | Allocations per `Execute` on the hot path, excluding handler and message | 0 after warm-up (cached delegates, `ValueTask` where it helps) |
| N4 | Overhead per `Execute` vs direct handler invocation, no interceptors | < 100 ns on Apple M-series, measured by BenchmarkDotNet |
| N5 | Reflection | startup only; runtime dispatch via cached `Func<>` per message type |
| N6 | Thread safety | `IWkeel` safe for concurrent use within a scope |
| N7 | Trimming / AOT | `IsTrimmable=true`; AOT compatibility deferred to v2 (reflection-based registration is the blocker) |
| N8 | Public API surface | under 15 public types in `Wkeel.Abstractions` |
| N9 | Diagnostics | `System.Diagnostics.Activity` per `Execute` and `Notify`, source name `Wkeel`, tags: `wkeel.message`, `wkeel.handler`, `wkeel.verb` |
| N10 | Logging | none built in; interceptors are the logging extension point |
| N11 | Test coverage | 90% line, 100% of public API |

---

## 4. Public API

### 4.1 Wkeel.Abstractions

```csharp
namespace Wkeel;

// Messages
public interface IOperation { }
public interface IRequest<out TResult> { }
public interface INotice { }

// Handlers
public interface IOperationHandler<in TOperation> where TOperation : IOperation
{
    Task Handle(TOperation operation, CancellationToken ct);
}

public interface IRequestHandler<in TRequest, TResult> where TRequest : IRequest<TResult>
{
    Task<TResult> Handle(TRequest request, CancellationToken ct);
}

public interface INoticeHandler<in TNotice> where TNotice : INotice
{
    Task Handle(TNotice notice, CancellationToken ct);
}

// Pipeline
public delegate Task<TResult> Next<TResult>();

public interface IInterceptor<in TMessage, TResult>
{
    Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct);
}

// Entry point
public interface IWkeel
{
    Task Execute(IOperation operation, CancellationToken ct);
    Task<TResult> Execute<TResult>(IRequest<TResult> request, CancellationToken ct);
    Task Notify(INotice notice, CancellationToken ct);
}

// Exceptions
public sealed class HandlerNotFoundException : InvalidOperationException { public Type MessageType { get; } }
public sealed class AmbiguousHandlerException : InvalidOperationException { public Type MessageType { get; } public IReadOnlyList<Type> HandlerTypes { get; } }
```

### 4.2 Internal marker for void operations

`IOperation` flows through the interceptor chain as `IInterceptor<TOperation, Nothing>` where `Nothing` is an `internal readonly struct`. It never appears on a public message, handler, or `IWkeel` signature. Users writing an open-generic interceptor see `TResult = Nothing` only via reflection-level inspection, never in source they must write.

### 4.3 Registration (Wkeel)

```csharp
services.AddWkeel(w => w
    .ScanAssembly(typeof(GetPatient).Assembly)
    .ScanAssemblyContaining<ArchivePatient>()
    .AddHandler<GetPatientHandler>()                       // explicit, no scan
    .Intercept(typeof(LoggingInterceptor<,>))              // open generic, all messages
    .Intercept(typeof(ValidationInterceptor<,>))
    .Intercept<GetPatient, Patient, GetPatientCache>()     // closed, one message
    .Notices(n => n.Sequential())                          // default; or .Parallel()
    .Notices<PatientArchived>(n => n.Parallel())           // per type override
    .HandlerLifetime(ServiceLifetime.Transient)            // default
    .ValidateOnBuild());                                   // default: on in Development
```

Fluent is confined to registration. It runs once, and the chain makes interceptor order visible at the only moment it matters.

### 4.4 Usage

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

sealed class LoggingInterceptor<TMessage, TResult>(ILogger<TMessage> log) : IInterceptor<TMessage, TResult>
{
    public async Task<TResult> Intercept(TMessage m, Next<TResult> next, CancellationToken ct)
    {
        log.LogInformation("Executing {Message}", typeof(TMessage).Name);
        var result = await next();
        log.LogInformation("Executed {Message}", typeof(TMessage).Name);
        return result;
    }
}
```

---

## 5. Architecture

### 5.1 Components

```
  ┌──────────────────────────────────────────────────────────────────┐
  │ Wkeel.Abstractions                                               │
  │   IOperation, IRequest<T>, INotice                               │
  │   IOperationHandler<>, IRequestHandler<,>, INoticeHandler<>      │
  │   IInterceptor<,>, Next<T>, IWkeel, exceptions                   │
  └──────────────────────────────────────────────────────────────────┘
                                   ▲
                                   │ references
  ┌──────────────────────────────────────────────────────────────────┐
  │ Wkeel                                                            │
  │                                                                  │
  │  AddWkeel ──► WkeelBuilder ──► Registry (built once)             │
  │                                   │                              │
  │                                   ▼                              │
  │              ┌─────────────────────────────────────┐             │
  │              │ Dispatcher cache                    │             │
  │              │  messageType -> Func<sp, msg, ct>   │             │
  │              │  (handler + interceptor chain       │             │
  │              │   composed once per message type)   │             │
  │              └─────────────────────────────────────┘             │
  │                                   │                              │
  │   IWkeel (scoped) ──► Execute/Notify ──► cache lookup ──► invoke │
  └──────────────────────────────────────────────────────────────────┘
```

### 5.2 Dispatch

1. `AddWkeel` scans assemblies, builds a `Registry`: message type → handler type(s), interceptor list, notice options.
2. `ValidateOnBuild` walks the registry: every `IOperation` / `IRequest<T>` has exactly one handler, every interceptor type closes correctly. Fails fast with all errors, not the first.
3. Per message type, a `Func<IServiceProvider, object, CancellationToken, Task<TResult>>` is composed once: resolve handler from `sp`, wrap in interceptors outermost-first, cache in a `FrozenDictionary<Type, ...>`.
4. `IWkeel.Execute` does one dictionary lookup and one delegate call. No reflection, no LINQ, no allocation beyond the `Task`.
5. `IWkeel.Notify` looks up the handler type list, resolves each from `sp`, runs sequentially or `Task.WhenAll` per configuration.

### 5.3 Scoping rule

`IWkeel` is Scoped. It captures the `IServiceProvider` it was resolved from and resolves handlers from it. It never calls `CreateScope`. This is what makes a handler's injected `DbContext`, `IPublishEndpoint`, or ambient `ConsumeContext` the same instance the caller has. Breaking this rule breaks transactional consistency silently; it is a Must.

---

## 6. Decisions

Each is Context / Decision / Consequences in one row.

| # | Decision | Context | Consequences |
|---|---|---|---|
| D1 | Two verbs, `Execute` and `Notify` | Only non-colliding verbs that are plain English for one-to-one and one-to-many | Result vs void is distinguished by overload, not verb |
| D2 | `IOperation` and `IRequest<T>` as separate message interfaces, no `Unit` on the public surface | `Unit` leaks implementation into user code; one interface with `Unit` doubles handler boilerplate for void | Two handler interfaces; `Nothing` marker stays internal |
| D3 | `IRequest<T>` name reused from MediatR | Most neutral English noun; MassTransit has no `IRequest` interface | CS0104 in mixed files; migration per assembly |
| D4 | `Interceptor` not `Behavior` | `Behavior` is content-free; `Filter` is MassTransit's; `Middleware` is Wolverine's and ASP.NET's | Users coming from MediatR learn one new word |
| D5 | One interceptor interface covers `IOperation` and `IRequest<T>` | Two interfaces double every cross-cutting concern | Internal `Nothing` result for operations |
| D6 | Interceptor order = registration order, no `Order` property | Attribute and property ordering hides order across files | Order is visible in exactly one place, `AddWkeel` |
| D7 | `Notify` not intercepted in v1 | n handlers, no result, different shape; v1 scope control | Cross-cutting on notices is done inside handlers or deferred to v2 |
| D8 | Sequential notice handlers by default | Deterministic, exception semantics are obvious, matches MediatR default | Parallel is opt-in, aggregates exceptions |
| D9 | Startup validation on by default in Development | A missing handler is a configuration bug, not a runtime condition | Production can disable for startup time; runtime still throws typed exceptions |
| D10 | `CancellationToken` required, not optional | Optional tokens get forgotten; a mediator sits on every request path | Slightly noisier call sites; every handler is cancellable |
| D11 | Reflection at startup, cached delegates at runtime, no source generator in v1 | Source generator is the right v2, but doubles v1 effort and complicates debugging | AOT deferred; startup cost is one scan |
| D12 | `Wkeel.Abstractions` separate from `Wkeel` | Domain assemblies reference contracts without pulling DI | Two packages to version together |
| D13 | No bus integration in v1 | Scope control; the vocabulary already prevents the confusion the bridge would need to manage | `Wkeel.MassTransit` is its own RFC |

---

## 7. Milestones

| Milestone | Scope | Exit criterion |
|---|---|---|
| M0 Skeleton | Abstractions, `IWkeel`, `Execute` for both message kinds, explicit registration | Unit tests green, no scanning yet |
| M1 Pipeline | `IInterceptor`, open and closed registration, ordering | Logging + validation interceptors in samples |
| M2 Notify | `INotice`, sequential + parallel, exception semantics | Tests for 0, 1, n handlers, both modes |
| M3 Scanning + validation | `ScanAssembly`, `ValidateOnBuild`, typed exceptions | Missing/duplicate handler caught at build with all errors listed |
| M4 Perf + diagnostics | Delegate cache, `FrozenDictionary`, `Activity`, BenchmarkDotNet suite | N3, N4 met and published in README |
| M5 Release | README, migration guide from MediatR, NuGet `1.0.0` | Package IDs `Wkeel` and `Wkeel.Abstractions` published |

---

## 8. Open Questions

| # | Question | Default if unanswered |
|---|---|---|
| Q1 | `ValueTask` on `IWkeel` verbs for the zero-allocation path? | `Task` in v1; `ValueTask` is a breaking change later, so decide before M4 |
| Q2 | Should handlers be able to declare `Scoped` lifetime via attribute, or registration only? | Registration only; attributes hide lifetime |
| Q3 | Notice handler ordering when parallel is off: registration order or declared `IOrderedNoticeHandler`? | Registration order; no ordering interface |
| Q4 | Expose the `Registry` for tooling (list all messages and handlers)? | Yes, read-only, `IWkeelRegistry` singleton, cheap and useful for docs generation |

---

## 9. Glossary

| Word | One line |
|---|---|
| Wkeel | The library. Tunisian for وكيل, your delegate. |
| Execute | Route a message to its one handler. |
| Notify | Route a message to all its handlers. |
| Operation | A message you execute and get nothing back. |
| Request | A message you execute and get a result back. |
| Notice | A message you notify; the domain-event mechanism. |
| Interceptor | Code that wraps an Execute, sees the message and result, may short-circuit. |
| Next | The delegate an interceptor calls to continue. |
