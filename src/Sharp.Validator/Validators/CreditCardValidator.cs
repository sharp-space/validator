namespace Sharp.Validator.Validators;

/// <summary>Value must be a Luhn-valid credit card number of 12 to 19 digits; spaces and dashes are ignored.</summary>
public sealed class CreditCardValidator<TModel> : PropertyValidator<TModel, string?>
{
    // Most issuers fall in [12, 19] digits; ISO/IEC 7812 allows up to 19. We accept >= 12 to cover Maestro/UnionPay
    // short forms while still rejecting trivially short input like "0".
    private const int MinimumDigits = 12;
    private const int MaximumDigits = 19;

    /// <summary>Creates the validator.</summary>
    public CreditCardValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.CreditCard;
        options.ErrorMessage = "'{PropertyName}' is not a valid credit card number.";
    });

    /// <inheritdoc />
    public override bool Validate(string? value, ValidationContext<TModel> context)
    {
        if (string.IsNullOrEmpty(value)) return true;

        var checksum = 0;
        var evenDigit = false;
        var digitCount = 0;
        for (var index = value.Length - 1; index >= 0; index--)
        {
            var character = value[index];
            if (character == '-' || character == ' ') continue;

            // ASCII only: char.IsDigit accepts Unicode decimal digits (e.g. U+0663), whose code points would corrupt
            // the `character - '0'` arithmetic below.
            if (!char.IsAsciiDigit(character)) return false;

            var digitValue = (character - '0') * (evenDigit ? 2 : 1);
            evenDigit = !evenDigit;
            digitCount++;
            if (digitCount > MaximumDigits) return false;

            checksum += (digitValue / 10) + (digitValue % 10);
        }

        return digitCount >= MinimumDigits && checksum % 10 == 0;
    }
}