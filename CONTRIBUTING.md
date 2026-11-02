# Contributing to Wkeel

Wkeel is pre-alpha. Milestones M0 and M1 are in and the rest of the v1 API is still being built from the [PRD](docs/PRD.md), so scrutiny of the design is as useful as code.

## Ways to help

- **Challenge the design.** Open a [design proposal](https://github.com/alaeddineG/Wkeel/issues/new?template=design_proposal.yml) if a requirement is wrong, contradictory or missing.
- **Answer an open question.** [PRD section 8](docs/PRD.md#8-open-questions) lists decisions that are still open.
- **Report a mistake.** The collision map in [PRD section 2.4](docs/PRD.md#24-collision-map-why-these-words) makes claims about other libraries. Corrections with a source are welcome.

## Before opening a pull request

Open an issue first for anything that changes the public API, the vocabulary or a dependency, so the design is agreed before you write code. Typo and documentation fixes can go straight to a pull request.

The vocabulary in [PRD section 2](docs/PRD.md#2-ubiquitous-language) was chosen deliberately. A proposal to rename a term needs to say which alternatives it beats and why, using the same collision criteria.

## Project rules

These come from the PRD, and pull requests are expected to respect them:

- `Wkeel.Abstractions` has no dependencies.
- `Wkeel` depends only on `Microsoft.Extensions.DependencyInjection.Abstractions`.
- No reflection on the dispatch path. Reflection is confined to startup.
- Wkeel never creates its own `IServiceScope`.
- `CancellationToken` stays required on every verb and handler method.
- New public API comes with tests. The coverage target is 90% of lines and 100% of the public API.

## Development

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
dotnet build
dotnet test
```

Tests run on Microsoft.Testing.Platform, selected in `global.json`.

Code style is defined in [.editorconfig](.editorconfig). Run `dotnet format` before committing.

## Pull requests

Keep each pull request to one change. Reference the issue and the PRD requirement IDs it covers (`F9`, `N3`). Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/).

## Conduct

Participation is governed by the [Code of Conduct](CODE_OF_CONDUCT.md).
