using System.Buffers;

namespace Sharp.Validator.Validators;

/// <summary>
/// Validates the WHATWG HTML5 "valid email address" syntax — the same check browsers apply to <c>input[type=email]</c>. ASCII-only: RFC 5322 local-part
/// specials and hyphenated (punycode) domain labels are accepted; internationalized addresses must use their punycode domain form.
/// </summary>
public sealed class EmailValidator<TModel> : PropertyValidator<TModel, string?>
{
    /// <summary>Creates the validator.</summary>
    public EmailValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.Email;
        options.ErrorMessage = "'{PropertyName}' is not a valid email address.";
    });

    /// <inheritdoc />
    public override bool Validate(string? value, ValidationContext<TModel> context) =>
        string.IsNullOrEmpty(value) || EmailSyntax.IsValid(value);
}

/// <summary>
/// The WHATWG HTML5 pattern (https://html.spec.whatwg.org/#valid-e-mail-address) as a scanner:
/// <c>^[a-zA-Z0-9.!#$%&amp;'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*\z</c>.
/// Neither character class contains the delimiter that ends it, so a match is unique and needs no backtracking.
/// </summary>
file static class EmailSyntax
{
    private const int MaximumLabelLength = 63;

    private static readonly SearchValues<char> LocalPartCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.!#$%&'*+/=?^_`{|}~-");

    private static readonly SearchValues<char> LabelCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-");

    internal static bool IsValid(ReadOnlySpan<char> value)
    {
        var at = value.IndexOfAnyExcept(LocalPartCharacters);
        if (at <= 0 || value[at] != '@') return false;

        var domain = value[(at + 1)..];
        while (true)
        {
            var end = domain.IndexOfAnyExcept(LabelCharacters);
            var label = end < 0 ? domain : domain[..end];
            if (label.Length is 0 or > MaximumLabelLength || label[0] == '-' || label[^1] == '-') return false;

            if (end < 0) return true;
            if (domain[end] != '.') return false;

            domain = domain[(end + 1)..];
        }
    }
}