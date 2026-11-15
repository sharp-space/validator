namespace Sharp.Validator.Tests.Results;

// ValidateAndThrow(Async) throws ValidationException only for Error-severity failures; the exception carries every
// recorded entry (warnings included) and formats a stable, LF-joined message.
public sealed class ValidateAndThrowTests
{
    [Fact]
    public void ValidateAndThrow_WhenInvalid_ThrowsWithErrors()
    {
        IValidator<Person> validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        var exception = Assert.Throws<ValidationException>(() => validator.ValidateAndThrow(new Person { Name = "" }));
        Assert.Single(exception.Errors);
        Assert.Contains(" - Name:", exception.Message);
    }

    [Fact]
    public void ValidateAndThrow_WhenValid_DoesNotThrow()
    {
        IValidator<Person> validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        validator.ValidateAndThrow(new Person { Name = "ok" });
    }

    [Fact]
    public void WarningOnlyFailures_DoNotThrow()
    {
        IValidator<Person> validator = new Validator<Person>(x => x.Property(person => person.Name)
            .NotEmpty(options => options.ErrorSeverity = ValidationErrorSeverity.Warning));

        validator.ValidateAndThrow(new Person { Name = "" });
    }

    [Fact]
    public async Task AsyncVariant_ThrowsWithErrors()
    {
        IValidator<Person> validator = new Validator<Person>(x => x.Property(person => person.Name)
            .MustAsync(async _ =>
            {
                await Task.Yield();
                return false;
            }));

        var exception = await Assert.ThrowsAsync<ValidationException>(() => validator.ValidateAndThrowAsync(new Person { Name = "x" }).AsTask());
        Assert.Single(exception.Errors);
    }

    [Fact]
    public void ValidationException_CopiesTheInputArray()
    {
        var errors = new[] { new ValidationError("Before") };
        var exception = new ValidationException(errors);
        errors[0] = new ValidationError("After");

        Assert.Equal("Before", exception.Errors[0].PropertyName);
    }

    [Fact]
    public void ValidationException_ConstructorsBuildJoinedMessages()
    {
        var errors = new[]
        {
            new ValidationError("Name", "is required"),
            new ValidationError("Age", "is too small")
        };

        var withLeadingMessage = new ValidationException("Validation failed", errors);
        Assert.NotSame(errors, withLeadingMessage.Errors);
        Assert.Equal(errors, withLeadingMessage.Errors);
        Assert.StartsWith("Validation failed\n", withLeadingMessage.Message);
        Assert.Contains(" - Name: is required", withLeadingMessage.Message);
        Assert.Contains(" - Age: is too small", withLeadingMessage.Message);

        var errorsOnly = new ValidationException(errors);
        Assert.NotSame(errors, errorsOnly.Errors);
        Assert.Equal(errors, errorsOnly.Errors);
        Assert.Equal("\n - Name: is required\n - Age: is too small", errorsOnly.Message);
    }
}