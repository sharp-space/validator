using System.Numerics;
using System.Runtime.InteropServices;

namespace Sharp.Validator.Validators;

/// <summary>Validates that a decimal has at most <c>precision</c> total digits with at most <c>scale</c> of them after the decimal point.</summary>
public sealed class PrecisionScaleValidator<TModel> : PropertyValidator<TModel, decimal>
{
    private readonly bool _ignoreTrailingZeros;
    private readonly int _precision;

    private readonly int _scale;

    /// <summary>
    /// Creates the validator. Throws when <paramref name="precision" /> or <paramref name="scale" /> is negative, or when <paramref name="scale" /> exceeds
    /// <paramref name="precision" />.
    /// </summary>
    public PrecisionScaleValidator(int precision, int scale, bool ignoreTrailingZeros)
    {
        PrecisionMath.ThrowIfInvalid(precision, scale);

        _precision = precision;
        _scale = scale;
        _ignoreTrailingZeros = ignoreTrailingZeros;

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.PrecisionScale;
            options.ErrorMessage = PrecisionMath.Message(precision, scale);
        });
    }

    private protected override string? BakeMessage(string? message) => PrecisionMath.BakeLimits(message, _precision, _scale);

    /// <inheritdoc />
    public override bool Validate(decimal value, ValidationContext<TModel> context)
    {
        if (PrecisionMath.Fits(value, _precision, _scale, _ignoreTrailingZeros, out var integerDigits, out var decimals)) return true;

        if (NeedsMessageParameters(context)) PrecisionMath.AddParameters(context, integerDigits, decimals);

        return false;
    }
}

/// <summary>Nullable variant of <see cref="PrecisionScaleValidator{TModel}" />. <c>null</c> is skipped.</summary>
internal sealed class NullablePrecisionScaleValidator<TModel> : PropertyValidator<TModel, decimal?>
{
    private readonly bool _ignoreTrailingZeros;
    private readonly int _precision;
    private readonly int _scale;

    /// <inheritdoc cref="PrecisionScaleValidator{TModel}(int, int, bool)" />
    public NullablePrecisionScaleValidator(int precision, int scale, bool ignoreTrailingZeros)
    {
        PrecisionMath.ThrowIfInvalid(precision, scale);

        _precision = precision;
        _scale = scale;
        _ignoreTrailingZeros = ignoreTrailingZeros;

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.PrecisionScale;
            options.ErrorMessage = PrecisionMath.Message(precision, scale);
        });
    }

    private protected override string? BakeMessage(string? message) => PrecisionMath.BakeLimits(message, _precision, _scale);

    /// <inheritdoc />
    public override bool Validate(decimal? value, ValidationContext<TModel> context)
    {
        if (!value.HasValue ||
            PrecisionMath.Fits(value.Value, _precision, _scale, _ignoreTrailingZeros, out var integerDigits, out var decimals))
            return true;

        if (NeedsMessageParameters(context)) PrecisionMath.AddParameters(context, integerDigits, decimals);

        return false;
    }
}

file static class PrecisionMath
{
    private static ReadOnlySpan<ulong> PowersOfTen =>
    [
        1, 10, 100, 1_000, 10_000, 100_000, 1_000_000, 10_000_000, 100_000_000, 1_000_000_000, 10_000_000_000,
        100_000_000_000, 1_000_000_000_000, 10_000_000_000_000, 100_000_000_000_000, 1_000_000_000_000_000,
        10_000_000_000_000_000, 100_000_000_000_000_000, 1_000_000_000_000_000_000, 10_000_000_000_000_000_000
    ];

    internal static void ThrowIfInvalid(int precision, int scale)
    {
        if (scale < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), $"Scale must be a positive integer. (value: {scale}).");
        }

        if (precision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(precision), $"Precision must be a positive integer. (value: {precision}).");
        }

        if (precision < scale)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), $"Scale must be less than or equal to precision. (scale: {scale}, precision: {precision}).");
        }
    }

    // The message states the limit as digits before and after the decimal point, and reports the value's own counts
    // rather than the value padded to the allowed decimals, as FluentValidation reports them.
    internal static string Message(int precision, int scale) =>
        $"'{{PropertyName}}' must fit {precision} digits with {scale} decimal places: at most {precision - scale} before " +
        $"the decimal point and {scale} after. It has {{IntegerDigits}} before and {{Scale}} after.";

    // The counts the verdict compares: digits before the decimal point without leading zeros, so 0.05 has none, and
    // digits after it without the trailing zeros the rule ignores.
    internal static bool Fits(
        decimal value,
        int precision,
        int scale,
        bool ignoreTrailingZeros,
        out int integerDigits,
        out int decimals
    )
    {
        var (actualScale, actualPrecision) = ScaleAndPrecision(value, ignoreTrailingZeros);
        integerDigits = Math.Max(actualPrecision - actualScale, 0);
        decimals = actualScale;
        return decimals <= scale && integerDigits <= precision - scale;
    }

    // The limits are fixed for the rule's lifetime, so they render into the template when it freezes.
    internal static string? BakeLimits(string? message, int precision, int scale) =>
        message?.Replace("{ExpectedPrecision}", precision.ToInvariantString(), StringComparison.Ordinal)
            .Replace("{ExpectedScale}", scale.ToInvariantString(), StringComparison.Ordinal);

    internal static void AddParameters<TModel>(ValidationContext<TModel> context, int integerDigits, int decimals)
    {
        context.MessageParameters.Add("{IntegerDigits}", integerDigits.ToInvariantString());
        context.MessageParameters.Add("{Scale}", decimals.ToInvariantString());
    }

    /// <summary>
    /// Derives scale, precision (significant-digit count) and the trailing-zero count from a single read of the decimal's bits, in integer arithmetic.
    /// <see cref="decimal" /> division costs tens of nanoseconds per digit, which made this rule scale linearly with the value's magnitude.
    /// </summary>
    private static (int Scale, int Precision) ScaleAndPrecision(decimal value, bool ignoreTrailingZeros)
    {
        Span<int> intBits = stackalloc int[4];
        decimal.GetBits(value, intBits);
        var bits = MemoryMarshal.Cast<int, uint>(intBits);

        var scale = (int)((bits[3] >> 16) & 31);
        var low = ((ulong)bits[1] << 32) | bits[0];
        var high = bits[2];

        var precision = CountDigits(high, low);
        if (!ignoreTrailingZeros) return (scale, precision);

        var trailingZeros = 0;
        while (trailingZeros < scale && IsDivisibleByTen(high, low))
        {
            DivideByTen(ref high, ref low);
            trailingZeros++;
        }

        return (scale - trailingZeros, precision - trailingZeros);
    }

    private static int CountDigits(uint high, ulong low)
    {
        if (high == 0) return low == 0 ? 0 : CountDigits(low);

        // The mantissa is below 2^96 < 10^29, so at most nine digits sit above 10^20.
        var value = ((UInt128)high << 64) | low;
        var digits = 20;
        var bound = (UInt128)10_000_000_000_000_000_000UL * 10;
        while (value >= bound && digits < 29)
        {
            digits++;
            bound *= 10;
        }

        return digits;
    }

    private static int CountDigits(ulong value)
    {
        // log10(x) estimated from the bit length (1233/4096 ~ log10(2)), then corrected by one table compare.
        var estimate = ((64 - BitOperations.LeadingZeroCount(value)) * 1233) >> 12;
        return estimate + (value >= PowersOfTen[estimate] ? 1 : 0);
    }

    private static bool IsDivisibleByTen(uint high, ulong low)
    {
        if ((low & 1) != 0) return false;

        // 2^64 % 5 == 1, 2^32 % 5 == 1: the limbs add up modulo 5.
        var sum = (ulong)high + (uint)(low >> 32) + (uint)low;
        return sum % 5 == 0;
    }

    private static void DivideByTen(ref uint high, ref ulong low)
    {
        var limbTwo = high;
        var limbOne = (uint)(low >> 32);
        var limbZero = (uint)low;

        var remainder = limbTwo % 10;
        high = limbTwo / 10;

        var current = ((ulong)remainder << 32) | limbOne;
        var one = (uint)(current / 10);
        remainder = (uint)(current % 10);

        current = ((ulong)remainder << 32) | limbZero;
        var zero = (uint)(current / 10);

        low = ((ulong)one << 32) | zero;
    }
}