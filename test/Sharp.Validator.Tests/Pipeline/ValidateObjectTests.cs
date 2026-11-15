namespace Sharp.Validator.Tests.Pipeline;

// Validate(object) and ValidateAsync(object) validate a typed model through the untyped entry points. Regression:
// they used to crash with NullReferenceException (or InvalidCastException for a value-type TModel) when given null,
// instead of surfacing a clean ArgumentNullException.
public sealed class ValidateObjectTests
{
    private static Validator<Person> NameRequired() =>
        new(x => x.Property(person => person.Name).NotEmpty());

    [Fact]
    public void ValidateObjectOverload_RunsTypedValidation()
    {
        var result = NameRequired().Validate((object)new Person { Name = null });
        Assert.False(result.IsValid);
        Assert.Equal("Name", result.Errors[0].PropertyName);
    }

    [Fact]
    public async Task ValidateAsyncObjectOverload_RunsTypedValidation()
    {
        var result = await NameRequired().ValidateAsync((object)new Person { Name = null });
        Assert.False(result.IsValid);
        Assert.Equal("Name", result.Errors[0].PropertyName);
    }

    [Fact]
    public void ValidateNullModel_ThrowsArgumentNullException()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        Assert.Throws<ArgumentNullException>(() => validator.Validate((Person)null!));
    }

    [Fact]
    public void ValidateObjectNull_ThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => NameRequired().Validate((object)null!));

    [Fact]
    public async Task ValidateAsyncObjectNull_ThrowsArgumentNullException() =>
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await NameRequired().ValidateAsync((object)null!));
}