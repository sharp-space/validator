using System.Buffers;

namespace Sharp.Validator.Validators;

/// <summary>
/// Value must contain at least one digit and only digits, whitespace, or <c>-.()+</c>; a trailing <c>x</c>/<c>ext</c>/<c>ext.</c> extension is allowed.
/// </summary>
public sealed class PhoneNumberValidator<TModel> : PropertyValidator<TModel, string?>
{
    /// <summary>Creates the validator.</summary>
    public PhoneNumberValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.PhoneNumber;
        options.ErrorMessage = "'{PropertyName}' is not a valid phone number.";
    });

    /// <inheritdoc />
    public override bool Validate(string? value, ValidationContext<TModel> context) =>
        string.IsNullOrEmpty(value) || PhoneNumberSyntax.IsValid(value);
}

// Outside the generic validator so the search sets exist once, not once per closed model type.
file static class PhoneNumberSyntax
{
    private const string ExtensionAbbreviationX = "x";
    private const string ExtensionAbbreviationExt = "ext";
    private const string ExtensionAbbreviationExtDot = "ext.";

    // Every allowed ASCII character. Whatever falls outside still gets the Unicode whitespace check.
    private static readonly SearchValues<char> PhoneCharacters = SearchValues.Create("0123456789-.()+ \t\n\v\f\r");

    // The first letter of each extension form, in both cases. No other character case-folds to these.
    private static readonly SearchValues<char> ExtensionStart = SearchValues.Create("xXeE");

    internal static bool IsValid(ReadOnlySpan<char> value)
    {
        var span = RemoveExtension(value.TrimEnd());
        if (span.IndexOfAnyInRange('0', '9') < 0) return false;

        var invalid = span.IndexOfAnyExcept(PhoneCharacters);
        while (invalid >= 0)
        {
            if (!char.IsWhiteSpace(span[invalid])) return false;

            span = span[(invalid + 1)..];
            invalid = span.IndexOfAnyExcept(PhoneCharacters);
        }

        return true;
    }

    private static ReadOnlySpan<char> RemoveExtension(ReadOnlySpan<char> span)
    {
        // Most numbers have no extension, and the three case-insensitive reverse searches below cost far
        // more than this.
        if (span.IndexOfAny(ExtensionStart) < 0) return span;

        var lastIndex = span.LastIndexOf(ExtensionAbbreviationExtDot, StringComparison.OrdinalIgnoreCase);
        if (lastIndex >= 0)
        {
            var extension = span[(lastIndex + ExtensionAbbreviationExtDot.Length)..];
            if (MatchesExtension(extension)) return span[..lastIndex];
        }

        lastIndex = span.LastIndexOf(ExtensionAbbreviationExt, StringComparison.OrdinalIgnoreCase);
        if (lastIndex >= 0)
        {
            var extension = span[(lastIndex + ExtensionAbbreviationExt.Length)..];
            if (MatchesExtension(extension)) return span[..lastIndex];
        }

        lastIndex = span.LastIndexOf(ExtensionAbbreviationX, StringComparison.OrdinalIgnoreCase);
        if (lastIndex >= 0)
        {
            var extension = span[(lastIndex + ExtensionAbbreviationX.Length)..];
            if (MatchesExtension(extension)) return span[..lastIndex];
        }

        return span;
    }

    private static bool MatchesExtension(ReadOnlySpan<char> span)
    {
        span = span.TrimStart();
        if (span.Length == 0) return false;

        foreach (var character in span)
            if (!char.IsAsciiDigit(character))
                return false;

        return true;
    }
}