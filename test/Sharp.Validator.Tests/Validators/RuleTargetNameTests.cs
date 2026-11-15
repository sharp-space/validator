namespace Sharp.Validator.Tests.Validators;

public sealed class RuleTargetNameTests
{
    [Fact]
    public void UnfrozenNamedTargets_ExposeTheirPropertyNamesOnFailure()
    {
        var context = new ValidationContext<Model>(new());
        Assert.False(new GreaterThanValidator<Model, string>(_ => (true, "z"), "Target").Validate("m", context));
        Assert.Equal("Target", context.MessageParameters["TargetPropertyName"]);
        Assert.False(new EqualValidator<Model, string>(_ => (true, "z"), "Target").Validate("m", context));
        Assert.False(new NotEqualValidator<Model, string>(_ => (true, "m"), "Target").Validate("m", context));
        Assert.False(new InclusiveBetweenValidator<Model, string>(_ => (true, "x"), "From", _ => (true, "z"), "To").Validate("m", context));
        Assert.Equal("From", context.MessageParameters["FromPropertyName"]);
        Assert.Equal("To", context.MessageParameters["ToPropertyName"]);
    }

    [Fact]
    public void ComputedTargets_ExplicitComparerOrNullTemplate_ProduceEmptyMessage()
    {
        var validator = new Validator<Model>(b =>
        {
            b.Property<string>(x => x.Text)
                .Add(WithoutMessage(new GreaterThanValidator<Model, string>(_ => (true, "z"), null, StringComparer.Ordinal)));

            b.Property<string>(x => x.Text).Add(WithoutMessage(new EqualValidator<Model, string>(_ => (true, "z"), null)));
            b.Property<string>(x => x.Text).Add(WithoutMessage(new NotEqualValidator<Model, string>(_ => (true, "m"), null)));
            b.Property<string>(x => x.Text)
                .Add(WithoutMessage(
                    new InclusiveBetweenValidator<Model, string>(_ => (true, "x"), null, _ => (true, "z"), null, StringComparer.Ordinal)));
        });

        Assert.All(validator.Validate(new Model()).Errors, error => Assert.Equal(string.Empty, error.Message));
    }

    [Fact]
    public void ComputedComparisonTarget_KeepsRuntimeValueSubstitution()
    {
        var validator = new Validator<Model>(b => b.Property<string>(x => x.Text)
            .Add(new GreaterThanValidator<Model, string>(_ => (true, "z"), null)));

        Assert.Contains("z", Assert.Single(validator.Validate(new Model()).Errors).Message!);

        Assert.True(new InclusiveBetweenValidator<Model, string>(_ => (true, "a"), "a", _ => (false, "z"), "z")
            .Validate("m", new ValidationContext<Model>(new())));
    }

    private static T WithoutMessage<T>(T rule) where T : PropertyValidator
    {
        rule.Options!.ErrorMessage = null;
        return rule;
    }

    [Fact]
    public void ExplicitlyNullUriSchemes_DoNotCreateARestriction()
    {
        var rule = new UriValidator<Model>(UriKind.Absolute, null);
        Assert.True(rule.Validate("custom://host", new ValidationContext<Model>(new())));
    }

    private sealed class Model
    {
        public string Text { get; } = "m";
    }
}