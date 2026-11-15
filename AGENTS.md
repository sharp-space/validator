# AGENTS.md

Instructions for AI coding agents working in this repository. [CONTRIBUTING.md](CONTRIBUTING.md) holds the full
conventions and [ARCHITECTURE.md](ARCHITECTURE.md) maps the code. This file repeats CONTRIBUTING.md's commands and rules
in short form and must match it: a change to one changes the other in the same pull request. If you find them
disagreeing, CONTRIBUTING.md is right; fix this file.

## Project

Sharp.Validator is a validator for .NET built for speed and Native AOT, with first-class fluent rules, async validation,
and DI integration. It targets .NET 10 and stays Native AOT- and trim-safe: the source generator emits the DI
registrations and property accessors that would otherwise need reflection, and the libraries build with
`IsAotCompatible`, so any reflection the trim analyzer can't prove safe fails the build.

## Commands

```bash
# The build as CI runs it; --no-incremental also reports the warnings of projects that are already up to date
dotnet build Sharp.Validator.slnx -c Release -warnaserror --no-incremental

# The fastest useful test run: the core tests
dotnet test --project test/Sharp.Validator.Tests/Sharp.Validator.Tests.csproj -c Release --no-build

# The full test run with coverage, then the 100% line and branch gate
rm -rf test/artifacts/coverage
dotnet test --solution Sharp.Validator.slnx -c Release --no-build --coverlet
python3 eng/coverage/verify-coverage.py test/artifacts/coverage

# The benchmark smoke checks and cross-library error-count parity, without timing
dotnet run --project test/Sharp.Validator.Benchmarks -c Release -- --check
dotnet run --project test/Sharp.Validator.ComparisonBenchmarks -c Release -- --check

# Reproduce CI package verification with a fresh output directory
dotnet build Sharp.Validator.slnx -c Release -warnaserror --no-incremental
rm -rf artifacts/packages
dotnet pack Sharp.Validator.slnx -c Release --no-build \
  --include-symbols -p:SymbolPackageFormat=snupkg \
  -o artifacts/packages
.github/scripts/verify-packages.sh artifacts/packages

# The internal benchmark checks again, on a build with the source generator's accessors
dotnet run --project test/Sharp.Validator.Benchmarks -c Release -p:SharpBenchmarkAccessors=Generated -- --check

# The Native AOT canary: needs the platform's Native AOT toolchain; replace <rid> with yours, such as osx-arm64
dotnet publish test/Sharp.Validator.AotTests/Sharp.Validator.AotTests.csproj -c Release -r <rid> -warnaserror
./test/Sharp.Validator.AotTests/bin/Release/net10.0/<rid>/publish/Sharp.Validator.AotTests
```

For ordinary local packaging without CI verification, see [CONTRIBUTING.md](CONTRIBUTING.md#packaging).

Run the narrowest test project that covers the change first, and the full suite before you finish.

Do not exclude code from coverage to pass the gate. The only hand-written exclusions are the reviewed NativeAOT
platform guards in `test/coverage-exclusions.json`; a code change to an excluded method requires an explicit policy
review. The allowlist hashes code, so comments and layout don't count.
`python3 eng/coverage/verify-coverage.py --check-policy` checks the allowlist and measurement filters before CI builds.

## Rules

- Every behavior change comes with a test. A bug fix comes with a regression test that fails without the fix and
  passes with it.
- Put a test where CONTRIBUTING.md › Test organization says: in the project named after the code under test, in a
  folder named after its subject.
- Every public member has a one-line `<summary>` XML doc comment; the build fails without one.
- Every shipped assembly keeps 100% line and branch coverage. A new branch needs a test, or the coverage gate fails.
- Preserve public API compatibility unless the task explicitly requests a breaking change. Breaking changes require
  a package major version increase, independently of .NET releases. Raising the minimum supported SDK, compiler, or
  runtime version also requires a package major version increase.
- Do not remove or change a shipped public member of `Sharp.Validator.Bindings`, old overloads included: libraries
  built against an earlier version call them from generated code. Add a new member instead.
- A new analyzer diagnostic follows CONTRIBUTING.md › Diagnostics: an ID in its feature's band, an entry in
  `AnalyzerReleases.Unshipped.md`, a page in `doc/diagnostics/`, and a row in `doc/diagnostics/README.md`. Entries
  move to `AnalyzerReleases.Shipped.md` only at release.
- `README.md` is the user manual and every package's NuGet page. When behavior changes, update the section that
  describes it. Links in it to other files are absolute URLs.
- Update ARCHITECTURE.md when a change moves responsibilities between projects or changes an invariant.
- Write code, comments, and documentation in English. Comments explain why, not what.
- Do not add files that record work in progress — plans, audits, reports, TODO lists — to the repository. Keep
  working notes outside it. There is no changelog either: the release notes come from pull request titles.
- Do not add a `CLAUDE.md`: Claude Code reads this file only when there is none. If Claude-specific instructions are
  ever needed, start the `CLAUDE.md` with the line `@AGENTS.md`.

## Pull requests

- The title appears as written in the release notes, so write it as one.
- Write commit messages in the same form: an imperative sentence of at most 72 characters, with no type prefix such
  as `feat:` and no closing period. Put the reason for the change in the body.
- Fill in the checklist in `.github/pull_request_template.md`.

## In this repository

- A test that changes `ValidatorBuilderOptions.Global` or `CultureInfo.CurrentCulture` runs in the
  `[Collection(GlobalOptions.Name)]` collection and restores the previous value in a `finally` block. It assigns new
  options to `Global` rather than editing the current ones, which are read-only once a validator has frozen reading
  them.
- `Sharp.Validator.Tests` must not reference the source generator: it is the suite that runs the reflection and
  compiled fallbacks. Test generated code in `Sharp.Validator.SourceGenerator.Tests`, and the generated path at run
  time in `Sharp.Validator.AotTests`.
- Keep `Microsoft.CodeAnalysis.CSharp.Workspaces` out of `Sharp.Validator.SourceGenerator`. The compiler loads that
  assembly; only `Sharp.Validator.CodeFixes` may reference Workspaces.
- A validator freezes at its first validation or `Include`. Code on the validation path reads the snapshots taken at
  freeze (`FrozenOptions`, `FrozenPropertyOptions`), never the options objects, which callers may still hold.
