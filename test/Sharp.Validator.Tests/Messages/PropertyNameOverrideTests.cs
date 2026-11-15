namespace Sharp.Validator.Tests.Messages;

// Regression: PropertyName is documented as an override, yet the naming policy still rewrote it, so "displayName"
// reported as display_name under SnakeCase. A name the author spelled out is reported as written; only outer prefixes,
// suffixes, and collection indices still apply.
public sealed class PropertyNameOverrideTests
{
    [Fact]
    public void Override_SkipsTheNamingPolicy()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.FirstName, o => o.PropertyName = "displayName").NotEmpty());
        validator.Configure(x => x.PropertyNamePolicy = PropertyNamePolicy.SnakeCase);
        Assert.Equal("displayName", Assert.Single(validator.Validate(new Model()).Errors).PropertyName);
    }

    [Fact]
    public void Override_KeepsTheOuterWrappers()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.FirstName, o => o.PropertyName = "displayName").NotEmpty());
        validator.Configure(x =>
        {
            x.PropertyNamePolicy = PropertyNamePolicy.SnakeCase;
            x.PropertyNamePrefix = "PRE_";
        });

        Assert.Equal("PRE_displayName", Assert.Single(validator.Validate(new Model()).Errors).PropertyName);
    }

    [Fact]
    public void ElementsOverride_KeepsTheIndex()
    {
        var validator = new Validator<Model>(x => x.Elements(m => m.Tags!, o => o.PropertyName = "allLabels").NotEmpty());
        validator.Configure(x => x.PropertyNamePolicy = PropertyNamePolicy.SnakeCase);
        Assert.Equal("allLabels[0]", Assert.Single(validator.Validate(new Model { Tags = [""] }).Errors).PropertyName);
    }

    [Fact]
    public void ElementPropertyOverride_AppliesToItsSegmentOnly()
    {
        var validator = new Validator<Model>(x => x
            .Elements(m => m.Items!)
            .Property(i => i.Label, o => o.PropertyName = "shownLabel")
            .NotEmpty());

        validator.Configure(x => x.PropertyNamePolicy = PropertyNamePolicy.SnakeCase);
        Assert.Equal("items[0].shownLabel", Assert.Single(validator.Validate(new Model { Items = [new Item()] }).Errors).PropertyName);
    }

    [Fact]
    public void Member_NamesStillFollowThePolicy()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.FirstName).NotEmpty());
        validator.Configure(x => x.PropertyNamePolicy = PropertyNamePolicy.SnakeCase);
        Assert.Equal("first_name", Assert.Single(validator.Validate(new Model()).Errors).PropertyName);
    }

    [Fact]
    // PropertyValidatorBuilderOptions setters (PropertyName / ErrorSeverity / ErrorCode) applied via the
    // Property(expr, configure) callback flow through to the produced error.
    public void Property_OptionsOverrideNameSeverityAndCode()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name, options =>
            {
                options.PropertyName = "custom";
                options.ErrorSeverity = ValidationErrorSeverity.Warning;
                options.ErrorCode = "CUSTOM_CODE";
            }).NotEmpty());

        var error = validator.Validate(new Person { Name = null }).Errors.Single();
        Assert.Equal("custom", error.PropertyName);
        Assert.Equal(ValidationErrorSeverity.Warning, error.Severity);
        Assert.Equal("CUSTOM_CODE", error.Code);
    }

    private sealed class Item
    {
        public string? Label { get; init; }
    }

    private sealed class Model
    {
        public string? FirstName { get; init; }

        public List<string>? Tags { get; init; }

        public List<Item>? Items { get; init; }
    }
}