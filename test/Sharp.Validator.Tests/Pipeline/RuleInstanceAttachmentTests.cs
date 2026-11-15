namespace Sharp.Validator.Tests.Pipeline;

// A rule instance carries its own options and the names it settles when its validator freezes, so the README asks for
// one instance per chain. Nothing enforced it: adding the same instance to a second chain overwrote the first chain's
// options without a word. The second Add now throws, so the mistake surfaces where it is made. Lifting the restriction
// later, for rule definitions that keep per-chain options apart, wouldn't break anyone.
public sealed class RuleInstanceAttachmentTests
{
    [Fact]
    public void AddingOneRuleInstanceToASecondChain_Throws()
    {
        var rule = new SkuRule();
        var exception = Assert.Throws<InvalidOperationException>(() => new Validator<Person>(x =>
        {
            x.Property(person => person.Name).Add(rule);
            x.Property(person => person.Email).Add(rule);
        }));

        Assert.Contains("own instance", exception.Message);
    }

    [Fact]
    public void RuleInstanceSharedByTwoValidators_ThrowsWhenTheSecondTakesIt()
    {
        var rule = new SkuRule();
        _ = new Validator<Person>(x => x.Property(person => person.Name).Add(rule));

        Assert.Throws<InvalidOperationException>(() => new Validator<Person>(x => x.Property(person => person.Name).Add(rule)));
    }

    [Fact]
    public void NewInstance_PerChain_IsFine()
    {
        var validator = new Validator<Person>(x =>
        {
            x.Property(person => person.Name).Add(new SkuRule(), options => options.ErrorCode = "NameSku");
            x.Property(person => person.Email).Add(new SkuRule(), options => options.ErrorCode = "EmailSku");
        });

        var result = validator.Validate(new Person { Name = "x", Email = "y" });
        Assert.Equal(["NameSku", "EmailSku"], result.Errors.Select(error => error.Code));
    }

    private sealed class SkuRule : PropertyValidator<Person, string?>
    {
        public override bool Validate(string? value, ValidationContext<Person> context) =>
            value is null || value.StartsWith("SKU-", StringComparison.Ordinal);
    }
}