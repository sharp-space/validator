# Architecture

This document describes the high-level architecture of Sharp.Validator, for anyone about to change the code.
[README.md](README.md) describes how to use the library. Files and types are named rather than linked, so use symbol
search to find them.

## Overview

A validator is a class that declares rules for a model in its constructor: `Builder.Property(x => x.Email).NotEmpty()`
adds a rule chain that reads one member of the model and runs its rules against the value. Rules are plain code;
nothing is configured by attributes or discovered while validating. The first validation freezes the chains into a
plan, and every call after that walks the plan against a fresh context.

Two things would otherwise need reflection at run time: reading the member a lambda selects, and finding an assembly's
validators for dependency injection. A source generator that runs in the user's build does both ahead of time. It emits
a precompiled accessor for each selector it can name and a registrar that lists the assembly's validators, and the
emitted code hands both to the runtime from a module initializer. The runtime looks for them first and falls back to
`Expression.Compile()` and an assembly scan when they are missing, so the library works without the generator under
the JIT and needs nothing the trimmer removes under Native AOT.

## Code map

The four projects under `src/`, in dependency order.

### `Sharp.Validator.Result`

The result types, in a package of their own so that a layer which only consumes results references nothing else:
`ValidationResult`, `ValidationError`, `ValidationErrorSeverity`, and `ValidationException`. It depends on the base
class library only.

### `Sharp.Validator`

The runtime: the validator base class, the rule chains, the built-in rules, and DI registration. It depends on
`Sharp.Validator.Result` and `Microsoft.Extensions.DependencyInjection.Abstractions`, and its package carries the two
analyzer projects below in its `analyzers/dotnet/cs` folder.

- **Declaring rules**: `Validator<TModel>` owns a `ValidatorBuilder<TModel>`, whose `Property` and `Elements` start
  chains and whose block `Where` gates several of them. A chain is a `PropertyValidatorBuilder<TModel, TProperty>`; a
  collection chain is an `ElementValidatorBuilder<TModel, TElement>`, and its `Property` starts an
  `ElementPropertyValidatorBuilder<TModel, TElement, TProperty>`. The rule methods (`NotEmpty`, `Email`,
  `GreaterThan`, `Include`, and the rest) are extension members in `ValidationExtensions`. Options are in
  `ValidatorBuilderOptions` (per validator, plus `Global`), `PropertyValidatorBuilderOptions` (per chain), and
  `PropertyValidatorOptions` (per rule).
- **Rules**: `PropertyValidator<TModel, TProperty>` and `AsyncPropertyValidator<TModel, TProperty>` are the bases of
  every rule. They implement `IPropertyValidator<TModel, in TProperty>` and `IAsyncPropertyValidator<TModel, in
  TProperty>`, which `Add` takes so that a chain also takes a rule for a wider type. Their members are internal and they
  are hidden from completion, so other types can't implement them and callers see nothing to call. A chain stores and
  runs its rules as the base classes, never through the variant interfaces: a rule that differs from the chain in
  nullable annotations alone is stored as it is, and one for a wider type runs behind `WiderRuleValidator.cs`'s
  adapters. A chain has its property's declared type, and the string rules extend chains of `string` and `string?` alike
  through an `Add` of their own, which takes a `string?` rule where a chain's type is generic. The built-in rules are in
  `Validators/`, one file per family (`ComparisonValidator.cs`, `RangeValidator.cs`, `RegexValidator.cs`,
  `DateValidator.cs`, and so on). `IncludeValidator.cs` holds the internal adapters that run a child validator.
- **Running rules**: `ValidationEvaluator.cs` holds the internal evaluators that read a chain's value and run its
  rules: `PropertyValidationEvaluator`, `ElementsValidationEvaluator`, `ElementPropertyValidationEvaluator`, and their
  async counterparts. `ConditionGate<TModel>` evaluates `Where` conditions and keeps their verdicts in
  `ConditionResults`. `ValidationContext<TModel>` holds the state of one call: the model, the errors, the message state
  of the rule being run, `Parameters`, and the partial-validation selection. What most calls never use, the parameters,
  the selection, and the async chains whose sync rules failed, sits in a side object the context creates on first use,
  and an `Include` child reads the parameters through the root context of its run.
- **Names**: `PropertyNameComposer` builds every reported property name from the path, the naming policy, the prefix
  and suffix wrappers, and collection indices.
- **Accessors**: `AccessorCache.cs` holds the caches that turn a selector into a delegate (`PropertyAccessorCache`,
  `NullSafeAccessorCache` for nested paths, `ElementsAccessorCache` for collections) and `CrossPropertyTarget`, which
  reads the target of a cross-property rule. `InternalExtensions` reads paths and members off selector expressions.
- **Registration**: `ValidatorServiceCollectionExtensions` holds `AddValidator` and `AddValidators`.
- **Entry points for generated code**: `AccessorRegistry` and `ValidatorRegistry`, in `Bindings/` and the
  `Sharp.Validator.Bindings` namespace (see [Generated code](#generated-code)).
- **Build integration**: `src/Sharp.Validator/Sharp.Validator.props`, packed into the package's `buildTransitive` folder, makes
  `PublishAot` visible to the analyzers in every project that references the package, directly or through another
  package.

### `Sharp.Validator.SourceGenerator`

The incremental generator and diagnostic analyzers target `netstandard2.0`, depend only on Roslyn, and ship together
inside the `Sharp.Validator` package's analyzer slot. Runtime types are recognized by name.

`Generator.cs` owns two independent emission pipelines. `Generation/Discovery.Validators.cs` finds validator types
using the same accessibility checks as `Analysis/ValidatorAnalysis.cs`; `Generation/Discovery.Accessors.cs` finds
selectors. `Generation/Emitter*.cs` writes their registrations and accessors. The string-based records in
`Generation/Model.cs` and content equality in `EquatableArray<T>` keep unrelated edits out of source emission.

`Analysis/` holds validator, regex-rule, and cross-property analysis. The three components in `Analyzers/` report
SVL001 (`InaccessibleValidatorAnalyzer`), SVL010 (`RegexRuleAnalyzer`), and SVL011 (`CrossPropertyTargetAnalyzer`).
Generators report no user diagnostics; unsupported inputs remain excluded independently of diagnostic suppression.
`Diagnostics/DiagnosticDescriptors.cs` owns the descriptors; `Properties/AnalyzerReleases.*.md` tracks their IDs.

### `Sharp.Validator.CodeFixes`

`GeneratedRegexCodeFix`, the SVL010 code fix that turns a literal pattern into a `[GeneratedRegex]` method. It is an assembly of
its own because it needs `Microsoft.CodeAnalysis.CSharp.Workspaces`, which IDEs load and the compiler doesn't; the
compiler loads the generator assembly, which therefore references the compiler alone.

### Tests

`test/` holds a test project for each project under `src/`, named after it, plus the Native AOT canary
`Sharp.Validator.AotTests`, the benchmarks, and two fixture libraries, one built with the generator and one without.
`Sharp.Validator.Benchmarks` owns the Sharp-only performance scenarios. `Sharp.Validator.ComparisonBenchmarks` owns
the FluentValidation and DataAnnotations comparisons and links `Setup.cs` and the model and validator files of `Fixtures/` as source from the
internal suite. Each suite keeps its model and validator declarations in a `Fixtures/` folder; the internal files
hold the common models and Sharp validators, and the comparison files hold the external libraries, one namespace each.
Each benchmark executable has its own `--check` entry point and artifacts directory.
`Sharp.Validator.Tests` doesn't reference the generator, so it runs the fallbacks; the canary runs the generated path
under Native AOT. The benchmarks build without the generator as well, except the internal suite's Generated accessor job
and its `--native-aot` jobs, which rebuild it with `-p:SharpBenchmarkAccessors=Generated`. The rules are in
CONTRIBUTING.md › [Test organization](CONTRIBUTING.md#test-organization).

## Generated code

The generator looks for two things in the user's compilation:

- **Validator types**: every class that is neither abstract nor generic and implements `Sharp.Validator.IValidator<T>`,
  once for each closed interface. It emits `SharpValidator.Registrations.g.cs`: a `GeneratedValidatorRegistrar` whose
  module initializer passes the assembly and a registration callback to `ValidatorRegistry.AddRegistrar`. The callback
  calls `ValidatorRegistry.Register` once for each validator type, with every interface the type implements.
- **Selectors**: the single-parameter lambda passed to `Property`, `Elements`, and the
  cross-property rules (the four comparisons, `Equal`, `NotEqual`, and both bounds of `InclusiveBetween` and
  `ExclusiveBetween`), when it is a pure member chain over types generated code can name. It emits
  `SharpValidator.Accessors.g.cs`: a `GeneratedAccessorRegistrar` whose module initializer seeds a delegate for each
  selector through `AccessorRegistry.RegisterProperty`, `RegisterNullSafe` for a nested path, or `RegisterElements` for
  a collection, keyed by the dotted member path.

Both registrars are `file` classes in the `Sharp.Validator.Generated` namespace, so assemblies that see each other's
internals don't collide, and both carry `[GeneratedCode]`, `[ExcludeFromCodeCoverage]`, and `[DebuggerNonUserCode]`.

At run time, `AddValidators(assembly)` first runs the assembly's module constructor, which an assembly loaded by name
may not have run yet, and then asks `ValidatorRegistry` for its registrar. With one, it registers exactly what the
generator listed. Without one, it scans the assembly with reflection under the JIT, and under Native AOT it throws
`InvalidOperationException` instead of scanning types the trimmer may have removed. An accessor cache looks up the
generated delegate by path first and otherwise compiles the selector with `Expression.Compile()`, which runs
interpreted, and more slowly, under Native AOT. A missing accessor therefore costs speed, never correctness.

## Invariants

- A validator's definition doesn't change after it freezes. The first `Validate` or `ValidateAsync`, or the first
  `Include` of the validator, calls `ValidatorBuilder.Freeze`, which snapshots every option (the validator's own, the
  `AddValidators` scan defaults, and `ValidatorBuilderOptions.Global`) and builds the sync and async plans. After that,
  `Configure`, new rules, and new conditions throw.
- An options object is read-only once the library has read it, so a change that would go unseen throws: one a
  `configure` callback receives when the callback returns, a custom rule's `Configure` options when a chain takes the
  rule, and `ValidatorBuilderOptions` when a validator freezes reading them. `Global` is one such object, and assigning
  a new one stays open, so a test swaps it rather than editing it in place.
- A validator holds no per-call state. Everything a call changes lives in its `ValidationContext<TModel>`, so one
  frozen instance is safe to share across threads, which is why DI registers validators as singletons by default.
- A context belongs to the call that received it, a rule's, a condition's, or a `Validate(model, configure)`
  configurator's, and README promises users that the library may reuse a context it created once that call returns. Only
  a context the caller created and passed to `Validate(context)` stays the caller's after its validation. An `Include`
  over a collection already reuses one child context for all its elements, so nothing in the library holds a context, or
  what it hands out, past the call either.
- A context runs one validation at a time. Every run, an `Include` child's too, marks its context from `BeginRun` until
  `ResetRunState` ends the run, after a pass or a throw, and a run that starts on a marked context throws before it
  touches the state of the one running.
- A `Where` condition runs at most once per validation. A condition that several chains or both phases reach gets a
  slot in `ConditionResults`, and every later reach reads the verdict from it.
- A collection that several chains walk is read once per validation. Its `Elements` chain gets a slot, unique within its
  validator, in the context's element sources; the first chain to walk the collection fills the slot, and the rest read
  it there. One that isn't an `IList<T>` is copied into an array as it is read, so a sequence that can be enumerated
  only once serves every chain, and every validation that reads it allocates that array. The end of every run clears the
  slots, because the context can go to another validator next, whose slots hold other collections.
- The all-valid path allocates no error list and no result: the context creates its list at the first error, and a
  call that records nothing returns the shared `ValidationResult.Empty`.
- A `ValidationResult` is immutable. A validator hands it the run's list without a copy, through `FromOwnedList`, which
  `InternalsVisibleTo` opens to `Sharp.Validator` only. The public constructor copies, and `Errors` is a
  `ValidationErrorCollection`, a read-only view over that list whose struct enumerator lets `foreach` allocate nothing.
- Generated code calls only public members of `Sharp.Validator.Bindings`, and those are never removed or
  changed, because libraries compiled against an earlier version call them. An overload that older generated code
  calls stays, such as the two-argument `ValidatorRegistry.AddRegistrar` and `ValidatorRegistry.CreateDescriptor`.
- A generated accessor's key is the dotted path the runtime computes from the selector (`PropertyPath`), built from the
  raw member names: a keyword is escaped in the emitted code (`m.@event`), never in the key. A mismatched key doesn't
  fail; it silently falls back to the compiled accessor, so tests check keys with `AccessorRegistry.HasProperty`,
  `HasNullSafe`, and `HasElements`. No selector that converts a `Nullable<T>`, or the `Value` it ends at, has a
  generated accessor: the path leaves that hop out, so such a selector shares its key with a plain read that treats a
  null otherwise. The generator doesn't register one, and the runtime doesn't look one up. The generator registers a
  target only as C# converts it implicitly and skips one with a cast, and the runtime relies on that: it reads a target
  written as two conversions into a nullable type from the registration for its path, so a generator that registered a
  cast target as its bare member would hand a narrowing cast an accessor that doesn't narrow.
- A chain's selector, and one passed to `IncludeProperty`, is a member chain rooted at the lambda parameter: properties
  or fields, `Nullable<T>.Value` hops included, under any number of conversions and at most one other unary.
  `EnsureChainSelector` rejects anything else where it is declared, so a chain's path names the value it reads, and no
  two chains share a path because one was cut short at an indexer, a call, or a cast. A cross-property target is only
  read, so it may also start at a captured or static value or compute one; a target that is a member chain in the same
  sense gets a name and a null-safe accessor.
- The static accessor caches hold only path-faithful selectors: a member chain rooted at the lambda parameter, with no
  captured value, under at most one top-level conversion or array `Length`, or under the two conversions C# writes to
  read a member whose type isn't nullable as another nullable type, such as an `int` member read as `long?`, both
  checked or both not. A selector's key is its dotted path with the body's node type and the type under its outer unary,
  since the path leaves out a `Nullable<T>.Value` hop: `(object) x.Age` and `(object) x.Age.Value` read a null age
  differently. A nullable member or its `Value` read through two conversions isn't faithful for that reason:
  `(long) x.Other` throws on a null `Other` where `x.Other.Value` skips, and both would key on `long`. Any other
  selector is compiled for its own rule and never enters a static cache, so two validators with different captured
  values never share a delegate.
- A generated accessor and a compiled one treat a member on the way to the value as absent only when it is a null
  reference or an empty `Nullable<T>`. Neither calls the `==` the member's type declares.
- The generated registrar and the reflection scan register the same validators: classes only, never structs, and
  never a type that generated code can't name (`private`, `protected`, or `file`-local, nested in such a type, or
  closing `IValidator<T>` over one). SVL001 reports exactly those types. The generator emits a registrar for every
  assembly it runs on, empty when no validator qualifies, so under NativeAOT a scan tells an assembly without
  validators from one built without the generator, which throws.
- Registrations live for the process. `ValidatorRegistry` and `AccessorRegistry` keep generated registrars and
  accessors in static state, so unloading a collectible `AssemblyLoadContext` that registered validators isn't
  supported.
- The generator never fails a build. It skips a selector or a type it doesn't recognize or can't reach, such as a
  property whose getter only the model's own code can call, and the runtime path stays correct. It takes a call for a
  rule only when Sharp.Validator declares the method, so an app's extension with a rule's name is left alone, and the
  analyzers apply the same test. Generated code names what the app chose and the app can't edit it, so it raises nothing
  the app silenced in its own code: the generated types are marked `[Obsolete]`, which turns off every obsolete
  diagnostic inside them, and each file disables the `[Experimental]` IDs its references would raise. It reports no
  diagnostics; the analyzers do.
- Every pattern the library builds ignores case in the invariant culture, and a match timeout is a validation failure,
  never an exception.
- A rule instance belongs to one chain. Its options and the names it settles at freeze are that chain's, so a second
  `Add` of the same instance throws. Custom rules see only `Configure` and `Validate`; the options, the freeze hooks,
  and message baking stay internal, so keeping per-chain options apart from an immutable rule later won't break them.
- The built-in rules are sealed, and the abstract ones they derive from (`ComparisonValidator`, `RangeValidator`,
  `StringLengthValidator`) have no constructor outside the library. A built-in rule decides at freeze whether its
  fixed message needs parameters, and no subclass can set a message after that decision.

## Boundaries

- The public API is what a validator author or a result consumer uses: `Validator<TModel>`, the builders, the rule
  extension members, the rule types in `Sharp.Validator.Validators`, the options, `ValidationContext<TModel>`,
  `ValidatorServiceCollectionExtensions`, and the result types. The evaluators, the caches, the condition gates, the
  name composer, and the `Include` adapters are internal.
- `Sharp.Validator.Bindings` is public only so that generated code can call it, and
  `[EditorBrowsable(EditorBrowsableState.Never)]` hides it from IntelliSense. It is a contract with generated code,
  not an API for users. Its name refers to the calls generated code
  binds to at compile time, not to model or configuration binding.
- `Sharp.Validator.Result` references no other project, and at run time `Sharp.Validator` references no project but
  `Sharp.Validator.Result`. The generator and the code fix are never runtime dependencies: they ship in the analyzer
  folder of the `Sharp.Validator` package, and packing fails when either DLL is missing.
- Generated code ends at the `Bindings` calls in its module initializers. Everything after them is runtime
  code, and each piece of it has a fallback for an assembly built without the generator.

## Cross-cutting concerns

- **Native AOT and trimming**: Both libraries build with `IsAotCompatible`, so trim and AOT analysis warnings fail the
  build. Reflection is allowed in two places: the assembly scan in `ValidatorServiceCollectionExtensions`, which carries
  `[RequiresUnreferencedCode]` and shares a method with the Native AOT throw, so the constant
  `RuntimeFeature.IsDynamicCodeSupported` removes it from Native AOT builds, and the `Expression.Compile()` fallbacks
  for accessors and computed cross-property targets. The generated registrar and accessors replace both, and CI
  publishes `Sharp.Validator.AotTests` with Native AOT and runs it.
- **Concurrency**: Frozen validators, the static accessor caches, the registrar list in `ValidatorRegistry`, and the
  instances of each literal `Regex` pattern, which every rule over the same pattern, options, and timeout uses, are
  shared across threads for the life of the process. A validator freezes once, under a lock, and its reads take no lock
  after that. `ValidatorBuilderOptions.Global` is read at freeze only. Everything per call is in the context, and an
  `Include` on a collection chain reuses one child context across the elements, so a rule must not keep its context
  after the call.
- **Errors**: Mistakes the compiler can see are build diagnostics: SVL001, SVL010, and SVL011. A rule declared wrongly
  throws when it is declared, such as a selector that isn't a member chain of the model, `Include(this)`, or
  `ExclusiveBetween` with equal bounds. A validator used wrongly throws `InvalidOperationException`: configured after it
  froze, run with `Validate` when it has async rules, or handed a `List<T>` that changes size while its elements are
  validated. A rule that fails, a regex that times out included, adds a `ValidationError` to the `ValidationResult`, and
  `ValidateAndThrow` turns `Error`-severity entries into a `ValidationException`. `ValidateAsync` returns a rule's
  exception and a cancellation in its task on every path, the sync fast path included; it checks the token before any
  rule, before each async chain, and between elements.
- **Performance**: The hot paths are the plan loops in `Validator<TModel>`, the evaluators, and the rules' `Validate`
  methods. On a valid model they allocate nothing per rule or per element, and none of them boxes values, uses LINQ, or
  creates closures. Two rules are exceptions: `Url()` and `Uri()` parse each value into a `System.Uri`, 56 bytes for a
  plain URL, and `NotEmpty()` and `Empty()` read a collection that doesn't implement the non-generic `ICollection`
  through an enumerator, which can allocate: 40 bytes for a non-empty `HashSet<T>`. A rule with a fixed message reports
  one shared, immutable `ValidationError`. A `Regex` built from a literal pattern gets a copy per core, up to 64,
  because a shared instance stops scaling once threads overlap on it. The original and its copies belong to the pattern:
  every rule over the same pattern, options, and timeout shares them, from a cache of up to 256 patterns, so a validator
  built in every scope doesn't parse its patterns each time.
