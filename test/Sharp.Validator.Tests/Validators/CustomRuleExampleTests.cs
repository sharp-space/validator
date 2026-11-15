namespace Sharp.Validator.Tests.Validators;

// README shows a reusable rule as a subclass of PropertyValidator<TModel, TProperty>, since the built-in rules are
// sealed, and an extension method for it written over string and string? chains alike. These tests keep that example
// working: defaults set with Configure act like a built-in rule's, and a string? rule fits a string chain.
public sealed class CustomRuleExampleTests
{
    private static Validator<Post> Validator() => new(x => x.Property(post => post.Slug).Add(new SlugValidator<Post>()));

    [Fact]
    public void CustomRule_InvalidValue_ReportsCodeWithMessage()
    {
        var validator = Validator();
        var error = Assert.Single(validator.Validate(new Post { Slug = "Hello World" }).Errors);
        Assert.Equal("InvalidSlug", error.Code);
        Assert.Equal("'Slug' must contain only lowercase letters, digits, and hyphens.", error.Message);
        Assert.True(validator.Validate(new Post { Slug = "hello-world-2" }).IsValid);
    }

    [Fact]
    public void CustomRule_ConfiguredByItsBaseAndItself_KeepsBoth()
    {
        var validator = new Validator<Post>(x => x.Property(post => post.Slug).Add(new NotBlankRule<Post>()));
        var error = Assert.Single(validator.Validate(new Post { Slug = " " }).Errors);
        Assert.Equal(("Coded", "'Slug' is blank."), (error.Code, error.Message));
    }

    [Fact]
    public void ErrorMessagesEntry_ForItsCode_ReplacesItsDefault()
    {
        var validator = Validator();
        validator.Configure(options => options.ErrorMessages["InvalidSlug"] = "{PropertyName} needs a slug.");
        Assert.Equal("Slug needs a slug.", Assert.Single(validator.Validate(new Post { Slug = "Hello" }).Errors).Message);
    }

    [Fact]
    public void CustomRule_OnNonNullableString_ChecksTheValue()
    {
        var validator = new Validator<Post>(x => x.Property(post => post.Title).Add(new SlugValidator<Post>()));
        Assert.Equal("InvalidSlug", Assert.Single(validator.Validate(new Post { Title = "Hello" }).Errors).Code);
    }

    [Fact]
    public void CustomRuleByType_OnNonNullableString_ChecksTheValue()
    {
        var validator = new Validator<Post>(x => x.Property(post => post.Title).Add<SlugValidator<Post>>());
        Assert.False(validator.Validate(new Post { Title = "Hello" }).IsValid);
    }

    [Fact]
    public void CustomRuleExtension_OnStringAndNullableString_ChecksBoth()
    {
        var validator = new Validator<Post>(x =>
        {
            x.Property(post => post.Slug).Slug().MaximumLength(64);
            x.Property(post => post.Title).Slug().MaximumLength(64);
        });

        var result = validator.Validate(new Post { Slug = "Hello", Title = "World" });
        Assert.Equal(["Slug", "Title"], result.Errors.Select(error => error.PropertyName));
    }

    // A family of rules can share defaults: the base sets the code, and each rule configures its message on top.
    private abstract class CodedRule<TModel> : PropertyValidator<TModel, string?>
    {
        protected CodedRule() => Configure(options => options.ErrorCode = "Coded");
    }

    private sealed class NotBlankRule<TModel> : CodedRule<TModel>
    {
        public NotBlankRule() => Configure(options => options.ErrorMessage = "'{PropertyName}' is blank.");

        public override bool Validate(string? value, ValidationContext<TModel> context) => !string.IsNullOrWhiteSpace(value);
    }

    private sealed class Post
    {
        public string? Slug { get; init; }

        public string Title { get; init; } = "";
    }
}

file sealed class SlugValidator<TModel> : PropertyValidator<TModel, string?>
{
    public SlugValidator() => Configure(options =>
    {
        options.ErrorCode = "InvalidSlug";
        options.ErrorMessage = "'{PropertyName}' must contain only lowercase letters, digits, and hyphens.";
    });

    public override bool Validate(string? value, ValidationContext<TModel> context) =>
        value is null || value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}

// The extension README shows: generic over the chain's type, so it reads as one rule on string and string? chains.
file static class SlugRules
{
    extension<TModel, TString>(PropertyValidatorBuilder<TModel, TString> builder) where TString : class?, IEquatable<string>?
    {
        public PropertyValidatorBuilder<TModel, TString> Slug() => builder.Add(new SlugValidator<TModel>());
    }
}