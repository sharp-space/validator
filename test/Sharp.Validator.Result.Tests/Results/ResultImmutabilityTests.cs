namespace Sharp.Validator.Result.Tests.Results;

// A result kept the list it was given and handed that same list out through Errors, so changing the caller's list, or
// casting Errors back to a List and clearing it, turned an invalid result valid. A result now copies what a caller
// gives it and hands out a read-only view, so nothing outside it can change it.
public sealed class ResultImmutabilityTests
{
    private static readonly ValidationError Error = new("Name", "'Name' must not be empty.");

    [Fact]
    public void ChangingTheSourceCollection_DoesNotChangeTheBuiltResult()
    {
        List<ValidationError> errors = [Error];
        var result = new ValidationResult(errors);
        errors.Clear();

        Assert.False(result.IsValid);
        Assert.Equal([Error], result.Errors);
    }

    [Fact]
    public void Errors_IsAViewThatCannotChangeTheResult()
    {
        var result = new ValidationResult([Error]);
        Assert.IsNotType<List<ValidationError>>(result.Errors, false);
        Assert.Throws<NotSupportedException>(() => ((IList<ValidationError>)result.Errors).Clear());
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ReadOfErrors_ReturnsTheSameView()
    {
        var result = new ValidationResult([Error]);
        Assert.Same(result.Errors, result.Errors);
    }

    [Fact]
    public void Result_CanBeBuiltFromAnySequence()
    {
        var result = new ValidationResult(Enumerable.Repeat(Error, 2));
        Assert.Equal(2, result.Errors.Count);
        Assert.True(new ValidationResult(Enumerable.Empty<ValidationError>()).IsValid);
    }
}