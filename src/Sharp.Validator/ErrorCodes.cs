namespace Sharp.Validator;

/// <summary>
/// The error code each built-in rule reports, named after the rule; an async rule reports the code of its sync form. Every message for a code can use
/// <c>{PropertyName}</c> and <c>{PropertyValue}</c> besides the placeholders listed on the code, and every form of the rule fills them all. Clients
/// branch on these values and <see cref="ValidatorBuilderOptions.ErrorMessages" /> is keyed by them, so they don't change.
/// </summary>
public static class ErrorCodes
{
    /// <summary>Reported by <c>NotNull</c>.</summary>
    public const string NotNull = nameof(NotNull);

    /// <summary>Reported by <c>Null</c>.</summary>
    public const string Null = nameof(Null);

    /// <summary>Reported by <c>NotEmpty</c>.</summary>
    public const string NotEmpty = nameof(NotEmpty);

    /// <summary>Reported by <c>Empty</c>.</summary>
    public const string Empty = nameof(Empty);

    /// <summary>Reported by <c>Equal</c>, with <c>{TargetValue}</c> and <c>{TargetPropertyName}</c>.</summary>
    public const string Equal = nameof(Equal);

    /// <summary>Reported by <c>NotEqual</c>, with <c>{TargetValue}</c> and <c>{TargetPropertyName}</c>.</summary>
    public const string NotEqual = nameof(NotEqual);

    /// <summary>Reported by <c>GreaterThan</c>, with <c>{TargetValue}</c> and <c>{TargetPropertyName}</c>.</summary>
    public const string GreaterThan = nameof(GreaterThan);

    /// <summary>Reported by <c>GreaterThanOrEqual</c>, with <c>{TargetValue}</c> and <c>{TargetPropertyName}</c>.</summary>
    public const string GreaterThanOrEqual = nameof(GreaterThanOrEqual);

    /// <summary>Reported by <c>LessThan</c>, with <c>{TargetValue}</c> and <c>{TargetPropertyName}</c>.</summary>
    public const string LessThan = nameof(LessThan);

    /// <summary>Reported by <c>LessThanOrEqual</c>, with <c>{TargetValue}</c> and <c>{TargetPropertyName}</c>.</summary>
    public const string LessThanOrEqual = nameof(LessThanOrEqual);

    /// <summary>Reported by <c>InclusiveBetween</c>, with <c>{From}</c>, <c>{To}</c>, <c>{FromPropertyName}</c>, and <c>{ToPropertyName}</c>.</summary>
    public const string InclusiveBetween = nameof(InclusiveBetween);

    /// <summary>Reported by <c>ExclusiveBetween</c>, with <c>{From}</c>, <c>{To}</c>, <c>{FromPropertyName}</c>, and <c>{ToPropertyName}</c>.</summary>
    public const string ExclusiveBetween = nameof(ExclusiveBetween);

    /// <summary>Reported by <c>InEnum</c>, on an enum or a string property.</summary>
    public const string InEnum = nameof(InEnum);

    /// <summary>Reported by <c>Length(min, max)</c>, with <c>{MinLength}</c>, <c>{MaxLength}</c>, and <c>{Length}</c>.</summary>
    public const string Length = nameof(Length);

    /// <summary>Reported by <c>Length(exact)</c>, with <c>{MinLength}</c>, <c>{MaxLength}</c>, and <c>{Length}</c>.</summary>
    public const string ExactLength = nameof(ExactLength);

    /// <summary>Reported by <c>MaximumLength</c>, with <c>{MaxLength}</c> and <c>{Length}</c>.</summary>
    public const string MaximumLength = nameof(MaximumLength);

    /// <summary>Reported by <c>MinimumLength</c>, with <c>{MinLength}</c> and <c>{Length}</c>.</summary>
    public const string MinimumLength = nameof(MinimumLength);

    /// <summary>Reported by <c>Regex</c>, with <c>{RegularExpression}</c>.</summary>
    public const string Regex = nameof(Regex);

    /// <summary>Reported by <c>Email</c>.</summary>
    public const string Email = nameof(Email);

    /// <summary>Reported by <c>CreditCard</c>.</summary>
    public const string CreditCard = nameof(CreditCard);

    /// <summary>Reported by <c>PhoneNumber</c>.</summary>
    public const string PhoneNumber = nameof(PhoneNumber);

    /// <summary>Reported by <c>Uri</c>.</summary>
    public const string Uri = nameof(Uri);

    /// <summary>Reported by <c>Url</c>.</summary>
    public const string Url = nameof(Url);

    /// <summary>Reported by <c>Must</c> and <c>MustAsync</c>.</summary>
    public const string Must = nameof(Must);

    /// <summary>
    /// Reported by <c>PrecisionScale</c>, with <c>{IntegerDigits}</c> and <c>{Scale}</c> for the value, and <c>{ExpectedPrecision}</c> and
    /// <c>{ExpectedScale}</c> for the limits.
    /// </summary>
    public const string PrecisionScale = nameof(PrecisionScale);

    /// <summary>Reported by <c>InPast</c>.</summary>
    public const string InPast = nameof(InPast);

    /// <summary>Reported by <c>InFuture</c>.</summary>
    public const string InFuture = nameof(InFuture);

    /// <summary>Reported by <c>IsToday</c>.</summary>
    public const string IsToday = nameof(IsToday);

    /// <summary>Reported by <c>Password</c> for its required length, with <c>{MinLength}</c> and <c>{Length}</c>.</summary>
    public const string PasswordLength = nameof(PasswordLength);

    /// <summary>Reported by <c>Password</c> for its required digits, with <c>{RequiredCount}</c>.</summary>
    public const string PasswordDigits = nameof(PasswordDigits);

    /// <summary>Reported by <c>Password</c> for its required non-alphanumeric characters, with <c>{RequiredCount}</c>.</summary>
    public const string PasswordNonAlphanumerics = nameof(PasswordNonAlphanumerics);

    /// <summary>Reported by <c>Password</c> for its required uppercase letters, with <c>{RequiredCount}</c>.</summary>
    public const string PasswordUppercaseLetters = nameof(PasswordUppercaseLetters);

    /// <summary>Reported by <c>Password</c> for its required lowercase letters, with <c>{RequiredCount}</c>.</summary>
    public const string PasswordLowercaseLetters = nameof(PasswordLowercaseLetters);

    /// <summary>Reported by <c>Password</c> for its required distinct characters, with <c>{RequiredCount}</c>.</summary>
    public const string PasswordUniqueCharacters = nameof(PasswordUniqueCharacters);
}