namespace Sharp.Validator.Tests.Validators;

public sealed class CreditCardTests
{
    [Theory]
    [InlineData("4242424242424242", true)] // Visa test card (16 digits, valid Luhn)
    [InlineData("4242 4242 4242 4242", true)] // spaces allowed
    [InlineData("4242-4242-4242-4242", true)] // dashes allowed
    [InlineData("4242424242424241", false)] // wrong Luhn
    [InlineData("---", false)] // separators only — must not pass after stripping
    [InlineData("0", false)] // single digit — passes Luhn but below min length
    [InlineData("000", false)] // below min length
    [InlineData("12345", false)] // too short
    [InlineData("12345678901234567890", false)] // too long (>19)
    [InlineData("4242424242424242a", false)] // letters
    [InlineData("    ", false)] // whitespace
    public void CreditCard_Input_ChecksFormatWithLuhn(string input, bool valid)
    {
        var validator = new Validator<Person>(x => x.Property(person => person.CardNumber).CreditCard());
        Assert.Equal(valid, validator.Validate(new Person { CardNumber = input }).IsValid);
    }

    [Fact]
    public void CreditCard_NullOrEmpty_IsValid()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.CardNumber).CreditCard());
        Assert.True(validator.Validate(new Person { CardNumber = null }).IsValid);
        Assert.True(validator.Validate(new Person { CardNumber = "" }).IsValid);
    }
}