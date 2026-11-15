namespace Sharp.Validator.Result.Tests.Results;

public sealed class ResultTests
{
    [Fact]
    public void ValidationResult_WithoutErrors_IsValid()
    {
        Assert.True(new ValidationResult().IsValid);
        Assert.True(new ValidationResult([]).IsValid);
    }

    [Fact]
    public void ValidationResult_WithAnyError_IsInvalid()
    {
        var result = new ValidationResult([new ValidationError("X", "y")]);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void EmptyResult_IsValidWithNoIndicators()
    {
        var result = new ValidationResult();
        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
        Assert.False(result.HasWarnings);
        Assert.False(result.HasInformation);
    }

    [Fact]
    public void EmptyResult_ReadsAsAnEmptyList()
    {
        Assert.True(ValidationResult.Empty.IsValid);
        Assert.Empty(ValidationResult.Empty.Errors);
        Assert.Empty(ValidationResult.Empty.ToDictionary());
    }

    [Fact]
    public void ToDictionaryGroupsMessages_ByPropertyInReportedOrder()
    {
        var result = new ValidationResult([
            new ValidationError("Name", "required"),
            new ValidationError("Age", "too young", severity: ValidationErrorSeverity.Warning),
            new ValidationError("Name", "too short"),
            new ValidationError("Email")
        ]);

        var dictionary = result.ToDictionary();
        Assert.Equal(["Name", "Age", "Email"], dictionary.Keys);
        Assert.Equal(new[] { "required", "too short" }, dictionary["Name"]);
        Assert.Equal(new[] { "too young" }, dictionary["Age"]);
        Assert.Equal(new[] { "" }, dictionary["Email"]);
    }

    [Fact]
    public void ValidationException_Errors_AreCarriedIntoTheMessage()
    {
        var errors = new[]
        {
            new ValidationError("A", "is bad"),
            new ValidationError("B", "is also bad")
        };

        var exception = new ValidationException(errors);
        Assert.Equal(errors, exception.Errors);
        Assert.Contains("A: is bad", exception.Message);
        Assert.Contains("B: is also bad", exception.Message);
    }

    [Fact]
    public void ValidationError_DefaultsToErrorSeverity()
    {
        var error = new ValidationError("p", "m");
        Assert.Equal(ValidationErrorSeverity.Error, error.Severity);
    }

    [Fact]
    public void ValidationError_ToString_UsesPropertyNameAndMessage()
    {
        var error = new ValidationError("Foo", "bar");
        Assert.Equal("Foo: bar", error.ToString());
    }

    [Fact]
    public void NullErrors_MeanASuccessfulEmptyResult()
    {
        var result = new ValidationResult(null);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal("\nValidation failed, but no errors were recorded.", new ValidationException([]).Message);
    }
}