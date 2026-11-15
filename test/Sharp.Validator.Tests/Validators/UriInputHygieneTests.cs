namespace Sharp.Validator.Tests.Validators;

// Regression: Uri.TryCreate trims whitespace and treats "/etc/passwd" as an absolute file URI on Unix, so Url() passed
// "https://example.com\n" (the string that gets stored is not the one the URI parser saw) and Uri() gave a different
// verdict per OS. Validation must judge the string as written.
public sealed class UriInputHygieneTests
{
    private static bool Url(string value) =>
        new Validator<Person>(x => x.Property(m => m.Url).Url()).Validate(new Person { Url = value }).IsValid;

    private static bool AbsoluteUri(string value) =>
        new Validator<Person>(x => x.Property(m => m.Url).Uri()).Validate(new Person { Url = value }).IsValid;

    private static bool AnyUri(string value) =>
        new Validator<Person>(x => x.Property(m => m.Url).Uri(UriKind.RelativeOrAbsolute)).Validate(new Person { Url = value }).IsValid;

    [Theory]
    [InlineData(" https://example.com")]
    [InlineData("https://example.com ")]
    [InlineData("https://example.com\n")]
    [InlineData("https://example.com\r\n")]
    [InlineData("\thttps://example.com")]
    [InlineData("https://exam\u0007ple.com")]
    [InlineData("https://example.com/\u007f")]
    public void Url_WhitespaceEdgeOrControlCharacter_IsRejected(string value) => Assert.False(Url(value));

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/a?b=c#d")]
    [InlineData("https://example.com/with%20escape")]
    public void Url_AcceptsCleanUrls(string value) => Assert.True(Url(value));

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\temp")]
    [InlineData("\\\\server\\share")]
    public void AbsoluteUri_NeedsAWrittenScheme(string value) => Assert.False(AbsoluteUri(value));

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("FILE:///etc/passwd")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("urn:isbn:0451450523")]
    public void AbsoluteUri_AcceptsAnyWrittenScheme(string value) => Assert.True(AbsoluteUri(value));

    [Fact]
    public void RelativeOrAbsolute_KeepsRootedPathsRelative() => Assert.True(AnyUri("/relative/path"));

    [Fact]
    public void RelativeOrAbsolute_RejectsAnImplicitWindowsPath() => Assert.False(AnyUri("C:\\temp"));
}