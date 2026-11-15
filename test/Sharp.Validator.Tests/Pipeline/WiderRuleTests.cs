namespace Sharp.Validator.Tests.Pipeline;

// Add takes IPropertyValidator<TModel, in TProperty>, so a chain also takes a rule for a wider type. A rule that
// differs from the chain in nullable annotations alone is the chain's type at run time and is stored as it is; a rule
// for a genuinely wider type, such as object?, runs behind an adapter, which must report the rule's own code, options,
// and one-chain check rather than its own.
public sealed class WiderRuleTests
{
    [Fact]
    public void Add_NullableRuleOnNonNullableChain_StoresTheRuleItself()
    {
        var rule = new ShortRule<Model>();
        var validator = new Validator<Model>(x => x.Property(m => m.Title).Add(rule));
        Assert.Same(rule, Assert.Single(validator.Builder.PropertyBuilders[0].PropertyEvaluator!.Validators));
    }

    [Fact]
    public void Add_WiderRule_RunsBehindAnAdapter()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Title).Add(new FilledRule<Model>()));
        Assert.IsType<WiderRuleValidator<Model, string>>(Assert.Single(validator.Builder.PropertyBuilders[0].PropertyEvaluator!.Validators));
    }

    [Fact]
    public void WiderRule_InvalidValue_ReportsItsOwnTypeName()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Title).Add(new FilledRule<Model>()));
        var error = Assert.Single(validator.Validate(new Model()).Errors);
        Assert.Equal(("Title", "FilledRule", "'Title' is not valid."), (error.PropertyName, error.Code, error.Message));
        Assert.True(validator.Validate(new Model { Title = "a" }).IsValid);
    }

    [Fact]
    public void WiderRule_ConfiguredByItself_KeepsItsOptions()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Title).Add(new ConfiguredFilledRule<Model>()));
        var error = Assert.Single(validator.Validate(new Model()).Errors);
        Assert.Equal(("Blank", "'Title' is blank: ''.", ValidationErrorSeverity.Warning), (error.Code, error.Message, error.Severity));
    }

    [Fact]
    public void WiderRule_ConfiguredByItsChain_TakesTheChainsOptions()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Title, o => o.ErrorCode = "Chain")
            .Add(new FilledRule<Model>(), o => o.ErrorMessage = "{PropertyName} is '{PropertyValue}'."));

        var error = Assert.Single(validator.Validate(new Model()).Errors);
        Assert.Equal(("Chain", "Title is ''."), (error.Code, error.Message));
    }

    [Fact]
    public void WiderRule_ErrorMessagesEntryForItsCode_ReplacesItsDefault()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Title).Add(new FilledRule<Model>()));
        validator.Configure(options => options.ErrorMessages["FilledRule"] = "{PropertyName} needs a value.");
        Assert.Equal("Title needs a value.", Assert.Single(validator.Validate(new Model()).Errors).Message);
    }

    [Fact]
    public void WiderLibraryRule_InvalidValue_ReportsItsCode()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Nick).Add(new NotNullValidator<Model, object?>()));
        var error = Assert.Single(validator.Validate(new Model()).Errors);
        Assert.Equal((ErrorCodes.NotNull, "'Nick' must not be null."), (error.Code, error.Message));
    }

    [Fact]
    public void WiderRule_AddedToASecondChain_Throws()
    {
        var rule = new FilledRule<Model>();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Model>(x =>
            {
                x.Property(m => m.Title).Add(rule);
                x.Property(m => m.Nick).Add(rule);
            }));

        Assert.StartsWith("This FilledRule`1 instance already belongs to a rule chain.", exception.Message);
    }

    [Fact]
    public void WiderRule_OnElements_ChecksEachElement()
    {
        var validator = new Validator<Model>(x => x.Elements(m => m.Tags).Add(new FilledRule<Model>()));
        Assert.Equal("Tags[1]", Assert.Single(validator.Validate(new Model { Tags = ["a", ""] }).Errors).PropertyName);
    }

    [Fact]
    public void WiderRule_OnElementProperty_ChecksEachElement()
    {
        var validator = new Validator<Model>(x => x.Elements(m => m.Locations).Property(l => l.City).Add(new FilledRule<Model>()));
        var result = validator.Validate(new Model { Locations = [new Location { City = "a" }, new Location()] });
        Assert.Equal(("Locations[1].City", "FilledRule"), (Assert.Single(result.Errors).PropertyName, result.Errors[0].Code));
    }

    [Fact]
    public async Task AsyncWiderRule_InvalidValue_ReportsItsOwnTypeName()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Title).Add(new AsyncFilledRule<Model>()));
        var error = Assert.Single((await validator.ValidateAsync(new Model())).Errors);
        Assert.Equal(("AsyncFilledRule", "'Title' is not valid."), (error.Code, error.Message));
        Assert.True((await validator.ValidateAsync(new Model { Title = "a" })).IsValid);
    }

    [Fact]
    public async Task AsyncWiderRule_OnElementProperty_ChecksEachElement()
    {
        var validator = new Validator<Model>(x =>
            x.Elements(m => m.Locations).Property(l => l.City).Add(new AsyncFilledRule<Model>()));

        var result = await validator.ValidateAsync(new Model { Locations = [new Location(), new Location { City = "a" }] });
        Assert.Equal("Locations[0].City", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public async Task AsyncNullableRuleOnNonNullableChain_StoresTheRuleItself()
    {
        var rule = new AsyncShortRule<Model>();
        var validator = new Validator<Model>(x => x.Property(m => m.Title).Add(rule));
        Assert.Same(rule, Assert.Single(validator.Builder.PropertyBuilders[0].AsyncEvaluator!.Validators));
        Assert.False((await validator.ValidateAsync(new Model { Title = "abcd" })).IsValid);
    }

    private sealed class FilledRule<TModel> : PropertyValidator<TModel, object?>
    {
        public override bool Validate(object? value, ValidationContext<TModel> context) => value is not (null or "");
    }

    private sealed class ConfiguredFilledRule<TModel> : PropertyValidator<TModel, object?>
    {
        public ConfiguredFilledRule() => Configure(options =>
        {
            options.ErrorCode = "Blank";
            options.ErrorMessage = "'{PropertyName}' is blank: '{PropertyValue}'.";
            options.ErrorSeverity = ValidationErrorSeverity.Warning;
        });

        public override bool Validate(object? value, ValidationContext<TModel> context) => value is not (null or "");
    }

    private sealed class AsyncFilledRule<TModel> : AsyncPropertyValidator<TModel, object?>
    {
        public override Task<bool> ValidateAsync(object? value, ValidationContext<TModel> context, CancellationToken cancellationToken) =>
            Task.FromResult(value is not (null or ""));
    }

    private sealed class ShortRule<TModel> : PropertyValidator<TModel, string?>
    {
        public override bool Validate(string? value, ValidationContext<TModel> context) => value is not { Length: > 3 };
    }

    private sealed class AsyncShortRule<TModel> : AsyncPropertyValidator<TModel, string?>
    {
        public override Task<bool> ValidateAsync(string? value, ValidationContext<TModel> context, CancellationToken cancellationToken) =>
            Task.FromResult(value is not { Length: > 3 });
    }

    private sealed class Location
    {
        public string City { get; init; } = "";
    }

    private sealed class Model
    {
        public string Title { get; init; } = "";

        public string? Nick { get; init; }

        public List<string> Tags { get; init; } = [];

        public List<Location> Locations { get; init; } = [];
    }
}