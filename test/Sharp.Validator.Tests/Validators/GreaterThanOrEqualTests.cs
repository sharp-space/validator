namespace Sharp.Validator.Tests.Validators;

public sealed class GreaterThanOrEqualTests
{
    [Fact]
    public void GreaterThanOrEqual_ValueTypeWithExplicitComparer_ComparesInclusively()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Age)
            .GreaterThanOrEqual(18, Comparer<int>.Default));

        Assert.True(validator.Validate(new Person { Age = 18 }).IsValid);
        Assert.True(validator.Validate(new Person { Age = 19 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 17 }).IsValid);
    }

    [Fact]
    public void GreaterThanOrEqual_StringWithExplicitComparer_ComparesInclusively()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .GreaterThanOrEqual("m", StringComparer.Ordinal));

        Assert.False(validator.Validate(new Person { Name = "a" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "m" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "z" }).IsValid);
    }

    [Fact]
    public void GreaterThanOrEqual_DefaultOverloadStillWorks()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).GreaterThanOrEqual(0));
        Assert.True(validator.Validate(new Person { Age = 0 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = -1 }).IsValid);
    }
}