namespace Sharp.Validator.Tests.Validators;

// MustAsync((value, token) => ...) reached its predicate through a lambda that dropped the context and passed the token
// on. The validator now calls such a predicate itself. These pin, for both overloads that take a token, that the
// predicate gets the one ValidateAsync was given: with any other, it couldn't stop work the caller has canceled.
public sealed class PredicateTokenTests
{
    [Fact]
    public async Task MustAsync_PredicateTakesAToken_ReceivesTheCallersToken()
    {
        using var source = new CancellationTokenSource();
        var received = CancellationToken.None;
        var validator = new Validator<Person>(x => x.Property(person => person.Name).MustAsync((_, token) =>
        {
            received = token;
            return Task.FromResult(true);
        }));

        await validator.ValidateAsync(new Person { Name = "a" }, source.Token);

        Assert.Equal(source.Token, received);
    }

    [Fact]
    public async Task MustAsync_PredicateTakesTheContextAndAToken_ReceivesTheCallersToken()
    {
        using var source = new CancellationTokenSource();
        var received = CancellationToken.None;
        var validator = new Validator<Person>(x => x.Property(person => person.Name).MustAsync((_, _, token) =>
        {
            received = token;
            return Task.FromResult(true);
        }));

        await validator.ValidateAsync(new Person { Name = "a" }, source.Token);

        Assert.Equal(source.Token, received);
    }
}