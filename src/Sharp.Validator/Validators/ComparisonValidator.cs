using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sharp.Validator.Validators;

/// <summary>
/// Base for order-comparison validators. The target is a fixed value or another property of the model; <c>null</c> values and unresolved
/// targets are skipped.
/// </summary>
public abstract class ComparisonValidator<TModel, TProperty> : PropertyValidator<TModel, TProperty>
{
    // Strings default to ordinal: Comparer<string>.Default is culture-sensitive, which would make verdicts vary with
    // the current thread culture and diverge from EqualValidator's ordinal default.
    private static readonly IComparer<TProperty> DefaultComparer =
        typeof(TProperty) == typeof(string)
            ? (IComparer<TProperty>)StringComparer.Ordinal
            : Comparer<TProperty>.Default;

    private readonly Func<TModel, (bool HasValue, TProperty? Value)>? _targetAccessor;

    private readonly string? _targetPropertyName;

    private readonly TProperty? _targetValue;

    // Whether Comparer is the default one. Kept per instance: DefaultComparer is a static of a class generic over the
    // model, and code shared across models reaches such a static through a helper call, once per comparison.
    private readonly bool _usesDefaultComparer;

    // The target's name as messages print it, settled when the validator freezes.
    private string? _targetName;

    /// <summary>Creates the validator comparing against a fixed target value.</summary>
    private protected ComparisonValidator([DisallowNull] TProperty targetValue, IComparer<TProperty>? comparer = null)
    {
        Comparer = comparer ?? DefaultComparer;
        _usesDefaultComparer = ReferenceEquals(Comparer, DefaultComparer);

        _targetValue = targetValue ??
            throw new ArgumentNullException(nameof(targetValue), $"{nameof(targetValue)} must not be null.");
    }

    /// <summary>
    /// Creates the validator comparing against a value resolved from the model at validation time; <paramref name="targetPropertyName" /> names it for
    /// messages and is <c>null</c> for a computed target.
    /// </summary>
    private protected ComparisonValidator(
        Func<TModel, (bool HasValue, TProperty? Value)> targetAccessor,
        string? targetPropertyName,
        IComparer<TProperty>? comparer = null
    )
    {
        Comparer = comparer ?? DefaultComparer;
        _usesDefaultComparer = ReferenceEquals(Comparer, DefaultComparer);

        _targetAccessor = targetAccessor ??
            throw new ArgumentNullException(nameof(targetAccessor), $"{nameof(targetAccessor)} must not be null.");

        _targetPropertyName = targetPropertyName;
    }

    /// <summary>The member a target that is another property ends at, for the display-name resolver.</summary>
    internal MemberInfo? TargetMember { get; init; }

    /// <summary>
    /// Comparer used for ordering. Defaults to <see cref="StringComparer.Ordinal" /> for strings, otherwise <see cref="Comparer{T}.Default" />.
    /// </summary>
    private protected IComparer<TProperty> Comparer { get; }

    // Comparer<T>.Default.Compare on a value type is a JIT intrinsic that inlines to CompareTo; the same call through
    // the IComparer<T> field is an interface dispatch plus a virtual call per comparison.
    private protected int Compare(TProperty value, TProperty target) =>
        typeof(TProperty).IsValueType && _usesDefaultComparer
            ? Comparer<TProperty>.Default.Compare(value, target)
            : Comparer.Compare(value, target);

    /// <inheritdoc />
    public override bool Validate(TProperty value, ValidationContext<TModel> context)
    {
        if (value == null) return true;

        TProperty? targetValue;
        if (_targetAccessor != null)
        {
            // A null intermediate in the target selector (x => x.Policy.Min) skips the comparison, matching the
            // null/leaf short-circuits below.
            var accessed = _targetAccessor(context.Model);
            if (!accessed.HasValue) return true;

            targetValue = accessed.Value;
        }
        else targetValue = _targetValue;

        if (targetValue == null) return true;

        if (Validate(value, targetValue)) return true;

        if (NeedsMessageParameters(context))
        {
            context.MessageParameters.Add("{TargetValue}", targetValue.ToInvariantString());
            if (_targetPropertyName != null)
                context.MessageParameters.Add("{TargetPropertyName}", _targetName ?? _targetPropertyName);
        }

        return false;
    }

    private protected override void FreezeSettings(
        in ValidatorBuilderOptionsSnapshot builderOptions,
        in ValidatorMessageOptions messages
    ) => _targetName = _targetPropertyName is null
        ? null : messages.DisplayNameOf(TargetMember)
        ?? builderOptions.NameOf(_targetPropertyName);

    // A fixed target renders once, and so does a target's name. Substituted values are never rescanned, so one with a
    // brace stays a runtime value.
    private protected override string? BakeMessage(string? message)
    {
        if (message == null) return message;

        message = BakeNameless(message, "{TargetPropertyName}", _targetPropertyName);
        if (_targetAccessor != null) return BakeName(message, "{TargetPropertyName}", _targetName);

        var target = _targetValue.ToInvariantString();
        return target.Contains('{')
            ? message
            : message.Replace("{TargetValue}", target, StringComparison.Ordinal);
    }

    /// <summary>Returns <c>true</c> when <paramref name="value" /> satisfies the comparison against <paramref name="targetValue" />.</summary>
    private protected abstract bool Validate(TProperty value, TProperty targetValue);
}

/// <summary>Value must be strictly greater than the target.</summary>
public sealed class GreaterThanValidator<TModel, TProperty> : ComparisonValidator<TModel, TProperty>
{
    private const string ErrorCode = ErrorCodes.GreaterThan;
    private const string ErrorMessage = "'{PropertyName}' must be greater than '{TargetValue}'.";

    /// <summary>Creates the validator comparing against a fixed target value.</summary>
    public GreaterThanValidator(
        [DisallowNull] TProperty targetValue,
        IComparer<TProperty>? comparer = null
    ) : base(targetValue, comparer) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCode;
        options.ErrorMessage = ErrorMessage;
    });

    /// <summary>Creates the validator comparing against a value computed from the model at validation time.</summary>
    public GreaterThanValidator(Func<TModel, TProperty?> target, IComparer<TProperty>? comparer = null) :
        this(ComputedTarget.Of(target), null, comparer)
    {
    }

    // The accessor protocol of a comparison target, which CrossPropertyTarget builds; a name is null for
    // a computed one.
    internal GreaterThanValidator(
        Func<TModel, (bool HasValue, TProperty? Value)> targetAccessor,
        string? targetPropertyName,
        IComparer<TProperty>? comparer = null
    ) : base(targetAccessor, targetPropertyName, comparer) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCode;
        options.ErrorMessage = ErrorMessage;
    });

    /// <inheritdoc />
    private protected override bool Validate(TProperty value, TProperty targetValue) => Compare(value, targetValue) > 0;
}

/// <summary>Value must be greater than or equal to the target.</summary>
public sealed class GreaterThanOrEqualValidator<TModel, TProperty> : ComparisonValidator<TModel, TProperty>
{
    private const string ErrorCode = ErrorCodes.GreaterThanOrEqual;
    private const string ErrorMessage = "'{PropertyName}' must be greater than or equal to '{TargetValue}'.";

    /// <summary>Creates the validator comparing against a fixed target value.</summary>
    public GreaterThanOrEqualValidator(
        [DisallowNull] TProperty targetValue,
        IComparer<TProperty>? comparer = null
    ) : base(targetValue, comparer) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCode;
        options.ErrorMessage = ErrorMessage;
    });

    /// <summary>Creates the validator comparing against a value computed from the model at validation time.</summary>
    public GreaterThanOrEqualValidator(
        Func<TModel, TProperty?> target,
        IComparer<TProperty>? comparer = null
    ) : this(ComputedTarget.Of(target), null, comparer)
    {
    }

    // The accessor protocol of a comparison target, which CrossPropertyTarget builds; a name is null for
    // a computed one.
    internal GreaterThanOrEqualValidator(
        Func<TModel, (bool HasValue, TProperty? Value)> targetAccessor,
        string? targetPropertyName,
        IComparer<TProperty>? comparer = null
    ) : base(targetAccessor, targetPropertyName, comparer) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCode;
        options.ErrorMessage = ErrorMessage;
    });

    /// <inheritdoc />
    private protected override bool Validate(TProperty value, TProperty targetValue) => Compare(value, targetValue) >= 0;
}

/// <summary>Value must be strictly less than the target.</summary>
public sealed class LessThanValidator<TModel, TProperty> : ComparisonValidator<TModel, TProperty>
{
    private const string ErrorCode = ErrorCodes.LessThan;
    private const string ErrorMessage = "'{PropertyName}' must be less than '{TargetValue}'.";

    /// <summary>Creates the validator comparing against a fixed target value.</summary>
    public LessThanValidator(
        [DisallowNull] TProperty targetValue,
        IComparer<TProperty>? comparer = null
    ) : base(targetValue, comparer) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCode;
        options.ErrorMessage = ErrorMessage;
    });

    /// <summary>Creates the validator comparing against a value computed from the model at validation time.</summary>
    public LessThanValidator(Func<TModel, TProperty?> target, IComparer<TProperty>? comparer = null) :
        this(ComputedTarget.Of(target), null, comparer)
    {
    }

    // The accessor protocol of a comparison target, which CrossPropertyTarget builds; a name is null for
    // a computed one.
    internal LessThanValidator(
        Func<TModel, (bool HasValue, TProperty? Value)> targetAccessor,
        string? targetPropertyName,
        IComparer<TProperty>? comparer = null
    ) : base(targetAccessor, targetPropertyName, comparer) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCode;
        options.ErrorMessage = ErrorMessage;
    });

    /// <inheritdoc />
    private protected override bool Validate(TProperty value, TProperty targetValue) => Compare(value, targetValue) < 0;
}

/// <summary>Value must be less than or equal to the target.</summary>
public sealed class LessThanOrEqualValidator<TModel, TProperty> : ComparisonValidator<TModel, TProperty>
{
    private const string ErrorCode = ErrorCodes.LessThanOrEqual;
    private const string ErrorMessage = "'{PropertyName}' must be less than or equal to '{TargetValue}'.";

    /// <summary>Creates the validator comparing against a fixed target value.</summary>
    public LessThanOrEqualValidator(
        [DisallowNull] TProperty targetValue,
        IComparer<TProperty>? comparer = null
    ) : base(targetValue, comparer) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCode;
        options.ErrorMessage = ErrorMessage;
    });

    /// <summary>Creates the validator comparing against a value computed from the model at validation time.</summary>
    public LessThanOrEqualValidator(Func<TModel, TProperty?> target, IComparer<TProperty>? comparer = null) :
        this(ComputedTarget.Of(target), null, comparer)
    {
    }

    // The accessor protocol of a comparison target, which CrossPropertyTarget builds; a name is null for
    // a computed one.
    internal LessThanOrEqualValidator(
        Func<TModel, (bool HasValue, TProperty? Value)> targetAccessor,
        string? targetPropertyName,
        IComparer<TProperty>? comparer = null
    ) : base(targetAccessor, targetPropertyName, comparer) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCode;
        options.ErrorMessage = ErrorMessage;
    });

    /// <inheritdoc />
    private protected override bool Validate(TProperty value, TProperty targetValue) => Compare(value, targetValue) <= 0;
}

// A target the caller computes always has a value: only a member chain can find a null on its way and skip the rule.
internal static class ComputedTarget
{
    internal static Func<TModel, (bool HasValue, TProperty? Value)> Of<TModel, TProperty>(
        Func<TModel, TProperty?> target,
        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Global — the exception reports the name.
        [CallerArgumentExpression(nameof(target))] string? parameterName = null
    )
    {
        ArgumentNullException.ThrowIfNull(target, parameterName);
        return model => (true, target(model));
    }
}