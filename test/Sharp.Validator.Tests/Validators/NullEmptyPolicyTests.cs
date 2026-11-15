namespace Sharp.Validator.Tests.Validators;

// FluentValidation-parity null/empty policy:
//   length validators short-circuit on null only;
//   format validators (Email, Regex, ...) short-circuit on null and empty string.
public sealed class NullEmptyPolicyTests
{
    [Fact]
    public void Length_ValidatesWhitespaceOnlyStringAgainstMin()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).MinimumLength(5));
        Assert.True(validator.Validate(new Person { Name = null }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "    " }).IsValid); // length 4
        Assert.True(validator.Validate(new Person { Name = "     " }).IsValid); // length 5
    }

    [Fact]
    public void Length_FailsEmptyStringBelowMin()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).MinimumLength(1));
        Assert.False(validator.Validate(new Person { Name = "" }).IsValid);
    }

    [Fact]
    public void Email_EmptyOrNull_Passes()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Email).Email());
        Assert.True(validator.Validate(new Person { Email = "" }).IsValid);
        Assert.True(validator.Validate(new Person { Email = null }).IsValid);
    }

    [Fact]
    public void Regex_EmptyOrNull_Passes()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Regex("^[A-Z]+$"));
        Assert.True(validator.Validate(new Person { Name = "" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = null }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "abc" }).IsValid);
    }
}