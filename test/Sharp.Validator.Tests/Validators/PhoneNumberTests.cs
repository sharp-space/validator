namespace Sharp.Validator.Tests.Validators;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("010-1234-5678", true)]
    [InlineData("+82 10 1234 5678", true)]
    [InlineData("(02) 1234-5678", true)]
    [InlineData("555.123.4567", true)]
    [InlineData("555 1234 x123", true)] // extension with x
    [InlineData("555 1234 ext 99", true)] // extension with ext
    [InlineData("555 1234 ext. 99", true)] // extension with ext.
    [InlineData("not a number", false)]
    [InlineData("abc-def-ghij", false)]
    [InlineData("- - -", false)] // no digits
    [InlineData("555@123", false)] // an illegal character after a digit
    [InlineData("1234 ext abc", false)] // an extension marker needs digits after it
    [InlineData("555-1234 ext 99", true)]
    public void PhoneNumber_Validates(string input, bool valid)
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Phone).PhoneNumber());
        Assert.Equal(valid, validator.Validate(new Person { Phone = input }).IsValid);
    }

    [Fact]
    public void PhoneNumber_NullOrEmpty_IsValid()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Phone).PhoneNumber());
        Assert.True(validator.Validate(new Person { Phone = null }).IsValid);
        Assert.True(validator.Validate(new Person { Phone = "" }).IsValid);
    }
}