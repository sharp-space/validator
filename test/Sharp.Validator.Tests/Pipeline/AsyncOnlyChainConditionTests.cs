namespace Sharp.Validator.Tests.Pipeline;

// The sync phase used to walk every chain, so a chain holding only async rules ran its sync Where there for nothing and
// again in the async phase. The frozen plan leaves such a chain out, and the condition now runs once per validation.
public sealed class AsyncOnlyChainConditionTests
{
    [Fact]
    public async Task SyncCondition_OnAnAsyncOnlyChain_RunsOnce()
    {
        var evaluations = 0;
        var validator = new Validator<Person>(x => x
            .Property(person => person.Name)
            .Where(_ =>
            {
                evaluations++;
                return true;
            })
            .MustAsync(_ => Task.FromResult(false)));

        var result = await validator.ValidateAsync(new Person { Name = "x" });
        Assert.False(result.IsValid);
        Assert.Equal(1, evaluations);
    }

    [Fact]
    public async Task FalseCondition_StillSkipsAnAsyncOnlyChain()
    {
        var validator = new Validator<Person>(x => x
            .Property(person => person.Name)
            .Where(_ => false)
            .MustAsync(_ => Task.FromResult(false)));

        Assert.True((await validator.ValidateAsync(new Person { Name = "x" })).IsValid);
    }
}