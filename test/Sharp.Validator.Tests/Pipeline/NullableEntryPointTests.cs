namespace Sharp.Validator.Tests.Pipeline;

// Regression: string rules extended PropertyValidatorBuilder<TModel, string?> and Include took Validator<TProperty>, so
// non-nullable string elements and nullable child properties raised CS8620 on the README's first example. String-only
// Property and Elements overloads then made every string chain a string? chain, so a predicate on a non-nullable string
// raised CS8602. A chain now has the property's declared type, and the string rules take string and string? chains
// alike. This file is the compile-time gate: the solution builds with warnings as errors, so a nullability mismatch
// fails the build, and assigning a chain to a variable of the other annotation does too (CS8619).
public sealed class NullableEntryPointTests
{
    [Fact]
    public void Property_NonNullableString_ChainsAsString()
    {
        PropertyValidatorBuilder<Model, string>? chain = null;
        var validator = new Validator<Model>(x => chain = x.Property(m => m.Title).MinimumLength(1).Must(title => title.Length <= 3));

        Assert.NotNull(chain);
        Assert.Equal("Title", Assert.Single(validator.Validate(new Model { Title = "abcd" }).Errors).PropertyName);
    }

    [Fact]
    // README: the declared type is the compiler's view, so a non-nullable string that is null at run time reaches the
    // predicate as null; NotNull first under Stop keeps the predicate from running on it.
    public void Must_NonNullableStringNullAtRunTime_ReachesThePredicate() =>
        Assert.ThrowsAny<NullReferenceException>(() =>
            new Validator<Model>(x => x.Property(m => m.Title).NotNull().Must(title => title.Length <= 3)).Validate(new Model { Title = null! }));

    [Fact]
    public void Must_AfterNotNullUnderStop_SkipsANullAtRunTime()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Title, o => o.CascadeMode = ValidationCascadeMode.Stop)
            .NotNull().Must(title => title.Length <= 3));

        Assert.Equal(ErrorCodes.NotNull, Assert.Single(validator.Validate(new Model { Title = null! }).Errors).Code);
    }

    [Fact]
    public void Property_NullableString_ChainsAsNullableString()
    {
        PropertyValidatorBuilder<Model, string?>? chain = null;
        var validator = new Validator<Model>(x => chain = x.Property(m => m.Nick).MaximumLength(3).Must(nick => nick?.Length != 2));

        Assert.NotNull(chain);
        Assert.True(validator.Validate(new Model()).IsValid);
        Assert.False(validator.Validate(new Model { Nick = "ab" }).IsValid);
    }

    [Fact]
    public async Task MustAsync_OnNonNullableString_ReadsTheValueAsString()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Title).MustAsync(title => Task.FromResult(title.Length <= 3)));
        Assert.False((await validator.ValidateAsync(new Model { Title = "abcd" })).IsValid);
    }

    [Fact]
    public void Elements_NonNullableStrings_ChainAsString()
    {
        ElementValidatorBuilder<Model, string>? chain = null;
        var validator = new Validator<Model>(x => (chain = x.Elements(m => m.Tags)).Must(tag => tag.Length <= 2));

        Assert.NotNull(chain);
        Assert.Equal("Tags[1]", Assert.Single(validator.Validate(new Model { Tags = ["ab", "abc"] }).Errors).PropertyName);
    }

    [Fact]
    public void ElementProperty_NonNullableString_ChainsAsString()
    {
        ElementPropertyValidatorBuilder<Model, Location, string>? chain = null;
        var validator = new Validator<Model>(x =>
            (chain = x.Elements(m => m.Locations).Property(l => l.City)).Must(city => city.Length <= 2));

        Assert.NotNull(chain);
        Assert.False(validator.Validate(new Model { Locations = [new Location { City = "abc" }] }).IsValid);
    }

    [Fact]
    public void StringRules_ApplyToNonNullableStringElements()
    {
        var validator = new Validator<Model>(x => x.Elements(m => m.Tags).NotEmpty().MaximumLength(2));
        Assert.Equal("Tags[0]", validator.Validate(new Model { Tags = ["abc"] }).Errors[0].PropertyName);
    }

    [Fact]
    public void StringRules_ApplyToStringArrays()
    {
        var validator = new Validator<Model>(x => x.Elements(m => m.Codes).MaximumLength(2));
        Assert.Equal("Codes[0]", validator.Validate(new Model { Codes = ["abc"] }).Errors[0].PropertyName);
    }

    [Fact]
    public void StringRules_ApplyToNonNullableElementProperties()
    {
        var validator = new Validator<Model>(x => x.Elements(m => m.Locations).Property(l => l.City).MaximumLength(2));
        var result = validator.Validate(new Model { Locations = [new Location { City = "abc" }] });
        Assert.Equal("Locations[0].City", result.Errors[0].PropertyName);
    }

    [Fact]
    public void StringRules_ApplyToNullableElementProperties()
    {
        var validator = new Validator<Model>(x => x.Elements(m => m.Locations).Property(l => l.Zip).MaximumLength(2));
        Assert.False(validator.Validate(new Model { Locations = [new Location { Zip = "abc" }] }).IsValid);
    }

    [Fact]
    public void Include_AcceptsANullableProperty()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Home).Include(new LocationValidator()));
        Assert.True(validator.Validate(new Model()).IsValid);
        Assert.Equal("Home.City", validator.Validate(new Model { Home = new Location() }).Errors[0].PropertyName);
    }

    [Fact]
    public void Include_AcceptsANonNullableProperty()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Work).Include(new LocationValidator()));
        Assert.Equal("Work.City", validator.Validate(new Model()).Errors[0].PropertyName);
    }

    [Fact]
    public void Include_AcceptsElements()
    {
        var validator = new Validator<Model>(x => x.Elements(m => m.Locations).Include(new LocationValidator()));
        Assert.Equal("Locations[0].City", validator.Validate(new Model { Locations = [new Location()] }).Errors[0].PropertyName);
    }

    private sealed class Location
    {
        public string City { get; init; } = "";

        public string? Zip { get; init; }
    }

    private sealed class Model
    {
        public string Title { get; init; } = "";

        public string? Nick { get; init; }

        public List<string> Tags { get; init; } = [];

        public string[] Codes { get; init; } = [];

        public List<Location> Locations { get; init; } = [];

        public Location? Home { get; init; }

        public Location Work { get; } = new();
    }

    private sealed class LocationValidator : Validator<Location>
    {
        public LocationValidator() => Builder.Property(x => x.City).NotEmpty();
    }
}