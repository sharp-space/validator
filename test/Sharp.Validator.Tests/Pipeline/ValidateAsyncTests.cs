namespace Sharp.Validator.Tests.Pipeline;

public sealed class ValidateAsyncTests
{
    [Fact]
    public async Task ValidateAsync_WorksWhenOnlyAsyncValidatorsPresent()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name)
            .MustAsync(async (name, _) =>
            {
                await Task.Yield();
                return !string.IsNullOrEmpty(name);
            }));

        var result = await validator.ValidateAsync(new Person { Name = "x" });
        Assert.True(result.IsValid);

        // null reaches the async predicate, which rejects it
        var fail = await validator.ValidateAsync(new Person { Name = null });
        Assert.False(fail.IsValid);

        // empty string reaches the async predicate, which returns false
        var empty = await validator.ValidateAsync(new Person { Name = "" });
        Assert.False(empty.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RunsSyncRulesThenAsyncRules()
    {
        var calls = new List<string>();
        var validator = new Validator<Person>(builder =>
        {
            builder.Property(x => x.Name).Must(_ =>
            {
                calls.Add("sync-name");
                return true;
            });

            builder.Property(x => x.Email).MustAsync(async (_, _) =>
            {
                calls.Add("async-email");
                await Task.Yield();

                return true;
            });
        });

        var result = await validator.ValidateAsync(new Person { Name = "x", Email = "y" });
        Assert.True(result.IsValid);
        Assert.Equal(new[] { "sync-name", "async-email" }, calls);
    }

    [Fact]
    public async Task ValidateAsync_RespectsCascadeModeStopAcrossSyncAndAsync()
    {
        var validator = new Validator<Person>(builder =>
        {
            builder.Property(x => x.Name).NotEmpty();
            builder.Property(x => x.Email).MustAsync(async (_, _) =>
            {
                await Task.Yield();
                return false;
            });
        });

        validator.Configure(x => x.CascadeMode = ValidationCascadeMode.Stop);
        var result = await validator.ValidateAsync(new Person { Name = null, Email = "y" });
        Assert.False(result.IsValid);

        // sync failure stops the cascade before the async rule runs
        Assert.Single(result.Errors);
        Assert.Equal("Name", result.Errors[0].PropertyName);
    }

    [Fact]
    public async Task PropertyCascadeModeStop_AfterSyncFailureOnSameProperty_SkipsAsyncRule()
    {
        var asyncRuleCalled = false;
        var validator = new Validator<Person>(builder => builder
            .Property(person => person.Name, options => options.CascadeMode = ValidationCascadeMode.Stop)
            .NotEmpty()
            .MustAsync(async (_, _) =>
            {
                asyncRuleCalled = true;
                await Task.Yield();

                return false;
            }));

        var result = await validator.ValidateAsync(new Person { Name = "" });
        Assert.False(result.IsValid);
        Assert.False(asyncRuleCalled);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task PropertyCascadeModeContinue_AfterSyncFailureOnSameProperty_RunsAsyncRule()
    {
        var asyncRuleCalled = false;
        var validator = new Validator<Person>(builder => builder
            .Property(person => person.Name, options => options.CascadeMode = ValidationCascadeMode.Continue)
            .NotEmpty()
            .MustAsync(async (_, _) =>
            {
                asyncRuleCalled = true;
                await Task.Yield();

                return false;
            }));

        var result = await validator.ValidateAsync(new Person { Name = "" });
        Assert.False(result.IsValid);
        Assert.True(asyncRuleCalled);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void PropertyOptions_WithoutCascade_DoNotOverrideBuilderCascade()
    {
        var validator = new Validator<Person>(builder =>
        {
            builder.Property(person => person.Name, options => options.PropertyName = "name")
                .NotEmpty()
                .MinimumLength(3);
        });

        validator.Configure(options => options.CascadeMode = ValidationCascadeMode.Stop);
        var result = validator.Validate(new Person { Name = "" });
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Validate_StillThrowsWhenAsyncRulesPresent()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name)
            .MustAsync(async (_, _) =>
            {
                await Task.Yield();
                return true;
            }));

        Assert.Throws<InvalidOperationException>(() => validator.Validate(new Person { Name = "x" }));
    }

    [Fact]
    public async Task InterfaceOverloads_OneArgumentOrContext_UseTheContextModel()
    {
        IValidator<Person> validator = new Validator<Person>(x => x.Property(m => m.Name).NotEmpty());
        Assert.False((await validator.ValidateAsync(new Person())).IsValid);

        var context = new ValidationContext<Person>(new Person { Name = "ok" });
        Assert.True((await validator.ValidateAsync(context)).IsValid);
    }
}