namespace Sharp.Validator.Tests.Messages;

// A failure that can't report a cached error, such as an element's, scanned its rule's template for placeholders every
// time. A frozen template whose only placeholder is {PropertyName} is now split once, and the failure joins the parts
// with the name. These pin that the joined message is the one the scan made, in the sync and async walks, that a
// wrapped message or one set at runtime is still scanned, and that a rule splits its message only once it fails: a
// Scoped validator freezes in every scope, where splitting every rule's message cost each scope about 0.85 µs.
public sealed class JoinedMessageTests
{
    private static readonly Model Empties = new() { Tags = ["", ""] };

    [Theory]
    [InlineData("'{PropertyName}' is empty.", "'Tags[1]' is empty.")]
    [InlineData("{PropertyName} and {PropertyName}", "Tags[1] and Tags[1]")]
    [InlineData("{PropertyName}", "Tags[1]")]
    [InlineData("No name.", "No name.")]
    [InlineData("", "")]
    public async Task ElementFailure_TemplateWithOnlyTheName_FillsEveryOccurrence(string template, string expected)
    {
        var sync = new Validator<Model>(x => x.Elements(model => model.Tags).NotEmpty(options => options.ErrorMessage = template));
        var async = new Validator<Model>(x => x.Elements(model => model.Tags)
            .MustAsync(_ => Task.FromResult(false), options => options.ErrorMessage = template));

        Assert.Equal(("Tags[1]", expected), Last(sync.Validate(Empties)));
        Assert.Equal(("Tags[1]", expected), Last(await async.ValidateAsync(Empties)));
    }

    [Fact]
    public void ElementFailure_ChainWithADisplayName_NamesItInTheMessageOnly()
    {
        var validator = new Validator<Model>(x => x.Elements(model => model.Tags, options => options.DisplayName = "Tag").NotEmpty());
        Assert.Equal(("Tags[1]", "'Tag' must not be empty."), Last(validator.Validate(Empties)));
    }

    [Fact]
    public void ElementFailure_ValidatorWrapsMessages_WrapsTheFilledTemplate()
    {
        var validator = new Validator<Model>(x => x.Elements(model => model.Tags).NotEmpty());
        validator.Configure(options =>
        {
            options.ErrorMessagePrefix = "[{PropertyName}] ";
            options.ErrorMessageSuffix = " [end]";
        });

        Assert.Equal(("Tags[1]", "[Tags[1]] 'Tags[1]' must not be empty. [end]"), Last(validator.Validate(Empties)));
    }

    [Fact]
    public void ElementFailure_MessageSetAtRuntime_ReplacesTheTemplate()
    {
        var validator = new Validator<Model>(x => x.Elements(model => model.Tags).Add(new RuntimeMessage()));
        Assert.Equal(("Tags[1]", "Tags[1] was "), Last(validator.Validate(Empties)));
    }

    [Fact]
    public void Rule_UntilItFirstFails_KeepsItsMessageWhole()
    {
        var rule = new FixedMessage();
        var validator = new Validator<Model>(x => x.Elements(model => model.Tags).Add(rule));
        validator.Validate(new Model { Tags = ["a"] });

        var beforeFailing = SplitOf(rule);
        validator.Validate(Empties);

        Assert.Null(beforeFailing);
        Assert.Equal(["'", "' is fixed."], SplitOf(rule)!);
    }

    private static string[]? SplitOf(PropertyValidator rule) => rule.SplitMessageParts;

    private static (string?, string?) Last(ValidationResult result) =>
        (result.Errors[^1].PropertyName, result.Errors[^1].Message);

    private sealed class Model
    {
        public List<string>? Tags { get; init; }
    }

    private sealed class RuntimeMessage : PropertyValidator<Model, string?>
    {
        public RuntimeMessage() => Configure(options => options.ErrorMessage = "'{PropertyName}' is fixed.");

        public override bool Validate(string? value, ValidationContext<Model> context)
        {
            context.ErrorMessage = "{PropertyName} was " + value;
            return false;
        }
    }

    private sealed class FixedMessage : PropertyValidator<Model, string?>
    {
        public FixedMessage() => Configure(options => options.ErrorMessage = "'{PropertyName}' is fixed.");

        public override bool Validate(string? value, ValidationContext<Model> context) => value != "";
    }
}