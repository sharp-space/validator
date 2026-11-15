using System.Globalization;
using System.Text.RegularExpressions;

namespace Sharp.Validator.Tests.Validators;

[Collection(GlobalOptions.Name)]
// A Regex takes case-insensitive matching from the culture current when it is built, so a pattern the validator
// built judged "I" and "İ" by the server's locale, and a dynamic pattern by whichever request first used it. Every
// pattern the validator builds ignores case in the invariant culture, as a [GeneratedRegex] does. A Regex passed in
// keeps its own.
public sealed class RegexCultureTests
{
    private static void InCulture(string name, Action action)
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void DynamicPattern_IgnoresCaseInTheInvariantCulture()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Regex(_ => "^[a-z]+$", RegexOptions.IgnoreCase));

        InCulture("tr-TR", () => Assert.True(validator.Validate(new Person { Name = "I" }).IsValid));
        InCulture("en-US", () => Assert.False(validator.Validate(new Person { Name = "İ" }).IsValid));
    }

    [Fact]
    public void RegexPassedIn_KeepsTheCultureItWasBuiltIn()
    {
        Regex? turkish = null;
        InCulture("tr-TR", () => turkish = new Regex("^[a-z]+$", RegexOptions.IgnoreCase));
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Regex(turkish!));

        InCulture("en-US", () => Assert.False(validator.Validate(new Person { Name = "I" }).IsValid));
    }
}