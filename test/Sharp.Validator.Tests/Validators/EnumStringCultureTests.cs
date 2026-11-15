using System.Globalization;

// The enums are the data under test: InEnum reads their members from the type, so code never names most of them.
// ReSharper disable UnusedMember.Local

namespace Sharp.Validator.Tests.Validators;

[Collection(GlobalOptions.Name)]
// Regression: InEnum(Type, StringComparison.CurrentCulture[IgnoreCase]) must resolve member-name comparisons against
// the culture in effect at Validate() time, not the one captured when the validator was built. A DI-singleton validator
// constructed under one culture and exercised under per-request localization would otherwise keep comparing with the
// construction-time culture (StringComparer.FromComparison snapshots it). Culture-stable comparisons
// (Ordinal*, Invariant*) keep the O(1) FrozenSet fast path.
public sealed class EnumStringCultureTests
{
    [Fact]
    public void CurrentCultureIgnoreCase_ResolvesAgainstValidationTimeCulture()
    {
        var enUs = CultureInfo.GetCultureInfo("en-US");
        var trTr = CultureInfo.GetCultureInfo("tr-TR");
        var original = CultureInfo.CurrentCulture;

        try
        {
            // Build the validator under en-US. Comparison must still be deferred to Validate() time.
            CultureInfo.CurrentCulture = enUs;
            var enMatch = string.Equals("Imap", "imap", StringComparison.CurrentCultureIgnoreCase);
            var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
                .InEnum(typeof(MailProtocol), StringComparison.CurrentCultureIgnoreCase));

            Assert.Equal(enMatch, validator.Validate(new Person { Name = "imap" }).IsValid);

            // Switch to Turkish, where I/i are not case-folding partners. The snapshot bug would keep en-US semantics
            // here and report the en-US result.
            CultureInfo.CurrentCulture = trTr;
            var trMatch = string.Equals("Imap", "imap", StringComparison.CurrentCultureIgnoreCase);

            // Skip if the runtime can't tell the two cultures apart (e.g. invariant globalization), since then there is
            // nothing to regress against.
            Assert.SkipWhen(enMatch == trMatch, "Runtime does not apply culture-specific casing (invariant globalization).");
            Assert.Equal(trMatch, validator.Validate(new Person { Name = "imap" }).IsValid);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Ordinal_IsExactCaseSensitiveMatch()
    {
        // Default StringComparison is Ordinal (culture-stable FrozenSet path).
        var validator = new Validator<Person>(x => x.Property(person => person.Name).InEnum(typeof(MailProtocol)));
        Assert.True(validator.Validate(new Person { Name = "Imap" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "imap" }).IsValid); // case differs under Ordinal
        Assert.False(validator.Validate(new Person { Name = "Webdav" }).IsValid); // not a member
        Assert.True(validator.Validate(new Person { Name = null }).IsValid); // null skipped
        Assert.True(validator.Validate(new Person { Name = "" }).IsValid); // empty skipped
        Assert.False(validator.Validate(new Person { Name = "   " }).IsValid); // whitespace-only is validated, like every other string rule
    }

    [Fact]
    public void OrdinalIgnoreCase_MatchesRegardlessOfCase()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .InEnum(typeof(MailProtocol), StringComparison.OrdinalIgnoreCase));

        Assert.True(validator.Validate(new Person { Name = "imap" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "IMAP" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "imapx" }).IsValid);
    }

    private enum MailProtocol
    {
        Imap,
        Smtp,
        Pop
    }
}