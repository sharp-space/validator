namespace Sharp.Validator.Tests.Messages;

// Regression: placeholders were replaced one after another over the growing message, so user input substituted for
// {PropertyValue} was scanned again. A value of "{TargetValue}" printed the comparison target, which is exactly what a
// confirmation rule must not reveal. Messages are formatted in one pass and substituted values are never rescanned.
public sealed class MessageTemplateTests
{
    [Fact]
    public void UserInputThatLooksLikeAPlaceholder_IsPrintedLiterally()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Confirm)
            .Equal(m => m.Secret, o => o.ErrorMessage = "'{PropertyValue}' does not match"));

        var result = validator.Validate(new Model { Secret = "s3cr3t", Confirm = "{TargetValue}" });
        Assert.Equal("'{TargetValue}' does not match", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void ParameterValueThatLooksLikeAPlaceholder_IsPrintedLiterally()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Confirm).Must((_, context) =>
        {
            context.MessageParameters.Add("First", "{Second}");
            context.MessageParameters.Add("Second", "hidden");
            context.ErrorMessage = "{First}";

            return false;
        }));

        Assert.Equal("{Second}", Assert.Single(validator.Validate(new Model { Confirm = "x" }).Errors).Message);
    }

    [Fact]
    public void ReportedNameThatLooksLikeAPlaceholder_IsPrintedLiterally()
    {
        var validator = new Validator<Model>(x => x
            .Property(m => m.Confirm, o => o.PropertyName = "{PropertyValue}")
            .NotEmpty(o => o.ErrorMessage = "{PropertyName}"));

        Assert.Equal("{PropertyValue}", Assert.Single(validator.Validate(new Model { Confirm = " " }).Errors).Message);
    }

    [Theory]
    [InlineData("{Unknown} {PropertyName}", "{Unknown} Confirm")]
    [InlineData("{PropertyName", "{PropertyName")]
    [InlineData("PropertyName}", "PropertyName}")]
    [InlineData("{ {PropertyName}", "{ Confirm")]
    [InlineData("{{PropertyName}}", "{Confirm}")]
    [InlineData("{}", "{}")]
    [InlineData("no placeholders", "no placeholders")]
    [InlineData("", "")]
    public void Template_EdgesFormatPredictably(string template, string expected)
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Confirm).NotEmpty(o => o.ErrorMessage = template));
        Assert.Equal(expected, Assert.Single(validator.Validate(new Model()).Errors).Message);
    }

    [Fact]
    public void RepeatedPlaceholder_IsSubstitutedEveryTime()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Confirm)
            .NotEmpty(o => o.ErrorMessage = "{PropertyName}, {PropertyName}"));

        Assert.Equal("Confirm, Confirm", Assert.Single(validator.Validate(new Model()).Errors).Message);
    }

    [Fact]
    public void MessageWrappers_TakePartInFormatting()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Confirm).NotEmpty(o => o.ErrorMessage = "required"));
        validator.Configure(o => o.ErrorMessagePrefix = "{PropertyName}: ");
        Assert.Equal("Confirm: required", Assert.Single(validator.Validate(new Model()).Errors).Message);
    }

    private sealed class Model
    {
        public string? Secret { get; init; }

        public string? Confirm { get; init; }
    }
}