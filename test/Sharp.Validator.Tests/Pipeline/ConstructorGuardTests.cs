namespace Sharp.Validator.Tests.Pipeline;

// Rules that can never pass (or never apply) fail fast at construction instead of shipping silently.
public sealed class ConstructorGuardTests
{
    [Fact]
    public void ExclusiveBetween_WithEqualBounds_Throws() =>
        // An exclusive range over equal bounds admits no value at all.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator<Person>(x => x.Property(person => person.Age).ExclusiveBetween(5, 5)));

    [Fact]
    public void InclusiveBetween_WithEqualBounds_IsAllowed()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).InclusiveBetween(5, 5));
        Assert.True(validator.Validate(new Person { Age = 5 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 6 }).IsValid);
    }

    [Fact]
    public void RelativeUri_WithSchemeWhitelist_Throws() =>
        // Relative URIs have no scheme — a scheme restriction could never apply and previously was silently ignored.
        Assert.Throws<ArgumentException>(() =>
            new Validator<Person>(x => x.Property(person => person.Url).Uri(UriKind.Relative, ["https"])));

    [Fact]
    public void RelativeOrAbsoluteUri_ChecksSchemesOnlyForAbsoluteValues()
    {
        var validator = new Validator<Person>(x =>
            x.Property(person => person.Url).Uri(UriKind.RelativeOrAbsolute, ["https"]));

        Assert.True(validator.Validate(new Person { Url = "https://example.com" }).IsValid);
        Assert.False(validator.Validate(new Person { Url = "http://example.com" }).IsValid);
        Assert.True(validator.Validate(new Person { Url = "/relative/path" }).IsValid);
    }
}