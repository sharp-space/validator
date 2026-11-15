using System.Text.RegularExpressions;

namespace Sharp.Validator.Tests.Validators;

// The string rules extend PropertyValidatorBuilder<TModel, TString> for TString string or string?, so a string chain
// keeps its property's declared type. Each form must judge a string chain as it judges a string? one. The constraint
// also admits an app's type that implements IEquatable<string>, which no string rule can check, so declaring one there
// throws rather than failing a cast later.
public sealed class StringChainTests
{
    private const int FormCount = 29;
    private static readonly string[] Values = ["", "a", "Ab1!", "user@example.com", "https://example.com/x", "4111 1111 1111 1111", "Monday"];

    public static TheoryData<int> Forms => [.. Enumerable.Range(0, FormCount)];

    [Theory]
    [MemberData(nameof(Forms))]
    public void StringRule_OnStringAndNullableStringChains_JudgesAlike(int form)
    {
        var onString = new Validator<Model>(x => Declare(x.Property(m => m.Text), form));
        var onNullable = new Validator<Model>(x => Declare(x.Property(m => m.Nick), form));

        var failures = 0;
        foreach (var value in Values)
        {
            var expected = onNullable.Validate(new Model { Nick = value }).Errors.Select(e => (e.Code, e.Message?.Replace("Nick", "Text")));
            var actual = onString.Validate(new Model { Text = value }).Errors.Select(e => (e.Code, e.Message)).ToList();
            Assert.Equal(expected, actual);

            failures += actual.Count;
        }

        Assert.True(failures > 0, $"form {form} failed no value");
    }

    [Fact]
    public void StringRule_OnTypeThatEquatesToString_ThrowsWhenDeclared()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new Validator<Model>(x => x.Property(m => m.Code).MinimumLength(1)));
        Assert.Contains(typeof(Code).FullName!, exception.Message);
    }

    [Fact]
    // A chain of the type itself has no instance Add for a string? rule, so the string rules' Add takes it, and throws.
    public void AsyncAdd_OnTypeThatEquatesToString_ThrowsWhenDeclared() =>
        Assert.Throws<InvalidOperationException>(() => new Validator<Model>(x => x.Property(m => m.Code).Add(new AsyncShortRule<Model>())));

    [Fact]
    public async Task AsyncAdd_OverAGenericStringChain_RunsTheRule()
    {
        var validator = new Validator<Model>(x => AddShortRule(x.Property(m => m.Text)));
        Assert.False((await validator.ValidateAsync(new Model { Text = "abcd" })).IsValid);
        Assert.True((await validator.ValidateAsync(new Model { Text = "abc" })).IsValid);
    }

    // Every string rule form, over a chain of either annotation.
    private static void Declare<TString>(PropertyValidatorBuilder<Model, TString> chain, int form)
        where TString : class?, IEquatable<string>? =>
        _ = form switch
        {
            0 => chain.Email(),
            1 => chain.Password(),
            2 => chain.Password(o => o.RequiredDigits = 1, o => o.ErrorCode = "Weak"),
            3 => chain.CreditCard(),
            4 => chain.PhoneNumber(),
            5 => chain.InEnum(typeof(DayOfWeek)),
            6 => chain.InEnum(typeof(DayOfWeek), StringComparison.OrdinalIgnoreCase),
            7 => chain.Length(2, 4),
            8 => chain.Length(_ => 2, _ => 4),
            9 => chain.Length(4),
            10 => chain.Length(_ => 4),
            11 => chain.MaximumLength(4),
            12 => chain.MaximumLength(_ => 4),
            13 => chain.MinimumLength(2),
            14 => chain.MinimumLength(_ => 2),
            15 => chain.Regex(new Regex("^[a-z]+$")),
            16 => chain.Regex("^[a-z]+$"),
            17 => chain.Regex("^[a-z]+$", RegexOptions.IgnoreCase),
            18 => chain.Regex("^[a-z]+$", RegexOptions.None, TimeSpan.FromSeconds(1)),
            19 => chain.Regex(_ => new Regex("^[a-z]+$")),
            20 => chain.Regex(_ => "^[a-z]+$"),
            21 => chain.Regex(_ => "^[a-z]+$", RegexOptions.IgnoreCase),
            22 => chain.Regex(_ => "^[a-z]+$", RegexOptions.None, TimeSpan.FromSeconds(1)),
            23 => chain.Url(),
            24 => chain.Uri(),
            25 => chain.Uri(UriKind.Relative),
            26 => chain.Uri(UriKind.Absolute, ["https"]),
            27 => chain.Add(new ShortRule<Model>()),
            28 => chain.Add(new ShortRule<Model>(), o => o.ErrorCode = "TooLong"),
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };

    // Generic over the chain's type, as an app's string rule extension is, so Add binds to the string rules' overload.
    private static void AddShortRule<TString>(PropertyValidatorBuilder<Model, TString> chain)
        where TString : class?, IEquatable<string>? =>
        chain.Add(new AsyncShortRule<Model>());

    private sealed class ShortRule<TModel> : PropertyValidator<TModel, string?>
    {
        public override bool Validate(string? value, ValidationContext<TModel> context) => value is not { Length: > 3 };
    }

    private sealed class AsyncShortRule<TModel> : AsyncPropertyValidator<TModel, string?>
    {
        public override Task<bool> ValidateAsync(string? value, ValidationContext<TModel> context, CancellationToken cancellationToken) =>
            Task.FromResult(value is not { Length: > 3 });
    }

    // A type of an app's that the string rules' constraint admits, though it is not a string.
    private sealed class Code : IEquatable<string>
    {
        public bool Equals(string? other) => other == "code";

        public override bool Equals(object? obj) => obj is string text && Equals(text);

        public override int GetHashCode() => 0;
    }

    private sealed class Model
    {
        public string Text { get; init; } = "";

        public string? Nick { get; init; }

        public Code Code { get; } = new();
    }
}