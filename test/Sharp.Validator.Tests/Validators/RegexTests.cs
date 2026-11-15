using System.Text.RegularExpressions;

namespace Sharp.Validator.Tests.Validators;

public sealed class RegexTests
{
    [Fact]
    public void Regex_WithStringPattern()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Regex("^[A-Z]+$"));
        Assert.True(validator.Validate(new Person { Name = "ABC" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "abc" }).IsValid);

        // null/empty short-circuit
        Assert.True(validator.Validate(new Person { Name = null }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "" }).IsValid);
    }

    [Fact]
    public void Regex_WithCompiledRegex()
    {
        var regex = new Regex(@"^\d+$");
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Regex(regex));
        Assert.True(validator.Validate(new Person { Name = "123" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "abc" }).IsValid);
    }

    [Fact]
    public void Regex_WithDynamicPattern_CachesCompiledRegex()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name).Regex(_ => "^[A-Z]+$"));

        // Run many times — should be fast (regex compiled once)
        for (var index = 0; index < 100; index++)
        {
            Assert.True(validator.Validate(new Person { Name = "X" }).IsValid);
            Assert.False(validator.Validate(new Person { Name = "x" }).IsValid);
        }
    }

    [Fact]
    public void Regex_WithManyDynamicPatterns_StillValidatesAfterCacheLimit()
    {
        var patternIndex = 0;
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            // ReSharper disable once AccessToModifiedClosure — the loop below changes the pattern on purpose.
            .Regex(_ => "^VALUE" + patternIndex + "$"));

        for (; patternIndex < 300; patternIndex++)
        {
            Assert.True(validator.Validate(new Person { Name = "VALUE" + patternIndex }).IsValid);
            Assert.False(validator.Validate(new Person { Name = "OTHER" }).IsValid);
        }
    }

    [Fact]
    public void RegexErrorMessage_ExposesPattern()
    {
        var validator = new Validator<Person>(builder =>
            builder.Property(x => x.Name, x => x.ErrorMessage = "must match {RegularExpression}").Regex("^[A-Z]+$"));

        var result = validator.Validate(new Person { Name = "abc" });
        Assert.False(result.IsValid);
        Assert.Equal("must match ^[A-Z]+$", result.Errors[0].Message);
    }

    [Fact]
    public void DynamicStringRegexSyntaxError_IsAValidationFailure()
    {
        var validator = new Validator<RegexModel>(x => x.Property(m => m.Value).Regex(m => m.Pattern));
        var result = validator.Validate(new RegexModel { Value = "abc", Pattern = "[" });
        Assert.Equal("Regex", Assert.Single(result.Errors).Code);
    }

    [Fact]
    // A null pattern from the model reached the dynamic-pattern cache, which threw ArgumentNullException with its own
    // parameter name, and a null Regex from the model threw NullReferenceException from the match. Both fail validation
    // as an invalid pattern does, reporting the missing pattern as null.
    public void DynamicPatternOrRegexThatIsNull_IsAValidationFailure()
    {
        var pattern = new Validator<RegexModel>(x =>
            x.Property(m => m.Value, o => o.ErrorMessage = "{RegularExpression}").Regex(m => m.Pattern));

        var regex = new Validator<RegexModel>(x =>
            x.Property(m => m.Value, o => o.ErrorMessage = "{RegularExpression}").Regex(Regex? (_) => null));

        var byPattern = Assert.Single(pattern.Validate(new RegexModel { Value = "abc", Pattern = null }).Errors);
        var byRegex = Assert.Single(regex.Validate(new RegexModel { Value = "abc" }).Errors);
        Assert.Equal(("Regex", "null"), (byPattern.Code, byPattern.Message));
        Assert.Equal(("Regex", "null"), (byRegex.Code, byRegex.Message));
    }

    [Fact]
    public void RegexUserDelegateExceptions_AreNotHidden()
    {
        var stringDelegate = new Validator<RegexModel>(x => x.Property(m => m.Value)
            .Regex((Func<RegexModel, string>)(_ => throw new ArgumentException("delegate"))));

        var regexDelegate = new Validator<RegexModel>(x => x.Property(m => m.Value)
            .Regex((Func<RegexModel, Regex>)(_ => throw new InvalidOperationException("delegate"))));

        Assert.Throws<ArgumentException>(() => stringDelegate.Validate(new RegexModel { Value = "abc" }));
        Assert.Throws<InvalidOperationException>(() => regexDelegate.Validate(new RegexModel { Value = "abc" }));
    }

    [Fact]
    // RegexValidator(Func<TModel, Regex>) overload: match, non-match, null skipped.
    public void Regex_FuncReturningRegexOverload_Matches()
    {
        var regex = new Regex("^[a-z]+$");
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Regex(_ => regex));
        Assert.True(validator.Validate(new Person { Name = "abc" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "ABC123" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = null }).IsValid); // null short-circuits
    }

    [Fact]
    // Regex(Func<TModel, string>, RegexOptions, TimeSpan) overload.
    public void Regex_FuncStringOverloadWithOptions_Matches()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .Regex(_ => "^[a-z]+$", RegexOptions.None, TimeSpan.FromSeconds(1)));

        Assert.True(validator.Validate(new Person { Name = "abc" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "ABC" }).IsValid);
    }

    private sealed class RegexModel
    {
        public string? Value { get; init; }

        public string? Pattern { get; init; } = "";
    }
}