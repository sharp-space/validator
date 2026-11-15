using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Sharp.Validator.Validators;

/// <summary>
/// Base for range validators checking a value against two bounds, fixed or read from the model; <c>null</c> values and bounds the model can't
/// supply are skipped.
/// </summary>
public abstract class RangeValidator<TModel, TProperty> : PropertyValidator<TModel, TProperty>
{
    // Strings default to ordinal: Comparer<string>.Default is culture-sensitive, which would make verdicts (and the
    // ctor's bounds check below) vary with the current thread culture.
    private static readonly IComparer<TProperty> DefaultComparer =
        typeof(TProperty) == typeof(string)
            ? (IComparer<TProperty>)StringComparer.Ordinal
            : Comparer<TProperty>.Default;

    private readonly Func<TModel, (bool HasValue, TProperty? Value)>? _fromAccessor;

    private readonly string? _fromPropertyName;

    private readonly Func<TModel, (bool HasValue, TProperty? Value)>? _toAccessor;

    private readonly string? _toPropertyName;

    // Whether Comparer is the default one. Kept per instance, as in ComparisonValidator: reading DefaultComparer from
    // code shared across models costs a helper call per comparison.
    private readonly bool _usesDefaultComparer;

    // The bounds' names as messages print them, settled when the validator freezes.
    private string? _fromName;
    private string? _toName;

    /// <summary>Creates the validator. Throws when <paramref name="to" /> compares smaller than <paramref name="from" />.</summary>
    private protected RangeValidator([DisallowNull] TProperty from, [DisallowNull] TProperty to, IComparer<TProperty>? comparer = null)
    {
        From = from ?? throw new ArgumentNullException(nameof(from), "'from' must not be null.");
        To = to ?? throw new ArgumentNullException(nameof(to), "'to' must not be null.");

        Comparer = comparer ?? DefaultComparer;
        _usesDefaultComparer = ReferenceEquals(Comparer, DefaultComparer);

        if (Comparer.Compare(to, from) < 0)
            throw new ArgumentOutOfRangeException(nameof(to), "'to' should be larger than 'from'.");
    }

    /// <summary>
    /// Creates the validator over bounds resolved from the model at validation time. A name is <c>null</c> for a computed bound. The bounds can't be checked
    /// up front, so an inverted range admits no value.
    /// </summary>
    private protected RangeValidator(
        Func<TModel, (bool HasValue, TProperty? Value)> fromAccessor,
        string? fromPropertyName,
        Func<TModel, (bool HasValue, TProperty? Value)> toAccessor,
        string? toPropertyName,
        IComparer<TProperty>? comparer = null
    )
    {
        _fromAccessor = fromAccessor ?? throw new ArgumentNullException(nameof(fromAccessor));
        _toAccessor = toAccessor ?? throw new ArgumentNullException(nameof(toAccessor));
        _fromPropertyName = fromPropertyName;
        _toPropertyName = toPropertyName;

        From = default!;
        To = default!;
        Comparer = comparer ?? DefaultComparer;

        _usesDefaultComparer = ReferenceEquals(Comparer, DefaultComparer);
    }

    /// <summary>Lower bound of a fixed range.</summary>
    private protected TProperty From { get; }

    /// <summary>Upper bound of a fixed range.</summary>
    private protected TProperty To { get; }

    /// <summary>The member a lower bound that is another property ends at, for the display-name resolver.</summary>
    internal MemberInfo? FromMember { get; init; }

    /// <summary>The member an upper bound that is another property ends at, for the display-name resolver.</summary>
    internal MemberInfo? ToMember { get; init; }

    /// <summary>
    /// Comparer used for the bounds checks. Defaults to <see cref="StringComparer.Ordinal" /> for strings, otherwise <see cref="Comparer{T}.Default" />.
    /// </summary>
    private protected IComparer<TProperty> Comparer { get; }

    // See ComparisonValidator.Compare: the default comparer inlines for value types, the field does not.
    private protected int Compare(TProperty value, TProperty bound) =>
        typeof(TProperty).IsValueType && _usesDefaultComparer
            ? Comparer<TProperty>.Default.Compare(value, bound)
            : Comparer.Compare(value, bound);

    /// <inheritdoc />
    public override bool Validate(TProperty value, ValidationContext<TModel> context)
    {
        if (value == null) return true;

        TProperty from, to;
        if (_fromAccessor == null)
        {
            from = From;
            to = To;
        }
        else
        {
            // A bound behind a null member, or a null bound, skips the check, as an unresolved comparison target does.
            var resolvedFrom = _fromAccessor(context.Model);
            var resolvedTo = _toAccessor!(context.Model);
            if (!resolvedFrom.HasValue || !resolvedTo.HasValue || resolvedFrom.Value == null || resolvedTo.Value == null)
                return true;

            from = resolvedFrom.Value;
            to = resolvedTo.Value;
        }

        if (!IsOutOfRange(value, from, to)) return true;

        if (NeedsMessageParameters(context))
        {
            context.MessageParameters.Add("{From}", from.ToInvariantString());
            context.MessageParameters.Add("{To}", to.ToInvariantString());
            if (_fromPropertyName != null) context.MessageParameters.Add("{FromPropertyName}", _fromName ?? _fromPropertyName);
            if (_toPropertyName != null) context.MessageParameters.Add("{ToPropertyName}", _toName ?? _toPropertyName);
        }

        return false;
    }

    private protected override void FreezeSettings(
        in ValidatorBuilderOptionsSnapshot builderOptions,
        in ValidatorMessageOptions messages
    )
    {
        _fromName = _fromPropertyName is null
            ? null : messages.DisplayNameOf(FromMember)
            ?? builderOptions.NameOf(_fromPropertyName);

        _toName = _toPropertyName is null
            ? null : messages.DisplayNameOf(ToMember)
            ?? builderOptions.NameOf(_toPropertyName);
    }

    // Fixed bounds render once, and so do the names of bounds read from the model. Substituted values are never
    // rescanned, so one with a brace stays a runtime value.
    private protected override string? BakeMessage(string? message)
    {
        if (message == null) return message;

        message = BakeNameless(BakeNameless(message, "{FromPropertyName}", _fromPropertyName), "{ToPropertyName}", _toPropertyName);
        if (_fromAccessor != null)
            return BakeName(BakeName(message, "{FromPropertyName}", _fromName), "{ToPropertyName}", _toName);

        var from = From.ToInvariantString();
        var to = To.ToInvariantString();

        if (!from.Contains('{')) message = message.Replace("{From}", from, StringComparison.Ordinal);

        return to.Contains('{') ? message : message.Replace("{To}", to, StringComparison.Ordinal);
    }

    /// <summary>Returns <c>true</c> when <paramref name="value" /> falls outside the range from <paramref name="from" /> to <paramref name="to" />.</summary>
    private protected abstract bool IsOutOfRange(TProperty value, TProperty from, TProperty to);
}

/// <summary>Value must satisfy <c>from &lt;= value &lt;= to</c>.</summary>
public sealed class InclusiveBetweenValidator<TModel, TProperty> : RangeValidator<TModel, TProperty>
{
    /// <summary>Creates the validator over inclusive bounds.</summary>
    public InclusiveBetweenValidator(
        [DisallowNull] TProperty from,
        [DisallowNull] TProperty to,
        IComparer<TProperty>? comparer = null
    ) : base(from, to, comparer) => ConfigureDefaults();

    /// <summary>Creates the validator over inclusive bounds computed from the model at validation time.</summary>
    public InclusiveBetweenValidator(Func<TModel, TProperty?> from, Func<TModel, TProperty?> to, IComparer<TProperty>? comparer = null) :
        this(ComputedTarget.Of(from), null, ComputedTarget.Of(to), null, comparer)
    {
    }

    // The accessor protocol of the bounds, which CrossPropertyTarget builds; a name is null for a computed bound.
    internal InclusiveBetweenValidator(
        Func<TModel, (bool HasValue, TProperty? Value)> fromAccessor,
        string? fromPropertyName,
        Func<TModel, (bool HasValue, TProperty? Value)> toAccessor,
        string? toPropertyName,
        IComparer<TProperty>? comparer = null
    ) : base(fromAccessor, fromPropertyName, toAccessor, toPropertyName, comparer) => ConfigureDefaults();

    private void ConfigureDefaults() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InclusiveBetween;
        options.ErrorMessage = "'{PropertyName}' must be between {From} and {To}.";
    });

    /// <inheritdoc />
    private protected override bool IsOutOfRange(TProperty value, TProperty from, TProperty to) =>
        Compare(value, from) < 0 || Compare(value, to) > 0;
}

/// <summary>Value must satisfy <c>from &lt; value &lt; to</c>.</summary>
public sealed class ExclusiveBetweenValidator<TModel, TProperty> : RangeValidator<TModel, TProperty>
{
    /// <summary>Creates the validator over exclusive bounds. Throws when <c>from == to</c> — an exclusive range over equal bounds admits no value.</summary>
    public ExclusiveBetweenValidator(
        [DisallowNull] TProperty from,
        [DisallowNull] TProperty to,
        IComparer<TProperty>? comparer = null
    ) : base(from, to, comparer)
    {
        if (Comparer.Compare(to, from) == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(to),
                "'to' must be strictly larger than 'from' for an exclusive range — equal bounds admit no value.");
        }

        ConfigureDefaults();
    }

    /// <summary>Creates the validator over exclusive bounds computed from the model at validation time; equal bounds admit no value.</summary>
    public ExclusiveBetweenValidator(Func<TModel, TProperty?> from, Func<TModel, TProperty?> to, IComparer<TProperty>? comparer = null) :
        this(ComputedTarget.Of(from), null, ComputedTarget.Of(to), null, comparer)
    {
    }

    // The accessor protocol of the bounds, which CrossPropertyTarget builds; a name is null for a computed bound.
    internal ExclusiveBetweenValidator(
        Func<TModel, (bool HasValue, TProperty? Value)> fromAccessor,
        string? fromPropertyName,
        Func<TModel, (bool HasValue, TProperty? Value)> toAccessor,
        string? toPropertyName,
        IComparer<TProperty>? comparer = null
    ) : base(fromAccessor, fromPropertyName, toAccessor, toPropertyName, comparer) => ConfigureDefaults();

    private void ConfigureDefaults() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.ExclusiveBetween;
        options.ErrorMessage = "'{PropertyName}' must be between {From} and {To} (exclusive).";
    });

    /// <inheritdoc />
    private protected override bool IsOutOfRange(TProperty value, TProperty from, TProperty to) =>
        Compare(value, from) <= 0 || Compare(value, to) >= 0;
}