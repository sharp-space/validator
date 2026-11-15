namespace Sharp.Validator.Tests.Validators;

public sealed class UriTests
{
    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/path?q=1", true)]
    [InlineData("HTTPS://example.com", true)] // case-insensitive scheme
    [InlineData("ftp://example.com", false)] // ftp not allowed by Url()
    [InlineData("file:///etc/passwd", false)]
    [InlineData("not a url", false)]
    [InlineData("/relative/path", false)] // relative not absolute
    public void Url_ValidatesHttpHttpsOnly(string value, bool valid)
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Url).Url());
        Assert.Equal(valid, validator.Validate(new Person { Url = value }).IsValid);
    }

    [Fact]
    public void Url_NullOrEmpty_Passes()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Url).Url());
        Assert.True(validator.Validate(new Person { Url = null }).IsValid);
        Assert.True(validator.Validate(new Person { Url = "" }).IsValid);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("ftp://example.com", true)]
    [InlineData("file:///etc/passwd", true)]
    [InlineData("not a url", false)]
    public void UriAbsolute_AcceptsAnySchemeByDefault(string value, bool valid)
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Url).Uri());
        Assert.Equal(valid, validator.Validate(new Person { Url = value }).IsValid);
    }

    [Theory]
    [InlineData("/relative/path", true)]
    [InlineData("https://example.com", true)]
    [InlineData("plain-string", true)] // valid relative URI
    public void UriRelativeOrAbsolute_AcceptsRelative(string value, bool valid)
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Url).Uri(UriKind.RelativeOrAbsolute));
        Assert.Equal(valid, validator.Validate(new Person { Url = value }).IsValid);
    }

    [Fact]
    public void Uri_WithExplicitAllowedSchemes()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Url)
            .Uri(UriKind.Absolute, ["wss", "ws"]));

        Assert.True(validator.Validate(new Person { Url = "wss://example.com" }).IsValid);
        Assert.True(validator.Validate(new Person { Url = "ws://example.com" }).IsValid);
        Assert.False(validator.Validate(new Person { Url = "https://example.com" }).IsValid);
    }
}