namespace Sharp.Validator.Tests.Pipeline;

public sealed class WhereChainingTests
{
    [Fact]
    public void SyncChainedWhere_ManyWheres_CombineConditionsWithoutStackOverflow()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name)
            .Where(x => x.Age >= 18)
            .Where(x => x.Age <= 65)
            .NotEmpty());

        Assert.True(validator.Validate(new Person { Name = null, Age = 10 }).IsValid);
        Assert.True(validator.Validate(new Person { Name = null, Age = 80 }).IsValid);

        var result = validator.Validate(new Person { Name = null, Age = 30 });
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task AsyncChainedWhere_ManyWheres_CombineConditionsWithoutStackOverflow()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name)
            .Where(async (_, _) =>
            {
                await Task.Yield();
                return true;
            })
            .Where(async (person, _) =>
            {
                await Task.Yield();
                return person.Age > 0;
            })
            .MustAsync(async (name, _) =>
            {
                await Task.Yield();
                return !string.IsNullOrEmpty(name);
            }));

        var ok = await validator.ValidateAsync(new Person { Name = "x", Age = 1 });
        Assert.True(ok.IsValid);

        var fail = await validator.ValidateAsync(new Person { Name = "", Age = 1 });
        Assert.False(fail.IsValid);
    }

    [Fact]
    public void WhereChain_PassingFirstFailingSecond_SkipsRule()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name)
            .Where(_ => true)
            .Where(_ => false)
            .NotEmpty());

        Assert.True(validator.Validate(new Person { Name = null }).IsValid);
    }

    [Fact]
    // The XML doc used to say "subsequent rules". Where gates the whole chain, the FluentValidation default.
    public void Where_BeforeRules_GatesTheRulesAdded()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name)
            .NotEmpty()
            .Where(x => x.Age >= 18)
            .MaximumLength(1));

        Assert.True(validator.Validate(new Person { Name = "", Age = 10 }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "", Age = 30 }).IsValid);
    }
}