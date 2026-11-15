namespace Sharp.Validator.Tests.Validators;

// Email validates the WHATWG HTML5 "valid email address" syntax (browser input[type=email] parity).
// The previous pattern rejected real addresses (RFC local-part specials, punycode/hyphenated domain labels) while
// asymmetrically accepting Unicode local parts.
public sealed class EmailTests
{
    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("user@EXAMPLE.COM", true)]
    [InlineData("User@Example.Com", true)]
    [InlineData("user+tag@example.com", true)]
    [InlineData("o'brien@example.com", true)] // RFC 5322 local-part special (apostrophe)
    [InlineData("!#$%&'*+/=?^_`{|}~-@example.com", true)] // full specials set
    [InlineData("user@xn--bcher-kva.de", true)] // punycode (IDN) domain label with hyphens
    [InlineData("user@my-host.example.com", true)] // hyphenated label
    [InlineData("user@example", true)] // dotless (intranet-style) domain — valid per WHATWG, as in browsers
    [InlineData("josé@example.com", false)] // ASCII-only: Unicode local part rejected
    [InlineData("user@josé.com", false)] // Unicode domain must be punycode-encoded
    [InlineData("user@.com", false)] // empty domain label
    [InlineData("user@@example.com", false)] // double @
    [InlineData("user space@example.com", false)]
    [InlineData("user@-example.com", false)] // label may not start with a hyphen
    [InlineData("user@example-.com", false)] // label may not end with a hyphen
    [InlineData("user@", false)]
    [InlineData("@example.com", false)]
    public void Email_FollowsTheWhatwgPattern(string email, bool valid)
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Email).Email());
        Assert.Equal(valid, validator.Validate(new Person { Email = email }).IsValid);
    }

    // Regression: an address followed by a line feed is not a valid address, whatever the matcher's end anchor allows.
    [Fact]
    public void Email_RejectsATrailingLineFeed()
    {
        var validator = new Validator<Person>(x => x.Property(m => m.Email).Email());
        Assert.False(validator.Validate(new Person { Email = "user@example.com\n" }).IsValid);
    }

    [Fact]
    public void Email_NullOrEmptyUnlessNotEmptyAdded_IsValid()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Email).Email());
        Assert.True(validator.Validate(new Person { Email = null }).IsValid);
        Assert.True(validator.Validate(new Person { Email = "" }).IsValid);
    }
}