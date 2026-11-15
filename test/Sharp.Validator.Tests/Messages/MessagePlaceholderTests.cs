namespace Sharp.Validator.Tests.Messages;

public sealed class MessagePlaceholderTests
{
    [Fact]
    public void RepeatedPropertyNamePlaceholder_IsReplacedEverywhere()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name, x => x.ErrorMessage = "{PropertyName} required; value of {PropertyName} missing")
            .NotEmpty());

        var result = validator.Validate(new Person { Name = null });
        Assert.False(result.IsValid);
        Assert.Equal("Name required; value of Name missing", result.Errors[0].Message);
    }

    [Fact]
    public void CustomMessageParameter_IsReplacedInAllPositions()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name, x => x.ErrorMessage = "{X} and {X}")
            .Must((_, context) =>
            {
                context.MessageParameters.Add("X", "v");
                return false;
            }));

        var result = validator.Validate(new Person { Name = "anything" });
        Assert.False(result.IsValid);
        Assert.Equal("v and v", result.Errors[0].Message);
    }

    [Fact]
    // Regression: {PropertyValue} is documented as always available, but a null value left the raw placeholder in the
    // message that a NotNull rule most needs it in.
    public void PropertyValue_RendersNullForANullValue()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .NotNull(x => x.ErrorMessage = "got {PropertyValue}"));

        Assert.Equal("got null", Assert.Single(validator.Validate(new Person()).Errors).Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FixedEqualityRules_AlwaysSupplyTargetValue(bool equal)
    {
        var validator = new Validator<Person>(builder =>
        {
            var property = builder.Property(m => m.Name);
            if (equal) property.Equal("target", x => x.ErrorMessage = "expected {TargetValue}");
            else property.NotEqual("target", x => x.ErrorMessage = "not {TargetValue}");
        });

        var model = new Person { Name = equal ? "wrong" : "target" };
        Assert.Contains("target", Assert.Single(validator.Validate(model).Errors).Message);
    }
}