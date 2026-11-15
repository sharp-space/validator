using static Sharp.Validator.Validators.PasswordValidatorHelpers;

namespace Sharp.Validator.Validators;

/// <summary>Password must be at least the required length. A minimum-length rule with password-specific error code and message.</summary>
internal sealed class PasswordLengthValidator<TModel> : StringLengthValidator<TModel>
{
    /// <summary>Creates the validator requiring at least <paramref name="length" /> characters.</summary>
    public PasswordLengthValidator(int length) : base(NotNegative(length, nameof(length)), (int?)null) => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.PasswordLength;
        options.ErrorMessage = $"'{{PropertyName}}' must be at least {length} character{(length == 1 ? string.Empty : "s")}.";
    });
}

/// <summary>
/// Password must contain a minimum number of digits, counted with <see cref="char.IsDigit(char)" /> (Unicode classification, not ASCII-only).
/// <c>null</c>/empty is skipped.
/// </summary>
internal sealed class PasswordDigitCountValidator<TModel> : PropertyValidator<TModel, string?>
{
    private readonly int _count;

    /// <summary>Creates the validator requiring at least <paramref name="count" /> digits.</summary>
    public PasswordDigitCountValidator(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _count = count;

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.PasswordDigits;
            options.ErrorMessage = $"'{{PropertyName}}' must contain at least {count} digit{(count == 1 ? string.Empty : "s")}.";
        });
    }

    private protected override string? BakeMessage(string? message) => BakeRequiredCount(message, _count);

    /// <inheritdoc />
    public override bool Validate(string? value, ValidationContext<TModel> context) =>
        string.IsNullOrEmpty(value) || HasAtLeast<DigitCharacter>(value, _count);
}

/// <summary>
/// Password must contain a minimum number of non-alphanumeric characters, counted as those failing <see cref="char.IsLetterOrDigit(char)" /> (Unicode
/// classification, not ASCII-only). <c>null</c>/empty is skipped.
/// </summary>
internal sealed class PasswordNonAlphanumericCountValidator<TModel> : PropertyValidator<TModel, string?>
{
    private readonly int _count;

    /// <summary>Creates the validator requiring at least <paramref name="count" /> non-alphanumeric characters.</summary>
    public PasswordNonAlphanumericCountValidator(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _count = count;

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.PasswordNonAlphanumerics;
            options.ErrorMessage = $"'{{PropertyName}}' must contain at least {count} non-alphanumeric character{(count == 1 ? string.Empty : "s")}.";
        });
    }

    private protected override string? BakeMessage(string? message) => BakeRequiredCount(message, _count);

    /// <inheritdoc />
    public override bool Validate(string? value, ValidationContext<TModel> context) =>
        string.IsNullOrEmpty(value) || HasAtLeast<NonAlphanumericCharacter>(value, _count);
}

/// <summary>
/// Password must contain a minimum number of uppercase letters, counted with <see cref="char.IsUpper(char)" /> (Unicode classification, not ASCII-only).
/// <c>null</c>/empty is skipped.
/// </summary>
internal sealed class PasswordUppercaseCountValidator<TModel> : PropertyValidator<TModel, string?>
{
    private readonly int _count;

    /// <summary>Creates the validator requiring at least <paramref name="count" /> uppercase letters.</summary>
    public PasswordUppercaseCountValidator(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _count = count;

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.PasswordUppercaseLetters;
            options.ErrorMessage = $"'{{PropertyName}}' must contain at least {count} uppercase letter{(count == 1 ? string.Empty : "s")}.";
        });
    }

    private protected override string? BakeMessage(string? message) => BakeRequiredCount(message, _count);

    /// <inheritdoc />
    public override bool Validate(string? value, ValidationContext<TModel> context) =>
        string.IsNullOrEmpty(value) || HasAtLeast<UppercaseCharacter>(value, _count);
}

/// <summary>
/// Password must contain a minimum number of lowercase letters, counted with <see cref="char.IsLower(char)" /> (Unicode classification, not ASCII-only).
/// <c>null</c>/empty is skipped.
/// </summary>
internal sealed class PasswordLowercaseCountValidator<TModel> : PropertyValidator<TModel, string?>
{
    private readonly int _count;

    /// <summary>Creates the validator requiring at least <paramref name="count" /> lowercase letters.</summary>
    public PasswordLowercaseCountValidator(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _count = count;

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.PasswordLowercaseLetters;
            options.ErrorMessage = $"'{{PropertyName}}' must contain at least {count} lowercase letter{(count == 1 ? string.Empty : "s")}.";
        });
    }

    private protected override string? BakeMessage(string? message) => BakeRequiredCount(message, _count);

    /// <inheritdoc />
    public override bool Validate(string? value, ValidationContext<TModel> context) =>
        string.IsNullOrEmpty(value) || HasAtLeast<LowercaseCharacter>(value, _count);
}

/// <summary>Password must contain a minimum number of unique characters (distinct <see cref="char" /> values). <c>null</c>/empty is skipped.</summary>
internal sealed class PasswordUniqueCharacterCountValidator<TModel> : PropertyValidator<TModel, string?>
{
    private readonly int _count;

    /// <summary>Creates the validator requiring at least <paramref name="count" /> unique characters.</summary>
    public PasswordUniqueCharacterCountValidator(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _count = count;

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.PasswordUniqueCharacters;
            options.ErrorMessage = $"'{{PropertyName}}' must contain at least {count} unique character{(count == 1 ? string.Empty : "s")}.";
        });
    }

    private protected override string? BakeMessage(string? message) => BakeRequiredCount(message, _count);

    /// <inheritdoc />
    public override bool Validate(string? value, ValidationContext<TModel> context)
    {
        if (string.IsNullOrEmpty(value) || _count <= 0) return true;
        if (value.Length < _count) return false;

        // Track distinct characters on the stack and stop as soon as the threshold is reached. A realistic
        // RequiredUniqueCharacters is tiny, so the linear membership scan beats allocating a HashSet per call.
        if (_count <= 128)
        {
            Span<char> seen = stackalloc char[_count];
            var distinct = 0;
            foreach (var character in value)
            {
                var alreadySeen = false;
                for (var index = 0; index < distinct; index++)
                    if (seen[index] == character)
                    {
                        alreadySeen = true;
                        break;
                    }

                if (alreadySeen) continue;

                seen[distinct++] = character;
                if (distinct >= _count) return true;
            }

            return false;
        }

        // Extreme requirement: fall back to a set rather than over-allocate the stack.
        var set = new HashSet<char>(_count);
        foreach (var character in value)
            if (set.Add(character) && set.Count >= _count)
                return true;

        return false;
    }
}

internal static class PasswordValidatorHelpers
{
    // A count rule's required count is fixed for its lifetime, so it renders into the template when the rule freezes.
    internal static string? BakeRequiredCount(string? message, int count) =>
        message?.Replace("{RequiredCount}", count.ToInvariantString(), StringComparison.Ordinal);

    // A struct type argument specializes the loop per class (no delegate per character), and the count stops at the
    // threshold instead of always walking the whole password.
    public static bool HasAtLeast<TClass>(string value, int required) where TClass : struct, ICharacterClass
    {
        if (required <= 0) return true;

        var count = 0;
        foreach (var character in value)
            if (TClass.Matches(character) && ++count >= required)
                return true;

        return false;
    }

    internal interface ICharacterClass
    {
        static abstract bool Matches(char character);
    }

    internal readonly struct DigitCharacter : ICharacterClass
    {
        public static bool Matches(char character) => char.IsDigit(character);
    }

    internal readonly struct NonAlphanumericCharacter : ICharacterClass
    {
        public static bool Matches(char character) => !char.IsLetterOrDigit(character);
    }

    internal readonly struct UppercaseCharacter : ICharacterClass
    {
        public static bool Matches(char character) => char.IsUpper(character);
    }

    internal readonly struct LowercaseCharacter : ICharacterClass
    {
        public static bool Matches(char character) => char.IsLower(character);
    }
}

/// <summary>
/// Knobs for the <c>.Password(...)</c> rule. By default only the length counts, as NIST SP 800-63B advises; set a count above <c>0</c> to require that
/// kind of character, and a negative count throws when it is set.
/// </summary>
public sealed class PasswordValidatorOptions
{
    private bool _readOnly;

    /// <summary>Minimum length. Default 8.</summary>
    public int RequiredLength
    {
        get;
        set => field = NotNegative(value);
    } = 8;

    /// <summary>Minimum number of digit characters. Default 0 (disabled).</summary>
    public int RequiredDigits
    {
        get;
        set => field = NotNegative(value);
    }

    /// <summary>Minimum number of non-alphanumeric characters. Default 0 (disabled).</summary>
    public int RequiredNonAlphanumerics
    {
        get;
        set => field = NotNegative(value);
    }

    /// <summary>Minimum number of uppercase letters. Default 0 (disabled).</summary>
    public int RequiredUppercaseLetters
    {
        get;
        set => field = NotNegative(value);
    }

    /// <summary>Minimum number of lowercase letters. Default 0 (disabled).</summary>
    public int RequiredLowercaseLetters
    {
        get;
        set => field = NotNegative(value);
    }

    /// <summary>Minimum number of distinct characters. Default 0 (disabled).</summary>
    public int RequiredUniqueCharacters
    {
        get;
        set => field = NotNegative(value);
    }

    private int NotNegative(int value)
    {
        if (_readOnly)
            throw new InvalidOperationException(ReadOnlyOptions.ReadWhenTheCallbackReturned);

        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return value;
    }

    internal void MakeReadOnly() => _readOnly = true;
}