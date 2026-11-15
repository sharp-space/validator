namespace Sharp.Validator.Tests.Pipeline;

// CascadeMode.Stop must react to the same predicate (an Error-severity entry) on both entry points, and must halt
// later async chains just like the sync loop halts later sync chains. Previously an async failure never stopped
// subsequent async chains.
public sealed class CascadeStopAsyncParityTests
{
    [Fact]
    public async Task WarningFailure_WithGlobalStop_StillRunsAsyncRules()
    {
        var asyncRuleRan = false;
        var validator = new Validator<Person>(x =>
        {
            x.Property(person => person.Name)
                .NotEmpty(options => options.ErrorSeverity = ValidationErrorSeverity.Warning);

            x.Property(person => person.Email)
                .MustAsync(async _ =>
                {
                    await Task.Yield();
                    asyncRuleRan = true;

                    return false;
                });
        });

        validator.Configure(options => options.CascadeMode = ValidationCascadeMode.Stop);
        var result = await validator.ValidateAsync(new Person { Name = "", Email = "x" });
        Assert.Equal(2, result.Errors.Count);
        Assert.False(result.IsValid);
        Assert.True(asyncRuleRan);
    }

    [Fact]
    public async Task ErrorFailure_WithGlobalStop_SkipsAsyncRules()
    {
        var asyncRuleRan = false;
        var validator = new Validator<Person>(x =>
        {
            x.Property(person => person.Name).NotEmpty();
            x.Property(person => person.Email)
                .MustAsync(async _ =>
                {
                    await Task.Yield();
                    asyncRuleRan = true;

                    return false;
                });
        });

        validator.Configure(options => options.CascadeMode = ValidationCascadeMode.Stop);
        var result = await validator.ValidateAsync(new Person { Name = "", Email = "x" });
        Assert.Single(result.Errors);
        Assert.False(asyncRuleRan);
    }

    [Fact]
    // The run ends at the sync phase's first Error, so the failed chain's own async rules never start either. The chain
    // therefore records nothing for the async phase to skip them by.
    public async Task ErrorFailure_WithGlobalStop_SkipsTheFailedChainAsyncRules()
    {
        var asyncRuleRan = false;
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .NotEmpty()
            .MustAsync(_ =>
            {
                asyncRuleRan = true;
                return Task.FromResult(false);
            }));

        validator.Configure(options => options.CascadeMode = ValidationCascadeMode.Stop);
        var result = await validator.ValidateAsync(new Person { Name = "" });
        Assert.Single(result.Errors);
        Assert.False(asyncRuleRan);
    }

    [Fact]
    // The same for a chain whose async condition defers its sync rules into the async phase: the run ends right after
    // them, before the chain's async rules.
    public async Task ErrorFailure_InADeferredChain_WithGlobalStop_SkipsTheChainAsyncRules()
    {
        var asyncRuleRan = false;
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .Where(_ => Task.FromResult(true))
            .NotEmpty()
            .MustAsync(_ =>
            {
                asyncRuleRan = true;
                return Task.FromResult(false);
            }));

        validator.Configure(options => options.CascadeMode = ValidationCascadeMode.Stop);
        var result = await validator.ValidateAsync(new Person { Name = "" });
        Assert.Single(result.Errors);
        Assert.False(asyncRuleRan);
    }

    [Fact]
    public async Task AsyncFailure_WithGlobalStop_SkipsLaterAsyncChains()
    {
        var secondRuleRan = false;
        var validator = new Validator<Person>(x =>
        {
            x.Property(person => person.Name)
                .MustAsync(async _ =>
                {
                    await Task.Yield();
                    return false;
                });

            x.Property(person => person.Email)
                .MustAsync(async _ =>
                {
                    await Task.Yield();
                    secondRuleRan = true;

                    return false;
                });
        });

        validator.Configure(options => options.CascadeMode = ValidationCascadeMode.Stop);
        var result = await validator.ValidateAsync(new Person { Name = "x", Email = "y" });
        var item = Assert.Single(result.Errors);
        Assert.Equal("Name", item.PropertyName);
        Assert.False(secondRuleRan);
    }
}