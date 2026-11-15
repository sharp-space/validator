using System.ComponentModel;

// The seam generated code binds to, kept apart from the public surface in a namespace of its own. The accessor caches
// live in Sharp.Validator and resolve from the parent namespace.

namespace Sharp.Validator.Bindings;

/// <summary>
/// Hook through which source-generated code supplies precompiled property accessors. Generated code calls the <c>Register*</c> methods from a
/// <c>[ModuleInitializer]</c>; the accessor caches then return the precompiled delegate instead of building one with <c>Expression.Compile()</c> — which,
/// under Native AOT, runs through the (slower) expression interpreter on every invocation. A registry miss is non-fatal: the cache falls back to the
/// runtime-compiled accessor, so this is a pure performance optimization. Infrastructure for the Sharp.Validator source generator; not for direct use.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AccessorRegistry
{
    /// <summary>
    /// Registers a precompiled accessor for the non-nested property identified by <paramref name="path" /> (its dotted member path, e.g. <c>"Email"</c>) on
    /// <typeparamref name="TModel" />.
    /// </summary>
    public static void RegisterProperty<TModel, TProperty>(string path, Func<TModel, TProperty> accessor)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(accessor);
        PropertyAccessorCache<TModel, TProperty>.Seed(path, accessor);
    }

    /// <summary>
    /// Diagnostic hook: whether a precompiled property accessor was registered under <paramref name="path" /> for <c>(TModel, TProperty)</c>. Used by tests
    /// to prove the generator's key matches the runtime lookup key (a mismatch would silently fall back to the runtime-compiled accessor with no error).
    /// </summary>
    public static bool HasProperty<TModel, TProperty>(string path) =>
        PropertyAccessorCache<TModel, TProperty>.HasGenerated(path);

    /// <summary>
    /// Registers a precompiled null-safe accessor for the nested property at <paramref name="path" /> (e.g. <c>"Policy.Max"</c>): it returns
    /// <c>HasValue = false</c> when an intermediate member is null.
    /// </summary>
    public static void RegisterNullSafe<TModel, TProperty>(string path, Func<TModel, (bool HasValue, TProperty Value)> accessor)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(accessor);
        NullSafeAccessorCache<TModel, TProperty>.Seed(path, accessor);
    }

    /// <inheritdoc cref="HasProperty{TModel,TProperty}" />
    public static bool HasNullSafe<TModel, TProperty>(string path) =>
        NullSafeAccessorCache<TModel, TProperty>.HasGenerated(path);

    /// <summary>
    /// Registers a precompiled, null-safe collection accessor for the elements selector at <paramref name="path" /> (e.g. <c>"Tags"</c>): a null
    /// intermediate yields a null sequence, which the evaluator treats as "skip".
    /// </summary>
    public static void RegisterElements<TModel, TProperty>(string path, Func<TModel, IEnumerable<TProperty>?> accessor)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(accessor);
        ElementsAccessorCache<TModel, TProperty>.Seed(path, accessor);
    }

    /// <inheritdoc cref="HasProperty{TModel,TProperty}" />
    public static bool HasElements<TModel, TProperty>(string path) =>
        ElementsAccessorCache<TModel, TProperty>.HasGenerated(path);
}