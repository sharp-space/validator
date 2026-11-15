using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sharp.Validator.Result.Tests.Errors;

// ValidationError is the body of a 422 response, so its JSON is a contract. Severity used to be written as a number,
// which made member order part of that contract and left clients to know that 0 meant Error. It is written by member
// name now; an app's own enum converter still decides when it has one, and numbers are still read.
public sealed class SeveritySerializationTests
{
    private static readonly ValidationError Error = new("Name", "required", "NotEmpty");

    [Fact]
    public void DefaultOptions_WriteTheMemberName() =>
        Assert.Contains("\"Severity\":\"Error\"", JsonSerializer.Serialize(Error));

    [Fact]
    public void WebOptions_WriteTheMemberName() =>
        Assert.Contains("\"severity\":\"Error\"", JsonSerializer.Serialize(Error, JsonSerializerOptions.Web));

    [Theory]
    [InlineData(ValidationErrorSeverity.Error, "\"Error\"")]
    [InlineData(ValidationErrorSeverity.Warning, "\"Warning\"")]
    [InlineData(ValidationErrorSeverity.Information, "\"Information\"")]
    public void Member_IsWrittenByName(ValidationErrorSeverity severity, string expected) =>
        Assert.Equal(expected, JsonSerializer.Serialize(severity));

    [Fact]
    public void AppConverter_TakesPrecedence()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        Assert.Contains("\"severity\":\"error\"", JsonSerializer.Serialize(Error, options));
    }

    [Theory]
    [InlineData("\"Error\"", ValidationErrorSeverity.Error)]
    [InlineData("\"Warning\"", ValidationErrorSeverity.Warning)]
    [InlineData("\"information\"", ValidationErrorSeverity.Information)]
    [InlineData("0", ValidationErrorSeverity.Error)]
    [InlineData("2", ValidationErrorSeverity.Information)]
    public void Severity_NameOrNumber_Deserializes(string json, ValidationErrorSeverity expected) =>
        Assert.Equal(expected, JsonSerializer.Deserialize<ValidationErrorSeverity>(json));

    [Fact]
    public void SourceGeneratedContext_WritesTheMemberName() =>
        Assert.Contains("\"severity\":\"Error\"", JsonSerializer.Serialize(Error, ResultJsonContext.Default.ValidationError));
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ValidationError))]
internal sealed partial class ResultJsonContext : JsonSerializerContext;