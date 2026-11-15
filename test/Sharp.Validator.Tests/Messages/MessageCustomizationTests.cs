namespace Sharp.Validator.Tests.Messages;

public sealed class MessageCustomizationTests
{
    [Fact]
    public void ValidatorLevelOptionsSetMessage_PerRule()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name)
            .NotEmpty(x => x.ErrorMessage = "Name is required")
            .MinimumLength(3, x => x.ErrorMessage = "Need 3 chars"));

        var nullResult = validator.Validate(new Person { Name = null });
        Assert.Equal("Name is required", nullResult.Errors[0].Message);

        var shortResult = validator.Validate(new Person { Name = "x" });
        Assert.Equal("Need 3 chars", shortResult.Errors[0].Message);
    }

    [Fact]
    public void ErrorMessage_PrefixAndSuffix()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        validator.Configure(options =>
        {
            options.ErrorMessagePrefix = "[err] ";
            options.ErrorMessageSuffix = " [end]";
        });

        var result = validator.Validate(new Person { Name = null });
        Assert.StartsWith("[err] ", result.Errors[0].Message);
        Assert.EndsWith(" [end]", result.Errors[0].Message);
    }

    [Fact]
    public void ContextErrorMessage_OverridesPerValidationCall()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        var result = validator.Validate(new Person { Name = null }, x => x.ErrorMessage = "custom for this call");
        Assert.Equal("custom for this call", result.Errors[0].Message);
    }

    [Fact]
    public void PropertyValuePlaceholderFilled_InErrorMessage()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name, x => x.ErrorMessage = "got '{PropertyValue}'")
            .MinimumLength(5));

        var result = validator.Validate(new Person { Name = "x" });
        Assert.Equal("got 'x'", result.Errors[0].Message);
    }

    [Fact]
    public void Validator_WithCustomMessage_CreatesFreshValidatorOptions()
    {
        // The custom validator carries no Options, so Add(...) with a configure builds a fresh PropertyValidatorOptions
        // from the supplied message.
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name)
            .Add(new AlwaysFailValidator<Person, string?>(), x => x.ErrorMessage = "Name is required"));

        var result = validator.Validate(new Person { Name = "anything" });
        Assert.False(result.IsValid);
        Assert.Equal("Name is required", result.Errors[0].Message);
    }

    // A validator that leaves Options null (unlike built-ins, which set it in their ctor), so adding it with a
    // configure exercises the "fresh options" branch of the builder.
    private sealed class AlwaysFailValidator<TModel, TProperty> : PropertyValidator<TModel, TProperty>
    {
        public override bool Validate(TProperty value, ValidationContext<TModel> context) => false;
    }
}