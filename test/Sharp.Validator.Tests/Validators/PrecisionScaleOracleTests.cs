using System.Globalization;
using System.Runtime.InteropServices;

namespace Sharp.Validator.Tests.Validators;

// PrecisionScale counted digits by dividing a decimal in a loop, tens of nanoseconds per digit, and now counts
// them in integer arithmetic over the 96-bit mantissa. The decimal loop stays here as the oracle, fed the
// shapes that decide the outcome: powers of ten and their neighbors, the largest mantissa, zero at every scale,
// and long trailing-zero runs.
public sealed class PrecisionScaleOracleTests
{
    private static (int Scale, int Precision) Oracle(decimal value, bool ignoreTrailingZeros)
    {
        Span<int> intBits = stackalloc int[4];
        decimal.GetBits(value, intBits);
        var bits = MemoryMarshal.Cast<int, uint>(intBits);

        var unsignedScale = (bits[3] >> 16) & 31;
        var mantissa = (bits[2] * 4294967296m * 4294967296m) + (bits[1] * 4294967296m) + bits[0];

        var precision = (uint)0;
        for (var temporary = mantissa; temporary >= 1; temporary /= 10) precision++;

        var trailingZeros = (uint)0;
        if (ignoreTrailingZeros)
        {
            for (var temporary = mantissa; temporary % 10m == 0 && trailingZeros < unsignedScale; temporary /= 10)
                trailingZeros++;
        }

        return ((int)(unsignedScale - trailingZeros), (int)(precision - trailingZeros));
    }

    private static decimal Sample(Random random)
    {
        int low, middle = 0, high = 0;
        switch (random.Next(6))
        {
            case 0: low = random.Next(); break;
            case 1:
                low = random.Next();
                middle = random.Next();
                break;
            case 2:
                low = random.Next();
                middle = random.Next();
                high = random.Next();
                break;
            case 3:
                low = -1;
                middle = -1;
                high = -1;
                break;
            default:
            {
                var value = random.Next(2) == 0 ? 1m : random.Next(0, 1000);
                for (var exponent = random.Next(0, 25); exponent > 0; exponent--) value *= 10m;

                var bits = decimal.GetBits(Math.Max(value + random.Next(-1, 2), 0m));
                low = bits[0];
                middle = bits[1];
                high = bits[2];
                break;
            }
        }

        return new decimal(low, middle, high, random.Next(2) == 0, (byte)random.Next(0, 29));
    }

    [Fact]
    public void IntegerCounting_ReportsWhatTheDecimalLoopReported()
    {
        var random = new Random(12345);
        for (var iteration = 0; iteration < 50_000; iteration++)
        {
            var value = Sample(random);
            var precision = random.Next(1, 30);
            var scale = random.Next(0, precision + 1);
            var ignoreTrailingZeros = random.Next(2) == 0;

            var (actualScale, actualPrecision) = Oracle(value, ignoreTrailingZeros);
            var actualDigits = actualPrecision - actualScale;
            var expected = actualScale <= scale && actualDigits <= precision - scale;

            var result = new Validator<Person>(x => x.Property(person => person.Amount)
                    .PrecisionScale(precision, scale, ignoreTrailingZeros))
                .Validate(new Person { Amount = value });

            Assert.True(expected == result.IsValid, $"Disagreement on {value} with ({precision}, {scale}, {ignoreTrailingZeros}).");

            if (expected) continue;

            Assert.EndsWith($"It has {Math.Max(actualDigits, 0)} before and {actualScale} after.", result.Errors[0].Message);
        }
    }

    [Theory]
    [InlineData("79228162514264337593543950335", 0, 29)]
    [InlineData("10000000000000000000", 0, 20)]
    [InlineData("9999999999999999999", 0, 19)]
    [InlineData("0", 0, 0)]
    public void Digit_CountsAcrossTheMantissaLimbs(string value, int scale, int digits)
    {
        var result = new Validator<Person>(x => x.Property(person => person.Amount).PrecisionScale(digits == 0 ? 1 : digits, scale))
            .Validate(new Person { Amount = decimal.Parse(value, CultureInfo.InvariantCulture) });

        Assert.True(result.IsValid);
    }
}