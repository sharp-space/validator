namespace Sharp.Validator.Tests.Validators;

public sealed class NotEqualTests
{
    [Fact]
    public void NotEqualValue_WithoutComparer_FailsOnEqualInput()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEqual("Alice"));
        Assert.False(validator.Validate(new Person { Name = "Alice" }).IsValid);
    }

    [Fact]
    public void NotEqualValue_WithoutComparer_PassesOnDifferentInput()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEqual("Alice"));
        Assert.True(validator.Validate(new Person { Name = "Bob" }).IsValid);
    }

    [Fact]
    public void NotEqualValue_WithComparer_UsesComparer()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .NotEqual("ALICE", StringComparer.OrdinalIgnoreCase));

        Assert.False(validator.Validate(new Person { Name = "alice" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "bob" }).IsValid);
    }

    [Fact]
    public void NotEqual_SkipsNull()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEqual("Alice"));
        Assert.True(validator.Validate(new Person { Name = null }).IsValid);
    }

    [Fact]
    public void Equal_StillWorksNormally()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Equal("Alice"));
        Assert.True(validator.Validate(new Person { Name = "Alice" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "Bob" }).IsValid);
    }

    [Fact]
    // NotEqualValidator func-target reporting path: value equals the func target → fails and the {TargetValue}
    // placeholder is surfaced.
    public void NotEqual_CrossPropertyTarget_NamesItsTargetOnFailure()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEqual(person => person.Email));

        var result = validator.Validate(new Person { Name = "dup", Email = "dup" });
        Assert.False(result.IsValid);
        Assert.Equal("NotEqual", result.Errors[0].Code);
        Assert.Equal("'Name' must not be equal to 'Email'.", result.Errors[0].Message); // the target's name, not its value
        Assert.True(validator.Validate(new Person { Name = "a", Email = "b" }).IsValid);
    }
}