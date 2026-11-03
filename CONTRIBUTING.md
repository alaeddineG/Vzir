# Contributing to Wkeel

Wkeel's requirements and decisions are recorded in the [PRD](docs/PRD.md), so scrutiny of the design is as useful as code.

## Ways to help

- **Challenge the design.** Open a [design proposal](https://github.com/alaeddineG/Wkeel/issues/new?template=design_proposal.yml) if a requirement is wrong, contradictory or missing.
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

`README.md` is also the package page on nuget.org, which cannot resolve relative links, so keep its links absolute.

Benchmarks live in `benchmarks/Wkeel.Benchmarks`. BenchmarkDotNet launches the `dotnet` on your `PATH`, so check that its summary reports the architecture you meant to measure. CI builds them but does not run them.

## Pull requests

Keep each pull request to one change. Reference the issue and the PRD requirement IDs it covers (`F9`, `N3`). Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/).

Breaking changes need a new major version, as the [README](README.md#versioning) defines them. `dotnet pack` compares the public API with the baseline set by `PackageValidationBaselineVersion` and fails on a break.

## Releasing

Maintainers publish from a version tag, through [`release.yml`](.github/workflows/release.yml) and NuGet Trusted Publishing, with no stored API key.

1. Set `VersionPrefix` in `Directory.Build.props` and move the changes into a new section of `CHANGELOG.md`.
2. Merge that to `main` and wait for CI.
3. Tag the merge commit and push the tag, such as `git tag v1.1.0` and `git push origin v1.1.0`. The workflow fails if the tag and the version disagree.
4. After a new major version, set `PackageValidationBaselineVersion` to it.

A published version can be unlisted but never deleted or replaced, so a mistake needs a new version.

## Conduct

Participation is governed by the [Code of Conduct](CODE_OF_CONDUCT.md).
