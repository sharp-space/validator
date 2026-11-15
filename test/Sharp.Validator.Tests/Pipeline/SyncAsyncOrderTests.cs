namespace Sharp.Validator.Tests.Pipeline;

// The README promises that ValidateAsync runs the sync rules first, then the async ones, whatever order a chain or the
// validator declares them in. The pre-release review kept that order over declaration order: it decides which rules a
// Stop cascade skips and when side effects run, so changing it is an API decision, not a pipeline cleanup.
public sealed class SyncAsyncOrderTests
{
    [Fact]
    public async Task SyncRuleDeclaredAfterAnAsyncRuleInItsChain_RunsFirst()
    {
        var ran = new List<string>();
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .MustAsync((_, _) =>
            {
                ran.Add("async");
                return Task.FromResult(true);
            })
            .Must(_ =>
            {
                ran.Add("sync");
                return true;
            }));

        await validator.ValidateAsync(new Person { Name = "a" });

        Assert.Equal(["sync", "async"], ran);
    }

    [Fact]
    public async Task ChainSyncRules_RunBeforeAnyAsyncRule()
    {
        var ran = new List<string>();
        var validator = new Validator<Person>(x =>
        {
            x.Property(person => person.Name).MustAsync((_, _) =>
            {
                ran.Add("Name async");
                return Task.FromResult(true);
            });

            x.Property(person => person.Email).Must(_ =>
            {
                ran.Add("Email sync");
                return true;
            });
        });

        await validator.ValidateAsync(new Person { Name = "a", Email = "b" });

        Assert.Equal(["Email sync", "Name async"], ran);
    }

    [Fact]
    public async Task SyncFailuresComeFirst_InTheResult()
    {
        var validator = new Validator<Person>(x =>
        {
            x.Property(person => person.Name)
                .MustAsync((_, _) => Task.FromResult(false), options => options.ErrorMessage = "async");

            x.Property(person => person.Email).Must(_ => false, options => options.ErrorMessage = "sync");
        });

        var result = await validator.ValidateAsync(new Person());
        Assert.Equal(["sync", "async"], result.Errors.Select(error => error.Message));
    }

    [Fact]
    public async Task StopCascade_SkipsAnAsyncRuleDeclaredBeforeAFailingSyncRule()
    {
        var asyncRan = false;
        var validator = new Validator<Person>(x => x
            .Property(person => person.Name, options => options.CascadeMode = ValidationCascadeMode.Stop)
            .MustAsync((_, _) =>
            {
                asyncRan = true;
                return Task.FromResult(true);
            })
            .Must(_ => false));

        Assert.Single((await validator.ValidateAsync(new Person())).Errors);
        Assert.False(asyncRan);
    }

    private sealed class Person
    {
        public string? Name { get; init; }

        public string? Email { get; init; }
    }
}