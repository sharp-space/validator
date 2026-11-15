namespace Sharp.Validator.Tests.Validators;

public sealed class CrossPropertyComparisonTests
{
    [Fact]
    public void GreaterThan_WithExpression_ComparesAgainstOtherProperty()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).GreaterThan(person => person.Email));
        Assert.True(validator.Validate(new Person { Name = "z", Email = "a" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "a", Email = "z" }).IsValid);
    }

    [Fact]
    public void GreaterThanOrEqual_WithExpression_ComparesAgainstOtherProperty()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).GreaterThanOrEqual(person => person.Email));
        Assert.True(validator.Validate(new Person { Name = "z", Email = "a" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "z", Email = "z" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "a", Email = "z" }).IsValid);
    }

    [Fact]
    public void LessThan_WithExpression_ComparesAgainstOtherProperty()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).LessThan(person => person.Email));
        Assert.True(validator.Validate(new Person { Name = "a", Email = "z" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "z", Email = "a" }).IsValid);
    }

    [Fact]
    public void LessThanOrEqual_WithExpression_ComparesAgainstOtherProperty()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).LessThanOrEqual(person => person.Email));
        Assert.True(validator.Validate(new Person { Name = "a", Email = "a" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "a", Email = "b" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "b", Email = "a" }).IsValid);
    }
}