namespace Sharp.Validator.Tests.Validators;

// Must and MustAsync skipped null like the built-in rules, so Must(value => value != null) passed for null and a
// predicate couldn't express a check about null at all. A predicate now receives every value, null included, as in
// FluentValidation, and decides what null means; a string? parameter makes the compiler ask for that. The built-in
// rules still skip null.
public sealed class PredicateNullTests
{
    [Fact]
    public void Must_ReceivesNull()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Must(name => name != null));
        Assert.False(validator.Validate(new Person { Name = null }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "a" }).IsValid);
    }

    [Fact]
    public async Task MustAsync_ReceivesNull()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .MustAsync((name, _) => Task.FromResult(name != null)));

        Assert.False((await validator.ValidateAsync(new Person { Name = null })).IsValid);
        Assert.True((await validator.ValidateAsync(new Person { Name = "a" })).IsValid);
    }

    [Fact]
    public void NullElement_ReachesThePredicate()
    {
        var seen = new List<string?>();
        var validator = new Validator<Person>(x => x.Elements(person => person.Tags).Must(tag =>
        {
            seen.Add(tag);
            return tag != null;
        }));

        var error = Assert.Single(validator.Validate(new Person { Tags = ["a", null] }).Errors);
        Assert.Equal("Tags[1]", error.PropertyName);
        Assert.Equal(["a", null], seen);
    }

    [Fact]
    public void BuiltInRule_StillSkipsNull()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).MaximumLength(3));
        Assert.True(validator.Validate(new Person { Name = null }).IsValid);
    }
}