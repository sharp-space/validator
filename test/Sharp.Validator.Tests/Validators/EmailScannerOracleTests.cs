using System.Text;
using System.Text.RegularExpressions;

namespace Sharp.Validator.Tests.Validators;

// Email moved from a generated regex to a scanner: a shared Regex stops scaling across threads, and the rule ran on
// every valid request. The scanner must accept exactly what the WHATWG pattern accepts, so the pattern stays here as
// the oracle, fed inputs built around its edges: label lengths, hyphen placement, separators, and non-ASCII characters.
public partial class EmailScannerOracleTests
{
    private static readonly string[] Tokens =
    [
        "a", "Z", "9", "-", ".", "@", "@", ".", "..", "--", "_", "+", "!", "é", " ", "\n", "\0", "com", "example",
        new('a', 61), new('b', 62), new('c', 63), new('d', 64), "x-y", "-x", "x-",
        "#", "`", "~", "{", "}", "|", "\"", "(", ")", ",", ":", ";", "<", ">", "[", "]", "\\"
    ];

    [GeneratedRegex(
        @"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*\z")]
    private static partial Regex WhatwgPattern();

    [Fact]
    public void Scanner_AcceptsExactlyWhatThePatternAccepts()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Email).Email());
        var random = new Random(12345);
        var input = new StringBuilder();
        for (var iteration = 0; iteration < 200_000; iteration++)
        {
            input.Clear();
            for (var token = random.Next(1, 9); token > 0; token--) input.Append(Tokens[random.Next(Tokens.Length)]);

            var email = input.ToString();
            Assert.True(
                WhatwgPattern().IsMatch(email) == validator.Validate(new Person { Email = email }).IsValid,
                $"Disagreement on '{email}'.");
        }
    }

    [Theory]
    [InlineData("a@b", true)]
    [InlineData("a@b.c", true)]
    [InlineData("a@b.", false)]
    [InlineData("a@.b", false)]
    [InlineData("@b", false)]
    [InlineData("a@", false)]
    [InlineData("a@-b", false)]
    [InlineData("a@b-", false)]
    [InlineData("a@b\n", false)]
    public void Label_EdgeCases_AreValidatedAsExpected(string email, bool expected) =>
        Assert.Equal(expected, new Validator<Person>(x => x.Property(person => person.Email).Email())
            .Validate(new Person { Email = email }).IsValid);

    [Fact]
    public void Label_MayHold63CharactersButNot64()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Email).Email());
        Assert.True(validator.Validate(new Person { Email = "a@" + new string('b', 63) }).IsValid);
        Assert.False(validator.Validate(new Person { Email = "a@" + new string('b', 64) }).IsValid);
    }
}