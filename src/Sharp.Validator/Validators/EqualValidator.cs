using System.Reflection;

namespace Sharp.Validator.Validators;

/// <summary>
/// Value must equal a fixed value or another property's value. Unlike most rules, <c>null</c> follows equality semantics rather than the skip-null
/// convention: with the default comparer <c>null == null</c> passes and <c>null</c> vs non-<c>null</c> fails — otherwise a confirmation field (
/// <c>Equal(x =&gt; x.Password)</c>) would silently pass when left <c>null</c>. A comparer you pass decides for <c>null</c> too.
/// </summary>
public sealed class EqualValidator<TModel, TProperty> : PropertyValidator<TModel, TProperty>
{
    private readonly IEqualityComparer<TProperty> _comparer;
    private readonly Func<TModel, (bool HasValue, TProperty? Value)>? _target;
    private readonly string? _targetPropertyName;
    private readonly TProperty? _value;

    // The target's name as messages print it, settled when the validator freezes.
    private string? _targetName;

    /// <summary>Compares against a fixed value.</summary>
    public EqualValidator(TProperty? value, IEqualityComparer<TProperty>? comparer = null)
    {
        _value = value;

        // Default to EqualityComparer<TProperty>.Default (cached per closed generic) so the comparison routes through
        // IEquatable<TProperty> instead of static object.Equals, which would box both operands for value types — on
        // every call, success path included.
        _comparer = comparer ?? EqualityComparer<TProperty>.Default;

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.Equal;
            options.ErrorMessage = "'{PropertyName}' must be equal to '{TargetValue}'.";
        });
    }

    /// <summary>Compares against a value computed from the model at validation time.</summary>
    public EqualValidator(Func<TModel, TProperty?> target, IEqualityComparer<TProperty>? comparer = null)
        : this(ComputedTarget.Of(target), null, comparer)
    {
    }

    // The accessor protocol of a target, which CrossPropertyTarget builds: the rule is skipped when the accessor finds
    // nothing to read, and a name is null for a computed target.
    internal EqualValidator(
        Func<TModel, (bool HasValue, TProperty? Value)> targetAccessor,
        string? targetPropertyName,
        IEqualityComparer<TProperty>? comparer = null
    )
    {
        _target = targetAccessor ?? throw new ArgumentNullException(nameof(targetAccessor));
        _targetPropertyName = targetPropertyName;

        // Default to EqualityComparer<TProperty>.Default (cached per closed generic) so the comparison routes through
        // IEquatable<TProperty> instead of static object.Equals, which would box both operands for value types — on
        // every call, success path included.
        _comparer = comparer ?? EqualityComparer<TProperty>.Default;

        // A named target is another property, so the message names it rather than print a value that may be a secret,
        // as it is for a password confirmation.
        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.Equal;
            options.ErrorMessage = targetPropertyName != null
                ? "'{PropertyName}' must be equal to '{TargetPropertyName}'."
                : "'{PropertyName}' must be equal to '{TargetValue}'.";
        });
    }

    /// <summary>The member a target that is another property ends at, for the display-name resolver.</summary>
    internal MemberInfo? TargetMember { get; init; }

    private protected override void FreezeSettings(
        in ValidatorBuilderOptionsSnapshot builderOptions,
        in ValidatorMessageOptions messages
    ) => _targetName = _targetPropertyName is null
        ? null : messages.DisplayNameOf(TargetMember)
        ?? builderOptions.NameOf(_targetPropertyName);

    // A fixed value renders once, and so does a target's name. Substituted values are never rescanned, so one with a
    // brace stays a runtime value.
    private protected override string? BakeMessage(string? message)
    {
        if (message == null) return message;

        message = BakeNameless(message, "{TargetPropertyName}", _targetPropertyName);
        if (_target != null) return BakeName(message, "{TargetPropertyName}", _targetName);

        var value = _value.ToInvariantString();
        return value.Contains('{') ? message : message.Replace("{TargetValue}", value, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Validate(TProperty value, ValidationContext<TModel> context)
    {
        TProperty? targetValue;
        if (_target != null)
        {
            // A null on the way to the target skips the rule, as it does for the comparison rules.
            var accessed = _target(context.Model);
            if (!accessed.HasValue) return true;

            targetValue = accessed.Value;
        }
        else targetValue = _value;

        // The comparer decides for null as well; the default one treats two nulls as equal and null and a value as not.
        if (_comparer.Equals(targetValue, value)) return true;

        if (NeedsMessageParameters(context))
        {
            context.MessageParameters.Add("{TargetValue}", targetValue.ToInvariantString());
            if (_targetPropertyName != null)
                context.MessageParameters.Add("{TargetPropertyName}", _targetName ?? _targetPropertyName);
        }

        return false;
    }
}

/// <summary>
/// Value must not equal a fixed value or another property's value: it fails exactly where <c>Equal</c> with the same target and comparer passes. With
/// the default comparer a <c>null</c> value passes against a non-<c>null</c> target, but <c>null</c> vs <c>null</c> fails — both sides null are equal.
/// </summary>
public sealed class NotEqualValidator<TModel, TProperty> : PropertyValidator<TModel, TProperty>
{
    private readonly IEqualityComparer<TProperty> _comparer;
    private readonly Func<TModel, (bool HasValue, TProperty? Value)>? _target;
    private readonly string? _targetPropertyName;
    private readonly TProperty? _value;

    // The target's name as messages print it, settled when the validator freezes.
    private string? _targetName;

    /// <summary>Compares against a fixed value.</summary>
    public NotEqualValidator(TProperty? value, IEqualityComparer<TProperty>? comparer = null)
    {
        _value = value;

        // Default to EqualityComparer<TProperty>.Default (cached per closed generic) so the comparison routes through
        // IEquatable<TProperty> instead of static object.Equals, which would box both operands for value types — on
        // every call, success path included.
        _comparer = comparer ?? EqualityComparer<TProperty>.Default;

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.NotEqual;
            options.ErrorMessage = "'{PropertyName}' must not be equal to '{TargetValue}'.";
        });
    }

    /// <summary>Compares against a value computed from the model at validation time.</summary>
    public NotEqualValidator(Func<TModel, TProperty?> target, IEqualityComparer<TProperty>? comparer = null)
        : this(ComputedTarget.Of(target), null, comparer)
    {
    }

    // The accessor protocol of a target, which CrossPropertyTarget builds: the rule is skipped when the accessor finds
    // nothing to read, and a name is null for a computed target.
    internal NotEqualValidator(
        Func<TModel, (bool HasValue, TProperty? Value)> targetAccessor,
        string? targetPropertyName,
        IEqualityComparer<TProperty>? comparer = null
    )
    {
        _target = targetAccessor ?? throw new ArgumentNullException(nameof(targetAccessor));
        _targetPropertyName = targetPropertyName;

        // Default to EqualityComparer<TProperty>.Default (cached per closed generic) so the comparison routes through
        // IEquatable<TProperty> instead of static object.Equals, which would box both operands for value types — on
        // every call, success path included.
        _comparer = comparer ?? EqualityComparer<TProperty>.Default;

        // A named target is another property, so the message names it rather than print a value that may be a secret,
        // as it is for a password confirmation.
        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.NotEqual;
            options.ErrorMessage = targetPropertyName != null
                ? "'{PropertyName}' must not be equal to '{TargetPropertyName}'."
                : "'{PropertyName}' must not be equal to '{TargetValue}'.";
        });
    }

    /// <summary>The member a target that is another property ends at, for the display-name resolver.</summary>
    internal MemberInfo? TargetMember { get; init; }

    private protected override void FreezeSettings(in ValidatorBuilderOptionsSnapshot builderOptions, in ValidatorMessageOptions messages) =>
        _targetName = _targetPropertyName is null
            ? null : messages.DisplayNameOf(TargetMember)
            ?? builderOptions.NameOf(_targetPropertyName);

    // A fixed value renders once, and so does a target's name. Substituted values are never rescanned, so one with a
    // brace stays a runtime value.
    private protected override string? BakeMessage(string? message)
    {
        if (message == null) return message;

        message = BakeNameless(message, "{TargetPropertyName}", _targetPropertyName);
        if (_target != null) return BakeName(message, "{TargetPropertyName}", _targetName);

        var value = _value.ToInvariantString();
        return value.Contains('{') ? message : message.Replace("{TargetValue}", value, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Validate(TProperty value, ValidationContext<TModel> context)
    {
        TProperty? targetValue;
        if (_target != null)
        {
            // A null on the way to the target skips the rule, as it does for the comparison rules.
            var accessed = _target(context.Model);
            if (!accessed.HasValue) return true;

            targetValue = accessed.Value;
        }
        else targetValue = _value;

        // The complement of Equal for every pair, null included, so the comparer decides for null here too.
        if (!_comparer.Equals(targetValue, value)) return true;

        if (!NeedsMessageParameters(context)) return false;

        context.MessageParameters.Add("{TargetValue}", targetValue.ToInvariantString());
        if (_targetPropertyName != null)
            context.MessageParameters.Add("{TargetPropertyName}", _targetName ?? _targetPropertyName);

        return false;
    }
}