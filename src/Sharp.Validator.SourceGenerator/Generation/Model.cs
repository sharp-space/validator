namespace Sharp.Validator.SourceGenerator.Generation;

/// <summary>
/// A single <c>IValidator&lt;T&gt;</c> implementation discovered in the compilation. One validator class that implements several
/// <c>IValidator&lt;T&gt;</c> interfaces yields one <see cref="ValidatorInfo" /> per interface. All members are strings, bools, or equatable arrays, so
/// the value is equatable for incremental caching.
/// </summary>
internal readonly record struct ValidatorInfo(
    string ServiceInterfaceFqn,
    string ImplementationFqn,
    bool Accessible,
    EquatableArray<string> ExperimentalIds = default
);

/// <summary>Which accessor cache a discovered call site feeds, and therefore which codegen shape it needs.</summary>
internal enum AccessorKind
{
    /// <summary>Flat <c>Property(x =&gt; x.Member)</c> → <c>Func&lt;TModel,TProperty&gt;</c>.</summary>
    Property,

    /// <summary>Nested <c>Property(x =&gt; x.A.B)</c> → null-safe <c>Func&lt;TModel,(bool,TProperty)&gt;</c>.</summary>
    NullSafe,

    /// <summary><c>Elements(x =&gt; x.Items)</c> → null-safe <c>Func&lt;TModel,IEnumerable&lt;TProperty&gt;?&gt;</c>.</summary>
    Elements
}

/// <summary>One member hop in an accessor chain. <see cref="Guard" /> is true for an intermediate that must be null-checked.</summary>
internal readonly record struct AccessorStep(string Member, bool Guard);

/// <summary>
/// An accessor call site to precompile. All fields are strings/enums/equatable arrays, so the value is equatable for incremental caching and dedupes
/// identical call sites across the compilation. <see cref="Path" /> is the registry key (the dotted member path, equal to the runtime
/// <c>PropertyPath</c>); <see cref="Chain" /> is the root-first member sequence the codegen dereferences. <see cref="ExperimentalIds" /> are the
/// <c>[Experimental]</c> diagnostics the emitted code must silence.
/// </summary>
internal readonly record struct AccessorInfo(
    AccessorKind Kind,
    string ModelFqn,
    string PropertyFqn,
    string Path,
    EquatableArray<AccessorStep> Chain,
    EquatableArray<string> ExperimentalIds = default
);