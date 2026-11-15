namespace Sharp.Validator.Tests.Pipeline;

// Where(condition, configure) on the builder gates a group of rules: the sync and async overloads, and a group
// condition that is ANDed onto a rule's own condition.
public sealed class BuilderWhereTests
{
    [Fact]
    public void SyncGroupedWhere_WhenGroupConditionIsFalse_SkipsAllRules()
    {
        var validator = new SyncGroupedValidator();

        // Age < 18 -> the whole group is skipped, even though Name/Email are null.
        Assert.True(validator.Validate(new Person { Name = null, Email = null, Age = 10 }).IsValid);
    }

    [Fact]
    public void SyncGroupedWhere_AppliesGroupConditionToAddedRules()
    {
        var validator = new SyncGroupedValidator();

        // Age >= 18, Email "x" passes the per-property guard -> only Name (null) fails.
        var result = validator.Validate(new Person { Name = null, Email = "x", Age = 20 });
        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);
        Assert.Equal("Name", item.PropertyName);
    }

    [Fact]
    public void SyncGroupedWhere_CombinesWithPreExistingPropertyCondition()
    {
        var validator = new SyncGroupedValidator();

        // Email == "skip" fails the per-property guard, so its rule is skipped even though Age >= 18.
        var result = validator.Validate(new Person { Name = "ok", Email = "skip", Age = 20 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task AsyncGroupedWhere_WhenConditionFalse_SkipsGroup()
    {
        var validator = new AsyncGroupedValidator();

        // Age < 18: the group is skipped, so the always-false async rules never run on non-null values.
        Assert.True((await validator.ValidateAsync(new Person { Name = "n", Email = "e", Age = 10 })).IsValid);
    }

    [Fact]
    public async Task AsyncGroupedWhere_RuleCondition_IsCombinedWithTheGroupCondition()
    {
        var validator = new AsyncGroupedValidator();

        // Age >= 18, Email passes guard -> both async rules run and fail.
        // (Values are non-null because the func validator short-circuits to valid on null.)
        var both = await validator.ValidateAsync(new Person { Name = "n", Email = "e", Age = 20 });
        Assert.Equal(2, both.Errors.Count);

        // Email == "skip" fails the AND-combined async guard -> only Name fails.
        var nameOnly = await validator.ValidateAsync(new Person { Name = "n", Email = "skip", Age = 20 });
        var item = Assert.Single(nameOnly.Errors);
        Assert.Equal("Name", item.PropertyName);
    }

    [Fact]
    public async Task AsyncGroupedWhereTokenOverload_GatesRule()
    {
        var validator = new AsyncTokenGroupedValidator();

        // Age > 65 -> group condition false -> Phone rule skipped.
        Assert.True((await validator.ValidateAsync(new Person { Phone = "p", Age = 70 })).IsValid);

        // Age <= 65 -> group condition true -> Phone rule runs and fails.
        Assert.False((await validator.ValidateAsync(new Person { Phone = "p", Age = 30 })).IsValid);
    }

    // Uses the protected parameterless ctor (rules built inside this ctor) and the sync
    // ValidatorBuilder<T>.Where(Func<T,bool>, Action) → Where(Func<T,Context,bool>, Action) chain.
    private sealed class SyncGroupedValidator : Validator<Person>
    {
        public SyncGroupedValidator() => Builder.Where(x => x.Age >= 18, () =>
        {
            // No pre-existing property condition -> Where assigns the group condition directly.
            Builder.Property(x => x.Name).NotEmpty();

            // Pre-existing property condition -> Where AND-combines onto it.
            Builder.Property(x => x.Email).Where(x => x.Email != "skip").NotEmpty();
        });
    }

    // Uses the async ValidatorBuilder<T>.Where(Func<T,Task<bool>>, Action) overload plus a property-level async .Where
    // so the async condition-combine branch runs.
    private sealed class AsyncGroupedValidator : Validator<Person>
    {
        public AsyncGroupedValidator() => Builder.Where(x => Task.FromResult(x.Age >= 18), () =>
        {
            Builder.Property(x => x.Name).MustAsync(_ => Task.FromResult(false));
            Builder.Property(x => x.Email)
                .Where(x => Task.FromResult(x.Email != "skip"))
                .MustAsync(_ => Task.FromResult(false));
        });
    }

    // Uses the async ValidatorBuilder<T>.Where(Func<T,CancellationToken,Task<bool>>, Action) overload.
    private sealed class AsyncTokenGroupedValidator : Validator<Person>
    {
        public AsyncTokenGroupedValidator() => Builder.Where((person, _) => Task.FromResult(person.Age <= 65), () =>
            Builder.Property(x => x.Phone).MustAsync(_ => Task.FromResult(false)));
    }
}