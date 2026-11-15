using System.Text;

namespace Sharp.Validator.Tests.Validators;

// PhoneNumber's character walk became a vectorized search, and it skips the three case-insensitive extension searches
// when no extension can start. The original walk stays here as the oracle over inputs mixing extension forms, Unicode
// whitespace, non-ASCII digits, and characters whose case folding could be mistaken for an extension letter.
public sealed class PhoneNumberOracleTests
{
    private const string Symbols = "-.()+";

    private static readonly string[] Tokens =
    [
        "0", "1", "9", "5", " ", "  ", "\t", "\n", " ", " ", "　", "\u0085", "-", ".", "(", ")", "+",
        "x", "X", "ext", "EXT", "Ext.", "ext.", "e", "t", "a", "٣", "１", "İ", "ſ", "K"
    ];

    private static bool Oracle(string value)
    {
        var span = RemoveExtension(value.AsSpan().TrimEnd());

        var digitFound = false;
        foreach (var character in span)
        {
            if (!char.IsAsciiDigit(character)) continue;

            digitFound = true;
            break;
        }

        if (!digitFound) return false;

        foreach (var character in span)
            if (!(char.IsAsciiDigit(character) || char.IsWhiteSpace(character) || Symbols.Contains(character)))
                return false;

        return true;
    }

    private static ReadOnlySpan<char> RemoveExtension(ReadOnlySpan<char> span)
    {
        foreach (var form in (string[])["ext.", "ext", "x"])
        {
            var lastIndex = span.LastIndexOf(form, StringComparison.OrdinalIgnoreCase);
            if (lastIndex >= 0 && MatchesExtension(span[(lastIndex + form.Length)..])) return span[..lastIndex];
        }

        return span;
    }

    private static bool MatchesExtension(ReadOnlySpan<char> span)
    {
        span = span.TrimStart();
        if (span.Length == 0) return false;

        foreach (var character in span)
            if (!char.IsAsciiDigit(character))
                return false;

        return true;
    }

    [Fact]
    public void Scanner_AcceptsExactlyWhatTheCharacterWalkAccepts()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Phone).PhoneNumber());
        var random = new Random(12345);
        var input = new StringBuilder();
        for (var iteration = 0; iteration < 200_000; iteration++)
        {
            input.Clear();
            for (var token = random.Next(1, 10); token > 0; token--) input.Append(Tokens[random.Next(Tokens.Length)]);

            var phone = input.ToString();
            Assert.True(
                Oracle(phone) == validator.Validate(new Person { Phone = phone }).IsValid,
                $"Disagreement on '{string.Concat(phone.Select(character => $"\\u{(int)character:X4}"))}'.");
        }
    }
}