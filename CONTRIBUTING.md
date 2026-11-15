# Contributing to Sharp.Validator

Thanks for your interest in Sharp.Validator. Bug reports, feature requests, and pull requests are welcome. Everyone
who takes part follows the [code of conduct](#conduct). To report a security problem, follow
[SECURITY.md](SECURITY.md) instead of opening an issue.

## Before you start

- Open an issue before you start on a new feature or a change to public behavior, so the design is agreed first.
- A breaking change to the public API is discussed in an issue before any code is written, and it ships only in the
  next package major version, independently of .NET releases.
- Small fixes — a typo, a docs correction, an obvious bug with a test that shows it — can go straight to a pull
  request.

## Prerequisites

- A stable .NET 10.0 SDK meeting the minimum in `global.json`. CI tests the minimum and latest versions.
- For the Native AOT canary, the
  [Native AOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites) of your
  platform. On Linux, CI installs `clang` and `zlib1g-dev`.

## GitHub Actions

Every pull request runs `Verify`, which builds with warnings as errors and runs the tests, the coverage gate, the
package checks, and the Native AOT canary, plus the benchmark checks, once more with the source generator's accessors,
and CodeQL's `Analyze`. Both must pass before a pull request is merged. The commands under [Build and
test](#build-and-test) reproduce `Verify` locally.

## Build and test

```bash
# The build as CI runs it
dotnet build Sharp.Validator.slnx -c Release -warnaserror --no-incremental

# The fastest useful test run: the core tests
dotnet test --project test/Sharp.Validator.Tests/Sharp.Validator.Tests.csproj -c Release --no-build

# The full test run with coverage, then the 100% line and branch gate
rm -rf test/artifacts/coverage   # the verifier merges every report it finds
dotnet test --solution Sharp.Validator.slnx -c Release --no-build --coverlet
python3 eng/coverage/verify-coverage.py test/artifacts/coverage

# The benchmark smoke checks and cross-library error-count parity, without timing
dotnet run --project test/Sharp.Validator.Benchmarks -c Release -- --check
dotnet run --project test/Sharp.Validator.ComparisonBenchmarks -c Release -- --check

# The internal checks again, on a build with the source generator's accessors
dotnet run --project test/Sharp.Validator.Benchmarks -c Release -p:SharpBenchmarkAccessors=Generated -- --check

# The Native AOT canary: replace <rid> with your runtime identifier, such as osx-arm64
dotnet publish test/Sharp.Validator.AotTests/Sharp.Validator.AotTests.csproj -c Release -r <rid> -warnaserror
./test/Sharp.Validator.AotTests/bin/Release/net10.0/<rid>/publish/Sharp.Validator.AotTests
```

**Warnings as errors.** CI builds a clean checkout with `-warnaserror` on Linux, Windows, and macOS, so a missing XML
doc comment or any analyzer warning fails it. Keep `--no-incremental` locally: `-warnaserror` still writes the
assemblies, so a second incremental build skips the compiler and reports success for code that fails in CI.

**Tests and coverage.** CI runs every test project on all three operating systems, because culture- and
regex-sensitive rules must behave the same everywhere. `test/testconfig.json` includes both runtime libraries and
the bundled source generator and code fixes. CI merges the reports of every operating system by repository-relative
path, so a line counts as covered when any report covers it, requires 100% line and branch coverage, and keeps JSON
and Cobertura reports as build artifacts, including when verification fails.

**Benchmark checks.** CI runs both projects with `--check`, without timing. `Sharp.Validator.Benchmarks` calls every
internal scenario's benchmark methods, for every size a scenario declares, and checks returned results where
available. `Sharp.Validator.ComparisonBenchmarks` runs each comparison scenario's setup and benchmark methods the
same way and checks that Sharp.Validator, FluentValidation, and DataAnnotations report the same number of errors for
every input. CI then builds the internal project with
`-p:SharpBenchmarkAccessors=Generated` and runs its check again, which fails unless that build registered the source
generator's accessors.

**Native AOT canary.** `Sharp.Validator.AotTests` is a console app built with the source generator. Publishing it with
Native AOT and `-warnaserror` fails on any trim or AOT warning, and the binary checks the generated registrations and
accessors, the compiled fallbacks, and that scanning an assembly built without the generator throws. CI publishes it
for `linux-x64`; replace `<rid>` with your runtime identifier, such as `osx-arm64` or `win-x64`.

**Benchmarks.** `dotnet run --project test/Sharp.Validator.Benchmarks -c Release` runs the Sharp-only suite;
`-- --filter '*ParallelThroughput*'` narrows it. The scenarios marked `[AccessorJobs]`, the element walks, chain count,
shared conditions, and element includes, run as two jobs in one table. Fallback reads members through compiled
expressions, as the project builds by default; Generated rebuilds it with the source generator, whose accessors
package users get. Fallback is the baseline, so Ratio compares the generated path with it. Under the JIT, a generated
accessor tiers up, so a delegate to it calls through its stable entry point, while a compiled expression's delegate
calls its code directly. Where one call site reads many members, as the per-chain read in ChainCount does, Generated
can therefore come out a few percent slower; where one member dominates, as in an element walk, the JIT inlines the
generated accessor and Generated is faster. The other scenarios, and
the comparison suite, time the fallback only. Use `test/Sharp.Validator.ComparisonBenchmarks` for the flat, collection,
nested, and async comparisons with FluentValidation and DataAnnotations (flat only). The comparison project links
`Setup.cs` and the combined model and validator declarations in `Fixtures/Models.cs` and `Fixtures/Validators.cs` from
`Sharp.Validator.Benchmarks` as source, so both suites use the same definitions without referencing an executable. `COMPARISON_BENCHMARKS` places
those linked sources in the
comparison project's namespace; each suite uses its project name as its root namespace. DataAnnotations-specific
inputs stay in the comparison project; its `Fixtures/` folder holds the FluentValidation rules and the DataAnnotations
models, each in the namespace of its library. Only that project references FluentValidation. Results go to
`test/artifacts/benchmark/<Project>/`.

**Native AOT benchmarks.** `-- --native-aot` runs the selected Sharp-only scenarios on two jobs in place of their own:
Jit, the baseline, and NativeAot. Both build with the source generator, as a Native AOT app always does, so Ratio shows
what Native AOT changes for the same code, including costs that only Native AOT pays. The run needs the platform's
Native AOT toolchain, as the canary does; on Linux without clang, set `CppCompilerAndLinker=gcc` in the environment. The
Native AOT build takes a minute or more, so narrow the run with `--filter`, as in
`-- --native-aot --filter '*RuleHotPath*'`.

Test tooling is shared across the Sharp repositories: xUnit v3, Microsoft.Testing.Platform with its TRX extension, and
coverlet.MTP, at the versions in `Directory.Packages.props`. `test/Directory.Build.props` supplies the references and
test defaults. `global.json` requires .NET SDK 10.0.403 or later in the 10.0 feature bands with `latestFeature`, stable
SDKs only, and the native MTP runner. The minimum includes the .NET 10.0.14 Native AOT security fixes. CI tests the
minimum and latest 10.0 SDKs, pins each test job to its installed SDK, and uses the latest stable 10.0 SDK for other
jobs. `test/testconfig.json` is merged into each application's configuration, alongside its results-directory default.

Run the engineering regression tests from the repository root with
`python3 -m unittest discover -s eng/coverage -p '*_test.py'`. Python entry points use kebab-case; imported modules use
snake_case, tests use the `*_test.py` pattern, and imports are ordinary.

Each test project writes to `test/artifacts/coverage/<Project>/`. A build clears that project's previous results.
Before collecting coverage with `--no-build`, remove `test/artifacts/coverage` and run the full solution. The verifier
rejects duplicate or missing project reports and coverage older than the latest TRX test run, including after CI
artifact download. This prevents an older successful run from covering a newer failed or filtered run. Separate
CI matrix artifacts can still merge. Generated output and automatic properties are excluded; the shipped runtime
and tooling assemblies are included. Only reviewed NativeAOT guards are excluded and verified by native canaries.

### Packaging

For ordinary local packaging, run:

```bash
dotnet pack Sharp.Validator.slnx -c Release -o artifacts/packages
```

To reproduce CI package verification, use the commands below. The verifier requires `.snupkg` files, so this pack
command enables symbols explicitly. Removing the previous output prevents stale packages from satisfying the checks.

```bash
# Reproduce CI package verification with a fresh output directory.
dotnet build Sharp.Validator.slnx -c Release -warnaserror --no-incremental
rm -rf artifacts/packages
dotnet pack Sharp.Validator.slnx -c Release --no-build \
  --include-symbols -p:SymbolPackageFormat=snupkg \
  -o artifacts/packages
.github/scripts/verify-packages.sh artifacts/packages
```

`verify-packages.sh` fails when a package lacks a file it promises: the library and its XML docs, both
analyzer DLLs in `analyzers/dotnet/cs`, `buildTransitive/Sharp.Validator.props`, the packed `README.md`, or a symbol
package. A package without the generator still works under the JIT and fails only when a consumer publishes with
Native AOT, so CI packs and verifies on every pull request.

## Repository layout

| Path | What it is |
| --- | --- |
| `src/` | The shipped projects; [ARCHITECTURE.md](ARCHITECTURE.md) maps them |
| `src/Sharp.Validator/Sharp.Validator.props` | Exposes `PublishAot` to the analyzers through `buildTransitive/` |
| `eng/coverage/` | Coverage policy implementation and the coverage verifier |
| `eng/coverage/modules/coverage_policy_test.py` | Regression tests of the coverage tooling |
| `test/testconfig.json` | Shared MTP configuration template |
| `test/coverage-exclusions.json` | Reviewed coverage exclusions |
| `test/Sharp.Validator.Tests` | Tests of the validation engine, the built-in rules, and registration |
| `test/Sharp.Validator.Result.Tests` | Tests of the result types on their own, including `ValidationError`'s JSON shape |
| `test/Sharp.Validator.SourceGenerator.Tests` | Generator and analyzer tests, run through a Roslyn compilation |
| `test/Sharp.Validator.CodeFixes.Tests` | Code-fix tests, run in a Roslyn workspace |
| `test/Sharp.Validator.AotTests` | The Native AOT canary, built with the source generator |
| `test/Sharp.Validator.Benchmarks` | Sharp-only performance and allocation scenarios |
| `test/Sharp.Validator.ComparisonBenchmarks` | Comparisons with FluentValidation and DataAnnotations |
| `test/Sharp.Validator.Fixtures` | The Roslyn test helpers that the analyzer, generator, and code fix tests share: the references of an in-memory compilation, the build properties an analyzer reads, and a stub of a Roslyn interface |
| `test/Sharp.Validator.LibraryFixtures` | A library built with the generator, which the unit tests load by name |
| `test/Sharp.Validator.NoGeneratorFixtures` | A library built without the generator, for the canary's negative check |
| `test/Sharp.Validator.NoValidatorFixtures` | A library built with the generator that declares no validator, which the canary scans |
| `doc/diagnostics/` | One page per analyzer diagnostic |

## Test organization

These conventions are shared by the Sharp .NET repositories.

On disk, every test project sits directly in `test/`. The solution groups them into folders by kind:

| Solution folder | Projects |
| --- | --- |
| `Tests/Packages/` | `{Project}.Tests`: one for each project under `src/`, named after it |
| `Tests/Benchmarks/` | `{Product}.Benchmarks`, plus `{Product}.{Word}Benchmarks` for a scenario that needs its own compilation |
| `Tests/Fixtures/` | `{Product}.Fixtures`, plus `{Product}.{Word}Fixtures` for one that differs from a plain library: the class libraries that tests or benchmarks reference |
| `Tests/` | `{Product}.{Word}Tests`: a test project whose execution conditions differ, such as the Native AOT canary `{Product}.AotTests` |

- **A test belongs to the project whose code it tests.** A package it only uses as a harness or a tool doesn't count.
  A test whose subject spans packages lives with the package that references the others.
- **The suffix decides how a project builds.** `test/Directory.Build.props` makes `*Tests` test executables, gives
  `*Benchmarks` an artifacts path, and marks `*Fixtures` as non-test projects, so a fixture's name never ends in
  `Tests`. A dot before `Tests` always follows the name of a project under `src/`.
- **Inside a test project, folders group tests by subject** — the code surface under test — and the namespace
  follows the folder path. File names name the subject (`{Subject}Tests.cs`). A regression test lives with its
  subject, with a comment that says what regressed and why the pinned behavior is correct. A project with fewer than
  about six test files stays flat. A file is named for its subject, not for the kind of test or the review that asked
  for it. Test classes are `sealed`, and `Xunit` comes from a `<Using Include="Xunit" />` item in the project file.
- **A test method is named `Subject_Condition_Expected`**, or `Subject_Expected` when there is no condition. Each part
  is PascalCase with no underscore inside it, and the name says what the test pins:
  `Publish_HandlerThrows_StopsAtFirstFailure`. A test checks one behavior, so a name that needs `And` for two unrelated
  outcomes is two tests; inputs that vary one behavior go in a `[Theory]`.

Do not exclude code from coverage to pass the gate. The only hand-written exclusions are the reviewed NativeAOT
platform guards in `test/coverage-exclusions.json`; a code change to an excluded method requires an explicit policy
review. The allowlist hashes code, so comments and layout don't count.
`python3 eng/coverage/verify-coverage.py --check-policy` checks the allowlist and measurement filters before CI builds.

In this repository:

- `Sharp.Validator.Tests` doesn't reference the source generator, so it runs the fallbacks: the reflection scan in
  `AddValidators` and the compiled accessors. The generated path is covered by `Sharp.Validator.AotTests`, and loading
  a generated registrar by assembly name by `Sharp.Validator.LibraryFixtures`.
- A test that changes `ValidatorBuilderOptions.Global` runs in the `[Collection(GlobalOptions.Name)]` collection, which
  disables parallelization, and restores the global state in a `finally` block. Without the collection attribute, it
  fails intermittently, depending on test order. It assigns new options to `Global` rather than editing the current
  ones, which are read-only once any validator has frozen reading them.
- A test that changes `CultureInfo.CurrentCulture` follows the same collection-and-restore pattern.
- The code-fix tests run the analyzer only to get a diagnostic to fix, so they belong to
  `Sharp.Validator.CodeFixes.Tests`. Results that `Validator<T>` builds are tested in `Sharp.Validator.Tests`, and the
  result types on their own in `Sharp.Validator.Result.Tests`.
- `Sharp.Validator.Tests` groups its tests in `Accessors/`, `Messages/`, `Pipeline/`, `Registration/`, `Results/`, and
  `Validators/`. A helper that more than one test project uses goes in `Sharp.Validator.Fixtures`, which declares no
  validators and does not apply the generator. A helper that one test project alone uses stays in that project's own
  `Fixtures/` folder (its namespace ends in `.Fixtures`, apart from the shared library).
- `Sharp.Validator.SourceGenerator.Tests` groups its tests in `Generator/` and `Diagnostics/`, and
  `Sharp.Validator.Result.Tests` in `Results/` and `Errors/`.

## Coding style

`.editorconfig` is authoritative. Every public member has a one-line `<summary>` XML doc comment — a missing one fails
the build. Comments explain *why* — constraints and invariants the code can't express — not *what* the next line
does. Code, comments, and documentation are written in English.

The formatter doesn't wrap XML documentation text, so wrap it yourself. Keep a line within 150 columns after its
`///`, break between phrases, and keep the punctuation that follows a tag on the tag's line. A `<summary>` that fits
on one line stays on one line: `/// <summary>Creates the validator.</summary>`. When an edit makes a line too long,
re-wrap the paragraph rather than push the overflow onto a short line of its own.

A public method gets a new setting as a new overload, never as a new parameter, which would break code compiled
against the old signature. Parameter names are public too: callers write them as named arguments.

A member's leading lines go in this order: its XML doc comment, its attributes, a `//` comment about the member,
then the member. Among the attributes, a test's `[Fact]` or `[Theory]` and its data come first and `[SuppressMessage]`
comes last.

Fix a finding rather than suppress it. When the code has to stay as it is, suppress at the narrowest scope and say
why:

- A ReSharper inspection takes `// ReSharper disable once <Id> — <why>` before the element, or a why comment and
  `// ReSharper disable`/`restore` around several. Don't use `[SuppressMessage("ReSharper", …)]`.
- A compiler or Roslyn analyzer diagnostic takes a why comment and `#pragma warning disable`/`restore`, or
  `[SuppressMessage]` with a `Justification` when the reason covers the whole member. A trimming or AOT warning takes
  `[UnconditionalSuppressMessage]`, the only form the trimmer sees.
- A path-wide setting goes in `.editorconfig` only when its reason holds for every file it matches, with a comment
  that says so. Shipped code never turns an inspection off for a whole folder. `<NoWarn>`, with a comment, is for
  project-wide policies and for NuGet or MSBuild warnings, which `.editorconfig` can't reach.

In this repository:

- A rule method takes `configure` last, as its only optional parameter, so a message can be changed without naming
  the argument.

## Diagnostics

The Sharp analyzers share one ID scheme: a three-letter prefix per product — `SVL` Validator, `SMD` Mediator, `SMS`
Messenger — followed by a three-digit number, with each feature in its own band of ten. Users pin these IDs in
`#pragma` directives and `.editorconfig`, so they are a public contract:

- A new diagnostic takes the next free ID in its feature's band. When a band is full, the feature continues in the
  product's next unassigned band, and that band is added to the table below.
- Record it in `src/Sharp.Validator.SourceGenerator/Properties/AnalyzerReleases.Unshipped.md`.
- Give it a page at `doc/diagnostics/<ID>.md` and a row in `doc/diagnostics/README.md`.
- Never renumber or reuse an ID after it ships.

| Band | Feature |
| --- | --- |
| `SVL001`–`SVL009` | Source generation and registration |
| `SVL010`–`SVL019` | Validation-rule analyzers and code fixes |

## Documentation

- `README.md` is the user manual, and every package uses it as its NuGet page. Links in it to other files are absolute
  URLs, because nuget.org cannot resolve relative ones, and it has no images.
- `doc/diagnostics/` has one page per analyzer diagnostic, listed in `doc/diagnostics/README.md`.
- A change to public behavior updates the README section that describes it.
- [ARCHITECTURE.md](ARCHITECTURE.md) maps the code. A change that moves responsibilities between projects or changes
  an invariant updates it.
- [AGENTS.md](AGENTS.md) gives AI coding agents the commands and rules from this file in short form and must match
  it: a change to them here updates it in the same pull request.
- There is no changelog file. What changed in each version is in the
  [release notes](https://github.com/sharp-space/validator/releases), which GitHub generates from merged pull requests.

## Pull requests

- Keep a pull request to one change, and link the issue it resolves.
- Write the title as a release note, because it appears as written in the release notes — for example
  "Add `DateOnly` overloads to `InPast`, `InFuture`, and `IsToday`" or "Fix range messages that ignored the naming
  policy".
- Pull requests are squash-merged, so the title becomes the one commit on `main`. Commit messages on a branch take the
  same form: an imperative sentence of at most 72 characters, capitalized, with no type prefix such as `feat:` and no
  closing period. A body, after a blank line and wrapped at 72 columns, says why the change is made.
- Every behavior change comes with a test.
- The build with warnings as errors and the full test suite pass locally before you ask for review.
- Fill in the checklist in the pull request template.
- The maintainers review every pull request. Before merging, they label a breaking change `breaking-change`, which
  gives it its own section in the release notes.

## Releases

Maintainers publish releases. Publishing a GitHub release starts the Release workflow, which builds, tests, and packs
the packages and pushes them to nuget.org; nothing is published by hand. The
[release notes](https://github.com/sharp-space/validator/releases) list what changed in each version.

## Conduct

This project follows the [Contributor Covenant, version 2.1](https://www.contributor-covenant.org/version/2/1/code_of_conduct/)
in every project space — issues, pull requests, discussions, and reviews. Be respectful, assume good faith, and keep
discussions about the work. Harassment and personal attacks are not tolerated.

To report unacceptable behavior, choose **Report content**, then **Report to repository admins** on the comment,
issue, or pull request in question. Only the maintainers see the report. They handle it confidentially and respond as
the Contributor Covenant's
[enforcement guidelines](https://www.contributor-covenant.org/version/2/1/code_of_conduct/#enforcement-guidelines)
describe.
