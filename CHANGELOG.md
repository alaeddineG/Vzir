# Changelog

Wkeel follows [Semantic Versioning](https://semver.org/). The [README](https://github.com/alaeddineG/Wkeel#versioning) says what counts as a breaking change.

## 1.0.0

First release.

### Added

- `IWkeel.Execute` routes an `IOperation` or `IRequest<TResult>` to exactly one handler, and throws `HandlerNotFoundException` or `AmbiguousHandlerException` when it cannot.
- `IWkeel.Notify` routes an `INotice` to zero or more handlers, sequentially by default and in parallel when configured, globally or per notice type.
- `IInterceptor<TMessage, TResult>` and `IOperationInterceptor<TOperation>` wrap `Execute`, open-generic or closed, in registration order.
- `ScanAssembly` and `ScanAssemblyContaining<T>` register handlers from an assembly, and `AddHandler<T>` registers one.
- `ValidateOnBuild` reports every missing or duplicate handler at startup.
- `IWkeelRegistry` lists the routed messages and their handlers for tooling.
- `Execute` and `Notify` report an `Activity` on the `Wkeel` source.
- A [migration guide from MediatR](https://github.com/alaeddineG/Wkeel/blob/main/docs/migrating-from-mediatr.md).
