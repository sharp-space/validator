using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Sharp.Validator.Result;

namespace Sharp.Validator;

/// <summary>
/// Per-validation state: model, error bag, parameters, and the optional include filter. A context belongs to the call that received it, a rule's, a
/// condition's, or the configure callback of <c>Validate(model, configure)</c>: use it during that call, and don't keep it, or the parameters and message
/// parameters it hands out, once the call returns. The library may reuse a context it created from then on, as an Include over a collection already
/// reuses one child context for all its elements. To read a context after its validation, create it and pass it to <c>Validate(context)</c>; that one
/// stays yours. A context runs one validation at a time: one started while another is running on it throws <see cref="InvalidOperationException" />.
/// </summary>
public sealed class ValidationContext<TModel>(TModel model) : IParameterScope
{
    // The root context of an Include child's run, which holds the parameters the whole run shares; null on the root.
    // The child points at the root itself, so a run that never touches Parameters allocates nothing for them.
    private readonly IParameterScope? _root;

    // The element sources this run has read, by the slot the validator gave each source that several chains walk.
    private object?[]? _elementSources;

    private List<ValidationError>? _errors;

    // What most validations never use, so it stays out of the context until one does.
    private Extras? _extras;

    private MessageParameters? _messageParameters;

    // Set from the start of a run until ResetRunState ends it, after a pass or a throw. A run started on a context
    // whose run hasn't ended would take that run's errors, element sources and selection, and reset them, so the outer
    // run would report what the inner one left. A plain field rather than an interlocked one: it stops a nested run and
    // a second run started before the first completed, and a context shared between threads is unsupported anyway.
    private bool _running;

    private ValidationContext(TModel model, IParameterScope root) : this(model) => _root = root;

    /// <summary>The model being validated.</summary>
    public TModel Model { get; private set; } = model ??
        throw new ArgumentNullException(nameof(model), "Validator model parameter cannot be null.");

    /// <summary>Error sink. Lazily allocated so the all-valid path never allocates a list.</summary>
    internal List<ValidationError> Errors => _errors ??= [];

    /// <summary>Error-severity entries recorded so far. Warning and Information entries are non-blocking.</summary>
    internal int BlockingErrorCount { get; private set; }

    /// <summary>An Error-severity entry has been recorded — the predicate CascadeMode.Stop reacts to on both the sync and async paths.</summary>
    internal bool HasBlockingError => BlockingErrorCount > 0;

    /// <summary>This context validates a child under Include, so the root owns the ambient name and message wrappers.</summary>
    internal bool IsIncludedRun => _root != null;

    /// <summary>
    /// Error message override for the rule being evaluated. Set it inside the rule: it is cleared when that rule finishes, pass or fail, so it can't leak
    /// into a later rule.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Placeholder values for the rule being evaluated. Cleared when that rule finishes, pass or fail; keep per-call data in <see cref="Parameters" />.
    /// </summary>
    public MessageParameters MessageParameters => _messageParameters ??= new();

    internal bool ExistsMessageParameters => _messageParameters != null;

    /// <summary>
    /// Free-form storage available to validators within this validation call. Included child validators share this storage; rule-local message state,
    /// errors, and evaluator caches remain isolated.
    /// </summary>
    public IDictionary<string, object?> Parameters =>
        _root?.Parameters ?? ((_extras ??= new()).Parameters ??= new Dictionary<string, object?>());

    internal void AddError(ValidationError error)
    {
        Errors.Add(error);
        if (error.Severity == ValidationErrorSeverity.Error) BlockingErrorCount++;
    }

    /// <summary>
    /// Builds the result from the collected errors and hands the list's ownership to the result: re-validating with this context starts a fresh run instead
    /// of mutating the previously returned result's error list. A run that recorded nothing returns the shared <see cref="ValidationResult.Empty" />.
    /// User-supplied state (Parameters and the included paths) survives across runs.
    /// </summary>
    internal ValidationResult ToResult() => TakeErrors() is { } errors ? ValidationResult.FromOwnedList(errors) : ValidationResult.Empty;

    /// <summary>Ends the run and hands over the collected errors, <c>null</c> when none were recorded.</summary>
    internal List<ValidationError>? TakeErrors()
    {
        var errors = _errors;
        ResetRunState();

        return errors;
    }

    /// <summary>
    /// Points an Include child context at the chain's next element. TakeErrors has reset everything run-scoped, and the selection narrowed from the parent
    /// is the same for every element of the chain.
    /// </summary>
    internal void Rebind(TModel model) => Model = model;

    internal void AbortValidation() => ResetRunState();

    /// <summary>Starts a run, or throws when a run on this context hasn't ended.</summary>
    internal void BeginRun()
    {
        if (_running) ThrowRunning();

        _running = true;
    }

    [DoesNotReturn]
    private static void ThrowRunning() =>
        throw new InvalidOperationException(
            "This context is in a validation that hasn't finished. Run one validation on a context at a time, and give " +
            "a nested or concurrent validation a context of its own.");

    private void ResetRunState()
    {
        _running = false;
        _errors = null;
        BlockingErrorCount = 0;

        if (_extras is { } extras)
        {
            // Run-scoped bookkeeping: stale entries would make HasRuleChainFailure skip async chains on reuse.
            extras.FailedRuleChains?.Clear();
            extras.SelectedChainPath = null;
        }

        // The slots belong to the validator that ran: the next run may be another validator's, whose slots hold other
        // sources, and a length check wouldn't tell two validators with as many slots apart.
        if (_elementSources is { } elementSources) Array.Clear(elementSources);

        ResetRuleMessageState();
    }

    internal ValidationContext<TChild> CreateChild<TChild>(TChild model)
    {
        var child = new ValidationContext<TChild>(model, _root ?? this);
        if (_extras is { IncludedPaths: { } includedPaths, SelectedChainPath: { } chainPath })
            child.NarrowFrom(includedPaths, chainPath);

        return child;
    }

    internal void ResetRuleMessageState()
    {
        _messageParameters?.Clear();
        ErrorMessage = null;
    }

    /// <summary>
    /// Restricts validation to the listed property paths; other rules are skipped. A selected path covers everything beneath it, rules on its ancestors
    /// still run, and a path inside an included child narrows that child. The selector is a member chain of the model, as a chain's own is; any other throws
    /// <see cref="InvalidOperationException" />.
    /// </summary>
    public void IncludeProperty<TProperty>(Expression<Func<TModel, TProperty>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        // A selection matches chains by path, so a selector without one would select whatever chain shares the
        // truncated name; it is rejected as the chains' own selectors are.
        expression.EnsureChainSelector();
        ((_extras ??= new()).IncludedPaths ??= new HashSet<string>(StringComparer.Ordinal)).Add(expression.GetPropertyPath());
    }

    internal bool SelectChain(PropertyValidatorBuilder<TModel> builder)
    {
        if (_extras is not { IncludedPaths: { } includedPaths } extras) return true;

        var chainPath = builder.FilterPath;
        foreach (var selected in includedPaths)
        {
            if (!IsSameOrBelow(chainPath, selected) && !IsSameOrBelow(selected, chainPath)) continue;

            extras.SelectedChainPath = chainPath;

            return true;
        }

        return false;
    }

    private static bool IsSameOrBelow(string path, string ancestor) =>
        path.Length == ancestor.Length
            ? path == ancestor
            : path.Length > ancestor.Length && path[ancestor.Length] == '.' &&
            path.StartsWith(ancestor, StringComparison.Ordinal);

    // Called on a new child. A selection that covers the chain itself runs the child whole, so that is looked for
    // first, before any of the narrower paths gets a set to go into.
    private void NarrowFrom(HashSet<string> parentSelection, string chainPath)
    {
        foreach (var selected in parentSelection)
            if (IsSameOrBelow(chainPath, selected))
                return;

        foreach (var selected in parentSelection)
            if (IsSameOrBelow(selected, chainPath))
            {
                ((_extras ??= new()).IncludedPaths ??= new HashSet<string>(StringComparer.Ordinal))
                    .Add(selected[(chainPath.Length + 1)..]);
            }
    }

    internal void MarkRuleChainFailed(PropertyValidatorBuilder<TModel> builder) =>
        ((_extras ??= new()).FailedRuleChains ??= []).Add(builder);

    internal bool HasRuleChainFailure(PropertyValidatorBuilder<TModel> builder) =>
        _extras?.FailedRuleChains?.Contains(builder) == true;

    // Reads the source at the slot once per run: the first chain to walk it reads it, the rest get the same elements. A
    // source that isn't a list is copied into an array, so a sequence that can be enumerated only once is walked by
    // every chain. Takes the accessor rather than a Func over the model so a chain that doesn't reuse its source pays
    // for no closure.
    internal IEnumerable<TElement>? GetElements<TElement>(
        int slot,
        int slotCount,
        Func<TModel, IEnumerable<TElement>?> accessor
    )
    {
        // An array left by a run of a validator with fewer slots is empty, so it can be dropped.
        var sources = _elementSources;
        if (sources == null || sources.Length < slotCount) _elementSources = sources = new object?[slotCount];

        if (sources[slot] is { } cached)
            return ReferenceEquals(cached, NullElementSource.Instance) ? null : (IEnumerable<TElement>)cached;

        var source = accessor(Model);
        if (source == null)
        {
            sources[slot] = NullElementSource.Instance;
            return null;
        }

        var reusable = source is IList<TElement> ? source : source.ToArray();
        sources[slot] = reusable;
        return reusable;
    }

    private sealed class NullElementSource
    {
        internal static readonly NullElementSource Instance = new();
    }

    // State a validation creates only when it uses a feature that needs it: the parameters of a root context, the
    // selection of a partial validation and the chain it is in, and the async chains whose sync rules failed. Kept out
    // of the context, it leaves the context four references smaller for every other validation.
    private sealed class Extras
    {
        internal Dictionary<string, object?>? Parameters { get; set; }

        internal HashSet<string>? IncludedPaths { get; set; }

        // The chain being evaluated under a selection: an Include inside it narrows its child by this path.
        internal string? SelectedChainPath { get; set; }

        internal HashSet<PropertyValidatorBuilder<TModel>>? FailedRuleChains { get; set; }
    }
}

// The root context of a validation run, as an Include child of another model type sees it: the one Parameters
// dictionary the whole run shares.
internal interface IParameterScope
{
    IDictionary<string, object?> Parameters { get; }
}

/// <summary>Dictionary of <c>{placeholder} → value</c> entries used in error messages.</summary>
public sealed class MessageParameters : IEnumerable<KeyValuePair<string, string>>
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>Gets or sets a placeholder value; bare keys are wrapped with <c>{}</c> automatically.</summary>
    public string this[string placeholder]
    {
        get => _values[Normalize(placeholder)];
        set => _values[Normalize(placeholder)] = value;
    }

    /// <summary>The number of parameters currently stored.</summary>
    public int Count => _values.Count;

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Adds or overwrites a placeholder. The bare key is wrapped with <c>{}</c> automatically.</summary>
    public MessageParameters Add(string placeholder, string parameter)
    {
        _values[Normalize(placeholder)] = parameter;
        return this;
    }

    internal bool TryGet(ReadOnlySpan<char> placeholder, out string parameter) =>
        _values.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(placeholder, out parameter!);

    /// <summary>Removes all parameters.</summary>
    public void Clear() => _values.Clear();

    private static string Normalize(string placeholder) =>
        // ReSharper disable once MergeIntoPattern — a list pattern adds a null test, so a null key would become "{}"
        // instead of throwing.
        placeholder.Length >= 2 && placeholder[0] == '{' && placeholder[^1] == '}'
            ? placeholder
            : "{" + placeholder + "}";
}