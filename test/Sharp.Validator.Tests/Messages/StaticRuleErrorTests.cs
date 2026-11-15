namespace Sharp.Validator.Tests.Messages;

// A rule whose message has nothing to substitute but {PropertyName} resolves one ValidationError and reports it from
// every validation, which takes the message string and the error object off the failure path. Anything that varies per
// call must stay out of that cache: a message set at runtime, and any other placeholder.
public sealed class StaticRuleErrorTests
{
    [Fact]
    public void FixedMessage_ReportsOneErrorInstance()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        Assert.Same(
            validator.Validate(new Person { Name = "" }).Errors[0],
            validator.Validate(new Person { Name = null }).Errors[0]);
    }

    [Fact]
    // Regression: the shared instance made Distinct count a fixed-message failure once, while the same rule with a
    // message that reads the value counted three times.
    public void RepeatedFailures_CompareEqualWhateverTheMessageReads()
    {
        var fixedMessage = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        var valueMessage = new Validator<Person>(x => x.Property(person => person.Name)
            .NotEmpty(options => options.ErrorMessage = "'{PropertyValue}' is empty."));

        var model = new Person { Name = "" };
        Assert.Single(Enumerable.Range(0, 3).SelectMany(_ => fixedMessage.Validate(model).Errors).Distinct());
        Assert.Single(Enumerable.Range(0, 3).SelectMany(_ => valueMessage.Validate(model).Errors).Distinct());
    }

    [Fact]
    public void CachedError_CarriesCodeAndSeverity()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty(options =>
        {
            options.ErrorCode = "Custom";
            options.ErrorSeverity = ValidationErrorSeverity.Warning;
        }));

        validator.Validate(new Person { Name = "" });
        var error = validator.Validate(new Person { Name = "" }).Errors[0];
        Assert.Equal("Custom", error.Code);
        Assert.Equal(ValidationErrorSeverity.Warning, error.Severity);
    }

    [Fact]
    public void RuntimeMessage_NeitherUsesNorPoisonsTheCache()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Add(new SometimesOverriding()));
        Assert.Equal("overridden", validator.Validate(new Person { Name = "override" }).Errors[0].Message);
        Assert.Equal("'Name' is fixed.", validator.Validate(new Person { Name = "x" }).Errors[0].Message);
        Assert.Equal("overridden", validator.Validate(new Person { Name = "override" }).Errors[0].Message);
        Assert.Equal("'Name' is fixed.", validator.Validate(new Person { Name = "x" }).Errors[0].Message);
    }

    [Fact]
    public void ValuePlaceholder_IsResolvedPerFailure()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .MaximumLength(1, options => options.ErrorMessage = "{PropertyName}: {PropertyValue}"));

        Assert.Equal("Name: ab", validator.Validate(new Person { Name = "ab" }).Errors[0].Message);
        Assert.Equal("Name: cd", validator.Validate(new Person { Name = "cd" }).Errors[0].Message);
    }

    [Fact]
    public void Brace_InACustomPropertyName_IsCopiedVerbatim()
    {
        var validator = new Validator<Person>(x => x
            .Property(person => person.Name, options => options.PropertyName = "{Length}")
            .MaximumLength(1, options => options.ErrorMessage = "{PropertyName} is too long"));

        Assert.Equal("{Length} is too long", validator.Validate(new Person { Name = "ab" }).Errors[0].Message);
        Assert.Equal("{Length} is too long", validator.Validate(new Person { Name = "ab" }).Errors[0].Message);
    }

    private sealed class SometimesOverriding : PropertyValidator<Person, string?>
    {
        public SometimesOverriding() => Configure(options => options.ErrorMessage = "'{PropertyName}' is fixed.");

        public override bool Validate(string? value, ValidationContext<Person> context)
        {
            if (value == "override") context.ErrorMessage = "overridden";

            return false;
        }
    }
}

[Collection(GlobalOptions.Name)]
// A validator run directly composes the ambient Global wrappers; included under a parent it leaves them to the root.
// One rule therefore has two resolved errors, and whichever run comes first must not decide the other's message.
public sealed class StaticRuleErrorWrapperTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RootAndIncluded_RunsKeepTheirOwnWrappers(bool rootFirst)
    {
        var previous = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = new ValidatorBuilderOptions { ErrorMessagePrefix = "[G] " };

            var child = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
            var parent = new Validator<Parent>(x => x.Property(model => model.Child).Include(child));
            var empty = new Person { Name = "" };

            string Root() => child.Validate(empty).Errors[0].Message!;
            string Included() => parent.Validate(new Parent { Child = empty }).Errors[0].Message!;

            // A root error cached for the included run would come back wrapped twice.
            Assert.Equal("[G] 'Name' must not be empty.", rootFirst ? Root() : Included());
            Assert.Equal("[G] 'Name' must not be empty.", rootFirst ? Included() : Root());
        }
        finally
        {
            ValidatorBuilderOptions.Global = previous;
        }
    }

    private sealed class Parent
    {
        public Person? Child { get; init; }
    }
}