using System.Globalization;

namespace Sharp.Validator.Tests.Messages;

[Collection(GlobalOptions.Name)]
// Placeholder values in error messages are formatted with the invariant culture. Previously the values were baked
// (Range/Equal) or stringified (Comparison) under the current culture, so a singleton validator constructed during a
// de-DE request would render "1,5" in every message forever after.
public sealed class InvariantMessageFormatTests
{
    [Fact]
    public void RangeMessage_BakesDecimalBoundsInvariantly()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var validator = new Validator<Person>(x => x.Property(person => person.Amount).InclusiveBetween(1.5m, 2.5m));
            var message = validator.Validate(new Person { Amount = 9m }).Errors[0].Message!;
            Assert.Contains("1.5", message);
            Assert.DoesNotContain("1,5", message);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ComparisonTargetPlaceholder_FormatsInvariantly()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var validator = new Validator<Person>(x =>
                x.Property(person => person.Amount).GreaterThan(person => person.NullableAmount!.Value));

            var message = validator.Validate(new Person { Amount = 1m, NullableAmount = 2.5m }).Errors[0].Message!;
            Assert.Contains("2.5", message);
            Assert.DoesNotContain("2,5", message);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void PropertyValuePlaceholder_FormatsInvariantly()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var validator = new Validator<Person>(x => x.Property(m => m.Amount)
                .Must(_ => false, options => options.ErrorMessage = "value={PropertyValue}"));

            Assert.Equal("value=1234.5", Assert.Single(
                validator.Validate(new Person { Amount = 1234.5m }).Errors).Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}