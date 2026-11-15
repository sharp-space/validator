namespace Sharp.Validator.Tests.Validators;

// Password counts stop at the required number instead of walking the whole value. A boundary off by one here would
// accept or reject exactly at the threshold, so these pin it, along with the Unicode classification the rules document.
public sealed class PasswordThresholdTests
{
    private static bool Digits(int required, string password) =>
        new Validator<Person>(x => x.Property(person => person.Password)
                .Add(new PasswordDigitCountValidator<Person>(required)))
            .Validate(new Person { Password = password }).IsValid;

    [Theory]
    [InlineData(2, "a1", false)]
    [InlineData(2, "a1b2", true)]
    [InlineData(2, "1a2b3", true)]
    [InlineData(0, "abc", true)]
    public void Count_StopsExactlyAtTheThreshold(int required, string password, bool expected) =>
        Assert.Equal(expected, Digits(required, password));

    [Fact]
    public void Digits_AreClassifiedAsUnicode() => Assert.True(Digits(2, "٣٣"));
}