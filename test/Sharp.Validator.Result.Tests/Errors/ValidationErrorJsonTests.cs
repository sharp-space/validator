using System.Text.Json;

namespace Sharp.Validator.Result.Tests.Errors;

// Regression: the constructor took ValidationErrorSeverity? while the Severity property is ValidationErrorSeverity, so
// System.Text.Json refused to bind it and a ValidationError could not be read back at all, for example by a client
// reading a 422 body. A contract type has to survive the trip it was made for.
public sealed class ValidationErrorJsonTests
{
    [Fact]
    public void Errors_WithWebOptions_RoundTrip()
    {
        var error = new ValidationError("Name", "required", "NotEmpty", ValidationErrorSeverity.Warning);
        var read = JsonSerializer.Deserialize<ValidationError>(JsonSerializer.Serialize(error, JsonSerializerOptions.Web),
            JsonSerializerOptions.Web)!;

        Assert.Equal(("Name", "required", "NotEmpty", ValidationErrorSeverity.Warning), (read.PropertyName, read.Message, read.Code, read.Severity));
    }

    [Fact]
    public void MissingSeverity_ReadsAsError()
    {
        var read = JsonSerializer.Deserialize<ValidationError>("""{"propertyName":"Name"}""", JsonSerializerOptions.Web)!;
        Assert.Equal(ValidationErrorSeverity.Error, read.Severity);
    }

    [Fact]
    public void NumericSeverity_FromOlderPayloads_Reads()
    {
        var read = JsonSerializer.Deserialize<ValidationError>("""{"propertyName":"Name","severity":1}""", JsonSerializerOptions.Web)!;
        Assert.Equal(ValidationErrorSeverity.Warning, read.Severity);
    }

    [Fact]
    public void SourceGeneratedContext_RoundTripsErrors()
    {
        var error = new ValidationError("Name", "required", "NotEmpty", ValidationErrorSeverity.Information);
        var json = JsonSerializer.Serialize(error, ResultJsonContext.Default.ValidationError);
        var read = JsonSerializer.Deserialize(json, ResultJsonContext.Default.ValidationError)!;
        Assert.Equal(ValidationErrorSeverity.Information, read.Severity);
    }

    [Fact]
    public void ListOfErrors_RoundTrips()
    {
        ValidationError[] errors = [new("A", "a"), new("B", "b", severity: ValidationErrorSeverity.Warning)];
        var read = JsonSerializer.Deserialize<ValidationError[]>(
            JsonSerializer.Serialize(errors, JsonSerializerOptions.Web),
            JsonSerializerOptions.Web)!;

        Assert.Equal([ValidationErrorSeverity.Error, ValidationErrorSeverity.Warning], read.Select(x => x.Severity));
    }
}