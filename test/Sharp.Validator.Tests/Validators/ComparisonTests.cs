namespace Sharp.Validator.Tests.Validators;

public sealed class ComparisonTests
{
    [Fact]
    public void GreaterThan_ConstantValue_RejectsEqualOrBelow()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).GreaterThan(10));
        Assert.False(validator.Validate(new Person { Age = 5 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 10 }).IsValid);
        Assert.True(validator.Validate(new Person { Age = 11 }).IsValid);
    }

    [Fact]
    public void GreaterThanOrEqual_ConstantValue_AcceptsEqual()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).GreaterThanOrEqual(10));
        Assert.False(validator.Validate(new Person { Age = 9 }).IsValid);
        Assert.True(validator.Validate(new Person { Age = 10 }).IsValid);
        Assert.True(validator.Validate(new Person { Age = 11 }).IsValid);
    }

    [Fact]
    public void LessThan_ConstantValue_RejectsEqualOrAbove()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).LessThan(10));
        Assert.True(validator.Validate(new Person { Age = 9 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 10 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 11 }).IsValid);
    }

    [Fact]
    public void LessThanOrEqual_ConstantValue_AcceptsEqual()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).LessThanOrEqual(10));
        Assert.True(validator.Validate(new Person { Age = 9 }).IsValid);
        Assert.True(validator.Validate(new Person { Age = 10 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 11 }).IsValid);
    }

    [Fact]
    public void GreaterThan_OtherProperty_ComparesLexicographically()
    {
        // Name must be greater than Email lexicographically
        var validator = new Validator<Person>(x => x.Property(person => person.Name).GreaterThan(person => person.Email));
        Assert.True(validator.Validate(new Person { Name = "z", Email = "a" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "a", Email = "z" }).IsValid);
    }

    [Fact]
    public void InclusiveBetween_BothBounds_AreAccepted()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).InclusiveBetween(1, 10));
        Assert.False(validator.Validate(new Person { Age = 0 }).IsValid);
        Assert.True(validator.Validate(new Person { Age = 1 }).IsValid);
        Assert.True(validator.Validate(new Person { Age = 5 }).IsValid);
        Assert.True(validator.Validate(new Person { Age = 10 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 11 }).IsValid);
    }

    [Fact]
    public void ExclusiveBetween_BothBounds_AreRejected()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).ExclusiveBetween(1, 10));
        Assert.False(validator.Validate(new Person { Age = 1 }).IsValid);
        Assert.True(validator.Validate(new Person { Age = 2 }).IsValid);
        Assert.True(validator.Validate(new Person { Age = 9 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 10 }).IsValid);
    }

    [Fact]
    public void GreaterThan_WithIgnoreCaseComparer_UsesTheComparer()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .GreaterThan("M", StringComparer.OrdinalIgnoreCase));

        Assert.False(validator.Validate(new Person { Name = "abc" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "ZZZ" }).IsValid);
    }

    [Fact]
    public void Equal_WithIgnoreCaseComparer_UsesTheComparer()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Equal("ALICE", StringComparer.OrdinalIgnoreCase));
        Assert.True(validator.Validate(new Person { Name = "alice" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "bob" }).IsValid);
    }

    [Fact]
    public void Equal_PropertyTarget_ComparesBothProperties()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Equal(person => person.Email));
        Assert.True(validator.Validate(new Person { Name = "x", Email = "x" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "x", Email = "y" }).IsValid);
    }
}