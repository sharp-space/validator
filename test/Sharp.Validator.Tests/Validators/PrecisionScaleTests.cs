using System.Globalization;

namespace Sharp.Validator.Tests.Validators;

// The failure message used to report the value padded to the allowed decimals, as FluentValidation does: 1234 checked
// against (3, 2) was "6 digits and 2 decimals". It now states the limit and reports the digits the rule compared,
// before the decimal point without leading zeros and after it without the trailing zeros the rule ignores.
public sealed class PrecisionScaleTests
{
    [Theory]
    [InlineData("1234", 3, 2, false, 4, 0)]
    [InlineData("123", 3, 2, false, 3, 0)]
    [InlineData("1.234", 3, 2, false, 1, 3)]
    [InlineData("0.005", 3, 2, false, 0, 3)]
    [InlineData("12.340", 4, 2, false, 2, 3)]
    [InlineData("123.40", 4, 2, true, 3, 1)]
    public void Message_ReportsTheDigitsTheRuleCompared(
        string value,
        int precision,
        int scale,
        bool ignoreTrailingZeros,
        int before,
        int after
    )
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Amount)
            .PrecisionScale(precision, scale, ignoreTrailingZeros));

        var error = Assert.Single(validator.Validate(new Person { Amount = decimal.Parse(value, CultureInfo.InvariantCulture) }).Errors);

        Assert.Equal(
            $"'Amount' must fit {precision} digits with {scale} decimal places: at most {precision - scale} before the " +
            $"decimal point and {scale} after. It has {before} before and {after} after.",
            error.Message);
    }

    [Fact]
    public void PrecisionScale_NullableValueWithCustomMessage_ReadsTheSameCounts()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.NullableAmount)
            .PrecisionScale(4, 2, options => options.ErrorMessage = "{IntegerDigits}|{Scale}"));

        Assert.Equal("3|2", Assert.Single(validator.Validate(new Person { NullableAmount = 123.45m }).Errors).Message);
    }

    [Fact]
    public void PrecisionScale_Basic()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Amount).PrecisionScale(5, 2));

        // Precision 5 and scale 2 leave 3 digits before the decimal point: 123.45 passes, 1234.5 fails although it has
        // 5 digits in all, since 4 come before the point, and 1.234 fails on its 3 decimals.
        Assert.True(validator.Validate(new Person { Amount = 123.45m }).IsValid);
        Assert.False(validator.Validate(new Person { Amount = 1234.5m }).IsValid);
        Assert.False(validator.Validate(new Person { Amount = 1.234m }).IsValid);
    }

    [Fact]
    public void PrecisionScale_IgnoreTrailingZeros()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Amount)
            .PrecisionScale(4, 2, true));

        // 12.30 with trailing zero ignored => effective scale 1, precision 3 → passes (4,2)
        Assert.True(validator.Validate(new Person { Amount = 12.30m }).IsValid);

        // 12.34 → precision 4, scale 2 → exactly the limit → passes
        Assert.True(validator.Validate(new Person { Amount = 12.34m }).IsValid);

        // 123.4 → precision 4, scale 1 → integer digits 3, only 2 allowed → fails
        Assert.False(validator.Validate(new Person { Amount = 123.4m }).IsValid);
    }

    [Fact]
    public void PrecisionScale_NullableDecimal()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.NullableAmount).PrecisionScale(4, 2));
        Assert.True(validator.Validate(new Person { NullableAmount = null }).IsValid);
        Assert.True(validator.Validate(new Person { NullableAmount = 12.34m }).IsValid);
        Assert.False(validator.Validate(new Person { NullableAmount = 123.45m }).IsValid);
        Assert.False(validator.Validate(new Person { NullableAmount = 1.234m }).IsValid); // scale 3 > 2
    }

    [Fact]
    public void PrecisionScale_InvalidArgs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator<Person>(x => x.Property(person => person.Amount).PrecisionScale(5, -1)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator<Person>(x => x.Property(person => person.Amount).PrecisionScale(-1, 2)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator<Person>(x => x.Property(person => person.Amount).PrecisionScale(2, 5)));
    }

    [Fact]
    // NullablePrecisionScaleValidator ctor argument guards on a decimal? property.
    public void PrecisionScale_NullableInvalidArgs_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator<Person>(x => x.Property(person => person.NullableAmount).PrecisionScale(5, -1))); // scale < 0

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator<Person>(x => x.Property(person => person.NullableAmount).PrecisionScale(-1, 2))); // precision < 0

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator<Person>(x => x.Property(person => person.NullableAmount).PrecisionScale(2, 5))); // precision < scale
    }

    [Fact]
    // The limits are baked into the template when the rule freezes; a rule whose template was cleared still freezes and
    // reports an empty message.
    public void ClearedTemplate_ReportsAnEmptyMessage()
    {
        var rule = new PrecisionScaleValidator<Person>(4, 2, false);
        rule.Options!.ErrorMessage = null;
        var validator = new Validator<Person>(x => x.Property(person => person.Amount).Add(rule));
        Assert.Equal(string.Empty, Assert.Single(validator.Validate(new Person { Amount = 123.456m }).Errors).Message);
    }
}