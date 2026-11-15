namespace Sharp.Validator.Tests.Validators;

// Regression: UriValidator kept the caller's array, so changing it after configuration changed a validator that README
// promises is snapshotted.
public sealed class UriSchemeSnapshotTests
{
    [Fact]
    public void ChangingTheSchemesArrayAfterwards_HasNoEffect()
    {
        var schemes = new[] { "https" };
        var validator = new Validator<Person>(x => x.Property(m => m.Url).Uri(UriKind.Absolute, schemes));

        schemes[0] = "http";

        Assert.False(validator.Validate(new Person { Url = "http://example.com" }).IsValid);
        Assert.True(validator.Validate(new Person { Url = "https://example.com" }).IsValid);
    }
}