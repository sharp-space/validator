namespace Sharp.Validator.Tests.Results;

// Regression: ValidationResult.IsValid used to count every entry as a failure regardless of severity, contradicting the
// ValidationErrorSeverity enum docs. The fixed contract:
//   - Error    => IsValid = false
//   - Warning  => IsValid stays true, surface via HasWarnings
//   - Info     => IsValid stays true, surface via HasInformation
public sealed class SeverityIsValidTests
{
    [Fact]
    public void WarningSeverity_DoesNotInvalidate()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .NotEmpty(x => x.ErrorSeverity = ValidationErrorSeverity.Warning));

        var result = validator.Validate(new Person { Name = null });
        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
        Assert.True(result.HasWarnings);
        Assert.False(result.HasInformation);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void InformationSeverity_DoesNotInvalidate()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .NotEmpty(x => x.ErrorSeverity = ValidationErrorSeverity.Information));

        var result = validator.Validate(new Person { Name = null });
        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
        Assert.False(result.HasWarnings);
        Assert.True(result.HasInformation);
    }

    [Fact]
    public void ErrorSeverity_Invalidates()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        var result = validator.Validate(new Person { Name = null });
        Assert.False(result.IsValid);
        Assert.True(result.HasErrors);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void MixedSeverities_IsValidReflectsOnlyErrors()
    {
        var validator = new Validator<Person>(builder =>
        {
            builder.Property(x => x.Name)
                .NotEmpty(x => x.ErrorSeverity = ValidationErrorSeverity.Warning);

            builder.Property(x => x.Email).NotEmpty();
        });

        var result = validator.Validate(new Person { Name = null, Email = null });
        Assert.False(result.IsValid); // Email error blocks
        Assert.True(result.HasErrors);
        Assert.True(result.HasWarnings);
        Assert.Equal(2, result.Errors.Count);
    }
}