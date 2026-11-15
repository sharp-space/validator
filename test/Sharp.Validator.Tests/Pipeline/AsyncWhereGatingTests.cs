namespace Sharp.Validator.Tests.Pipeline;

// A validator-level async Where must mark the builder async and gate the sync rules it wraps.
// Previously the overload never set IsAsync, so Validate() was allowed and the condition was never evaluated on any
// path — wrapped rules ran unconditionally.
public sealed class AsyncWhereGatingTests
{
    [Fact]
    public void ValidatorLevelAsyncWhere_ForcesValidateAsync()
    {
        var validator = new GatedValidator();
        Assert.Throws<InvalidOperationException>(() => validator.Validate(new Person { Age = 20 }));
    }

    [Fact]
    public async Task ValidatorLevelAsyncConditionFalse_SkipsWrappedSyncRules()
    {
        var validator = new GatedValidator();
        var result = await validator.ValidateAsync(new Person { Age = 10, Name = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatorLevelAsyncConditionTrue_RunsWrappedSyncRules()
    {
        var validator = new GatedValidator();
        var result = await validator.ValidateAsync(new Person { Age = 20, Name = null });
        Assert.False(result.IsValid);
        Assert.Equal("Name", result.Errors[0].PropertyName);
    }

    [Fact]
    public async Task PropertyLevelAsyncWhere_GatesSyncRulesInTheChain()
    {
        var validator = new Validator<Person>(x =>
            x.Property(person => person.Name)
                .Where(async person =>
                {
                    await Task.Yield();
                    return person.Age >= 18;
                })
                .NotEmpty());

        Assert.True((await validator.ValidateAsync(new Person { Age = 10, Name = null })).IsValid);

        var gatedIn = await validator.ValidateAsync(new Person { Age = 20, Name = null });
        Assert.False(gatedIn.IsValid);
        Assert.Equal("Name", gatedIn.Errors[0].PropertyName);
    }

    [Fact]
    public async Task PropertyLevelAsyncWhere_GatesMixedSyncAndAsyncRules()
    {
        var asyncRuleRan = false;
        var validator = new Validator<Person>(x =>
            x.Property(person => person.Email)
                .Where(async person =>
                {
                    await Task.Yield();
                    return person.Age >= 18;
                })
                .NotEmpty()
                .MustAsync(async _ =>
                {
                    await Task.Yield();
                    asyncRuleRan = true;

                    return true;
                }));

        var gatedOut = await validator.ValidateAsync(new Person { Age = 10, Email = "" });
        Assert.True(gatedOut.IsValid);
        Assert.False(asyncRuleRan);

        var gatedIn = await validator.ValidateAsync(new Person { Age = 20, Email = "" });
        Assert.False(gatedIn.IsValid);
    }

    [Fact]
    public async Task PropertySingleArgAsyncWhere_GatesRule()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name)
            .Where(x => Task.FromResult(x.Age >= 18))
            .MustAsync(_ => Task.FromResult(false)));

        Assert.True((await validator.ValidateAsync(new Person { Name = "x", Age = 10 })).IsValid);
        Assert.False((await validator.ValidateAsync(new Person { Name = "x", Age = 20 })).IsValid);
    }

    private sealed class GatedValidator : Validator<Person>
    {
        public GatedValidator() => Builder.Where(async person =>
        {
            await Task.Yield();
            return person.Age >= 18;
        }, () => Builder.Property(x => x.Name).NotEmpty());
    }
}