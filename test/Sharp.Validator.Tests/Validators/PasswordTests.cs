namespace Sharp.Validator.Tests.Validators;

public sealed class PasswordTests
{
    [Theory]
    [InlineData(1, "character", "letter")]
    [InlineData(2, "characters", "letters")]
    public void Password_Messages_AgreeWithTheRequiredCount(int count, string characters, string letters)
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Password).Password(options =>
        {
            options.RequiredLength = count;
            options.RequiredDigits = 0;
            options.RequiredUppercaseLetters = 0;
            options.RequiredLowercaseLetters = count;
            options.RequiredNonAlphanumerics = count;
            options.RequiredUniqueCharacters = count;
        }));

        var errors = validator.Validate(new Person { Password = "A" }).Errors;
        Assert.Contains(errors, x => x.Code == "PasswordLowercaseLetters" && x.Message!.Contains($"{count} lowercase {letters}"));
        Assert.Contains(errors, x => x.Code == "PasswordNonAlphanumerics" && x.Message!.Contains($"{count} non-alphanumeric {characters}"));

        if (count == 2)
        {
            Assert.Contains(errors, x => x.Code == "PasswordLength" && x.Message!.Contains($"{count} {characters}"));
            Assert.Contains(errors, x => x.Code == "PasswordUniqueCharacters" && x.Message!.Contains($"{count} unique {characters}"));
        }
    }

    [Fact]
    // The defaults required a digit, a symbol, and a lowercase letter but no uppercase one, a mix that was hard to
    // explain and that NIST SP 800-63B advises against. Only the length counts by default; the counts are opt-in.
    public void Password_DefaultOptionsRequireOnlyTheLength()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Password).Password());
        var shortOne = validator.Validate(new Person { Password = "abc" });
        Assert.Equal("PasswordLength", Assert.Single(shortOne.Errors).Code);
        Assert.True(validator.Validate(new Person { Password = "abcdefgh" }).IsValid);
    }

    [Fact]
    public void Password_NullPassesWhileEmptyFailsMinimumLength()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Password).Password());

        // null short-circuits all rules.
        Assert.True(validator.Validate(new Person { Password = null }).IsValid);

        // "" reaches MinimumLength(8) and fails.
        Assert.False(validator.Validate(new Person { Password = "" }).IsValid);
    }

    [Fact]
    public void Password_WhitespaceOnly_IsValidatedNotSkipped()
    {
        // Password sub-validators short-circuit only null/empty (IsNullOrEmpty), not whitespace.
        // So a whitespace-only value is validated and fails, unlike "" which is skipped.
        var validator = new Validator<Person>(builder => builder.Property(x => x.Password).Password(options =>
        {
            options.RequiredLength = 0;
            options.RequiredDigits = 1;
            options.RequiredNonAlphanumerics = 0;
            options.RequiredUppercaseLetters = 0;
            options.RequiredLowercaseLetters = 0;
            options.RequiredUniqueCharacters = 0;
        }));

        // "   " is not skipped: it contains no digit → fails the digit rule.
        Assert.False(validator.Validate(new Person { Password = "   " }).IsValid);

        // "" is skipped by the digit rule (IsNullOrEmpty) → passes once length is disabled.
        Assert.True(validator.Validate(new Person { Password = "" }).IsValid);
    }

    [Fact]
    public void Password_UniqueCharactersConfigured_AreRequired()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Password)
            .Password(options =>
            {
                options.RequiredLength = 0;
                options.RequiredDigits = 0;
                options.RequiredNonAlphanumerics = 0;
                options.RequiredUppercaseLetters = 0;
                options.RequiredLowercaseLetters = 0;
                options.RequiredUniqueCharacters = 5;
            }));

        Assert.False(validator.Validate(new Person { Password = "aaaa" }).IsValid);
        Assert.False(validator.Validate(new Person { Password = "aabbcc" }).IsValid); // 3 unique
        Assert.True(validator.Validate(new Person { Password = "abcde" }).IsValid);
        Assert.True(validator.Validate(new Person { Password = "abcdefg" }).IsValid);
    }

    [Fact]
    public void Password_DefaultUniqueCharacters_IsZeroSoNothingIsChecked()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Password).Password(options =>
        {
            options.RequiredLength = 0;
            options.RequiredDigits = 0;
            options.RequiredNonAlphanumerics = 0;
            options.RequiredUppercaseLetters = 0;
            options.RequiredLowercaseLetters = 0;
        }));

        Assert.True(validator.Validate(new Person { Password = "aaaa" }).IsValid);
    }

    [Theory]
    [InlineData(1, "'Password' must contain at least 1 uppercase letter.")]
    [InlineData(2, "'Password' must contain at least 2 uppercase letters.")]
    // Isolates PasswordUppercaseCountValidator by disabling every other rule. The message says "letter" for one and
    // "letters" for any other count.
    public void Password_UppercaseRequirementIsolated(int required, string message)
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Password).Password(options =>
        {
            options.RequiredLength = 0;
            options.RequiredDigits = 0;
            options.RequiredNonAlphanumerics = 0;
            options.RequiredUppercaseLetters = required;
            options.RequiredLowercaseLetters = 0;
            options.RequiredUniqueCharacters = 0;
        }));

        Assert.True(validator.Validate(new Person { Password = new string('A', required) + "bcd" }).IsValid);

        var fail = validator.Validate(new Person { Password = new string('A', required - 1) + "bcd" });
        Assert.False(fail.IsValid);
        Assert.Equal("PasswordUppercaseLetters", fail.Errors[0].Code);
        Assert.Equal(message, fail.Errors[0].Message);
        Assert.True(validator.Validate(new Person { Password = null }).IsValid); // null skipped
        Assert.True(validator.Validate(new Person { Password = "" }).IsValid); // empty skipped
    }

    [Fact]
    // A count rule bakes its required count into the template when it freezes; one whose template was cleared still
    // freezes and reports an empty message.
    public void ClearedTemplate_ReportsAnEmptyMessage()
    {
        var rule = new PasswordDigitCountValidator<Person>(1);
        rule.Options!.ErrorMessage = null;
        var validator = new Validator<Person>(x => x.Property(person => person.Password).Add(rule));
        Assert.Equal(string.Empty, Assert.Single(validator.Validate(new Person { Password = "abc" }).Errors).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    // The count rules run only when a requirement turns them on, and like the other format rules they leave a missing
    // password to NotNull or NotEmpty.
    public void CountRules_SkipANullOrEmptyPassword(string? password)
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Password).Password(requirements =>
        {
            requirements.RequiredLength = 0;
            requirements.RequiredDigits = 1;
            requirements.RequiredNonAlphanumerics = 1;
            requirements.RequiredUppercaseLetters = 1;
            requirements.RequiredLowercaseLetters = 1;
            requirements.RequiredUniqueCharacters = 2;
        }));

        Assert.True(validator.Validate(new Person { Password = password }).IsValid);
    }
}