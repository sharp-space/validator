using System.Globalization;

namespace Sharp.Validator.Validators;

/// <summary>Base for the string length rules, which only the library derives from. <c>null</c> is skipped.</summary>
public abstract class StringLengthValidator<TModel> : PropertyValidator<TModel, string?>
{
    private const string DynamicRangeMessage =
        "'{PropertyName}' must be between {MinLength} and {MaxLength} characters.";

    // Null for no upper bound, which only MinimumLength and the password length rule ask for.
    private readonly int? _maximum;
    private readonly Func<TModel, int>? _maximumFunc;

    private readonly int _minimum;
    private readonly Func<TModel, int>? _minimumFunc;

    /// <summary>Creates the validator with constant bounds; a <c>null</c> <paramref name="maximum" /> means no upper bound.</summary>
    private protected StringLengthValidator(int minimum, int? maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);
        if (maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum), maximum, $"'{nameof(maximum)}' must not be below '{nameof(minimum)}' ({minimum}).");
        }

        _minimum = minimum;
        _maximum = maximum;

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.Length;
            options.ErrorMessage = _maximum is null
                ? $"'{{PropertyName}}' must be at least {_minimum} characters."
                : $"'{{PropertyName}}' must be between {_minimum} and {_maximum} characters.";
        });
    }

    /// <summary>Creates the validator with both bounds resolved from the model at validation time.</summary>
    private protected StringLengthValidator(Func<TModel, int> minimum, Func<TModel, int> maximum)
    {
        _minimumFunc = minimum ?? throw new ArgumentNullException(nameof(minimum));
        _maximumFunc = maximum ?? throw new ArgumentNullException(nameof(maximum));

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.Length;
            options.ErrorMessage = DynamicRangeMessage;
        });
    }

    // Asymmetric forms used by Min/MaximumLengthValidator(Func): only one side is dynamic and the other is a constant,
    // or absent, so the placeholders report only the bounds the author set.
    /// <summary>Creates the validator with a minimum resolved from the model at validation time and no upper bound.</summary>
    private protected StringLengthValidator(Func<TModel, int> minimum) =>
        _minimumFunc = minimum ?? throw new ArgumentNullException(nameof(minimum));

    /// <summary>Creates the validator with a constant minimum and a maximum resolved from the model at validation time.</summary>
    private protected StringLengthValidator(int minimum, Func<TModel, int> maximum)
    {
        _minimum = minimum;
        _maximumFunc = maximum ?? throw new ArgumentNullException(nameof(maximum));
    }

    /// <inheritdoc />
    public override bool Validate(string? value, ValidationContext<TModel> context)
    {
        if (value is null) return true;

        var minimum = _minimumFunc?.Invoke(context.Model) ?? _minimum;

        // ExactLengthValidator(Func) passes the same delegate for both bounds — invoke it once, both to halve the cost
        // and so a side-effecting or non-deterministic bound can't disagree with itself.
        var maximum = _maximumFunc != null
            ? ReferenceEquals(_maximumFunc, _minimumFunc) ? minimum : _maximumFunc(context.Model)
            : _maximum;

        if (_minimumFunc != null || _maximumFunc != null) ThrowIfInvalid(minimum, maximum);

        var length = value.Length;
        if (length >= minimum && !(length > maximum)) return true;

        if (!NeedsMessageParameters(context)) return false;

        // Surface every bound the author set, so custom messages work for constant bounds too. MaximumLength's lower
        // bound of 0 is an implementation detail, not a bound.
        if (this is not MaximumLengthValidator<TModel>)
            context.MessageParameters.Add("{MinLength}", minimum.ToInvariantString());

        if (maximum is { } bound)
            context.MessageParameters.Add("{MaxLength}", bound.ToInvariantString());

        context.MessageParameters.Add("{Length}", length.ToInvariantString());

        return false;
    }

    // Bounds read from the model come from the validator's definition, not from the input it judges, so a negative or
    // reversed pair is a bug in the function that supplies them, as it is in a constant.
    private static void ThrowIfInvalid(int minimum, int? maximum)
    {
        if (minimum >= 0 && !(maximum < minimum)) return;

        throw new InvalidOperationException(
            $"The length bounds read from the model are invalid: minimum {minimum.ToString(CultureInfo.InvariantCulture)}, " +
            $"maximum {maximum?.ToString(CultureInfo.InvariantCulture) ?? "none"}. A bound must not be negative, and the " +
            "maximum must not be below the minimum.");
    }

    /// <summary>Returns <paramref name="value" />, throwing when it is negative, for a constructor's base call.</summary>
    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Global — the exception reports the name.
    private protected static int NotNegative(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        return value;
    }
}

/// <summary>String length must be between a minimum and a maximum (inclusive). <c>null</c> is skipped.</summary>
public sealed class LengthValidator<TModel> : StringLengthValidator<TModel>
{
    /// <summary>
    /// Creates the validator with constant bounds. Throws when a bound is negative or <paramref name="maximum" /> is below <paramref name="minimum" />; for
    /// no upper bound, use <see cref="MinimumLengthValidator{TModel}" />.
    /// </summary>
    public LengthValidator(int minimum, int maximum) : base(minimum, maximum)
    {
    }

    /// <summary>
    /// Creates the validator with both bounds resolved from the model at validation time. A validation that reads a negative bound, or a maximum below the
    /// minimum, throws <see cref="InvalidOperationException" />.
    /// </summary>
    public LengthValidator(Func<TModel, int> minimum, Func<TModel, int> maximum) : base(minimum, maximum)
    {
    }
}

/// <summary>String length must be exactly the given length. <c>null</c> is skipped.</summary>
public sealed class ExactLengthValidator<TModel> : StringLengthValidator<TModel>
{
    /// <summary>Creates the validator with a constant <paramref name="length" />, which must not be negative.</summary>
    public ExactLengthValidator(int length) : base(NotNegative(length, nameof(length)), length) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.ExactLength;
        options.ErrorMessage = $"'{{PropertyName}}' must be exactly {length} characters.";
    });

    /// <summary>Creates the validator with the length resolved from the model at validation time.</summary>
    public ExactLengthValidator(Func<TModel, int> func)
        : base(func ?? throw new ArgumentNullException(nameof(func)), func) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.ExactLength;
        options.ErrorMessage = "'{PropertyName}' must be exactly {MaxLength} characters.";
    });
}

/// <summary>String length must be at most the given maximum. <c>null</c> is skipped.</summary>
public sealed class MaximumLengthValidator<TModel> : StringLengthValidator<TModel>
{
    /// <summary>Creates the validator with a constant <paramref name="maximum" />, which must not be negative.</summary>
    public MaximumLengthValidator(int maximum) : base(0, NotNegative(maximum, nameof(maximum))) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.MaximumLength;
        options.ErrorMessage = $"'{{PropertyName}}' must be {maximum} characters or fewer.";
    });

    /// <summary>Creates the validator with the maximum resolved from the model at validation time.</summary>
    public MaximumLengthValidator(Func<TModel, int> maximum) : base(0, maximum) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.MaximumLength;
        options.ErrorMessage = "'{PropertyName}' must be {MaxLength} characters or fewer.";
    });
}

/// <summary>String length must be at least the given minimum. <c>null</c> is skipped.</summary>
public sealed class MinimumLengthValidator<TModel> : StringLengthValidator<TModel>
{
    /// <summary>Creates the validator with a constant <paramref name="minimum" />, which must not be negative.</summary>
    public MinimumLengthValidator(int minimum) : base(minimum, (int?)null) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.MinimumLength;
        options.ErrorMessage = $"'{{PropertyName}}' must be at least {minimum} characters.";
    });

    /// <summary>Creates the validator with the minimum resolved from the model at validation time.</summary>
    public MinimumLengthValidator(Func<TModel, int> minimum) : base(minimum) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.MinimumLength;
        options.ErrorMessage = "'{PropertyName}' must be at least {MinLength} characters.";
    });
}