namespace Sharp.Validator.Tests.Pipeline;

// Where((model, token) => ...) reached its condition through a lambda that dropped the context and passed the token on.
// The gate now calls such a condition itself. These pin, for a chain's condition and a block's, that the condition gets
// the token ValidateAsync was given: with any other, it couldn't stop work the caller has canceled.
public sealed class WhereTokenTests
{
    [Fact]
    public async Task ChainWhere_ConditionTakesAToken_ReceivesTheCallersToken()
    {
        using var source = new CancellationTokenSource();
        var received = CancellationToken.None;
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Where((_, token) =>
        {
            received = token;
            return Task.FromResult(true);
        }).NotEmpty());

        await validator.ValidateAsync(new Person { Name = "a" }, source.Token);

        Assert.Equal(source.Token, received);
    }

    [Fact]
    public async Task BlockWhere_ConditionTakesAToken_ReceivesTheCallersToken()
    {
        using var source = new CancellationTokenSource();
        var received = CancellationToken.None;
        var validator = new Validator<Person>(x => x.Where((_, token) =>
        {
            received = token;
            return Task.FromResult(true);
        }, () => x.Property(person => person.Name).NotEmpty()));

        await validator.ValidateAsync(new Person { Name = "a" }, source.Token);

        Assert.Equal(source.Token, received);
    }
}