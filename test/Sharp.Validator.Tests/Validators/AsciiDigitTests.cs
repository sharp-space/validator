namespace Sharp.Validator.Tests.Validators;

// CreditCard and PhoneNumber must only accept ASCII digits. char.IsDigit accepts Unicode decimal digits (e.g.
// Arabic-Indic U+0660..U+0669), whose code points corrupted the Luhn arithmetic — a hostile Unicode-digit string could
// even checksum to "valid".
public sealed class AsciiDigitTests
{
    [Fact]
    public void CreditCard_RejectsUnicodeDigits()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.CardNumber).CreditCard());
        Assert.True(validator.Validate(new Person { CardNumber = "4539148803436467" }).IsValid);
        Assert.False(validator.Validate(new Person { CardNumber = "4539١48803436467" }).IsValid);
        Assert.False(validator.Validate(new Person { CardNumber = "٤٥٣٩١٤٨٨٠٣٤٣٦٤٦٧" }).IsValid);
    }

    [Fact]
    public void PhoneNumber_RejectsUnicodeDigits()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Phone).PhoneNumber());
        Assert.True(validator.Validate(new Person { Phone = "+1 (555) 123-4567" }).IsValid);
        Assert.False(validator.Validate(new Person { Phone = "٥٥٥١٢٣٤" }).IsValid);
    }
}