namespace Sharp.Validator.Tests.Validators;

// Regression: NotEqual(null) + value == null incorrectly passed because the validator short-circuited every null value.
// When both sides are null they are equal, so NotEqual must fail rather than skip.
public sealed class NotEqualNullTargetTests
{
    [Fact]
    public void NotEqual_NullTargetNullValue_Fails()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEqual((string?)null));
        Assert.False(validator.Validate(new Person { Name = null }).IsValid);
    }

    [Fact]
    public void NotEqual_NullTargetNonNullValue_Passes()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEqual((string?)null));
        Assert.True(validator.Validate(new Person { Name = "x" }).IsValid);
    }

    [Fact]
    public void NotEqual_FuncReturningNullNullValue_Fails()
    {
        // Email is null on Person by default; NotEqual(x => x.Email) with Name = null reduces to null vs null.
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEqual(person => person.Email));
        Assert.False(validator.Validate(new Person { Name = null, Email = null }).IsValid);
    }

    [Fact]
    public void NotEqual_FuncReturningNonNullNullValue_Skips()
    {
        // Backwards-compatible: skip-null convention preserved when the target side is non-null.
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEqual(person => person.Email));
        Assert.True(validator.Validate(new Person { Name = null, Email = "x" }).IsValid);
    }
}