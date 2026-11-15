using System.Text.Json.Serialization;

namespace Sharp.Validator.Result;

/// <summary>Severity of a validation failure. System.Text.Json writes it by member name, such as <c>"Error"</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValidationErrorSeverity>))]
// Written by name, the JSON doesn't turn member order into a wire contract or leave clients a number table to know.
// The generic converter is trim- and AOT-safe and still reads numbers. A converter in JsonSerializerOptions.Converters
// takes precedence over this attribute, so an app that configures its own enum naming keeps it.
public enum ValidationErrorSeverity
{
    /// <summary>Hard failure. Default. Causes <see cref="ValidationResult.IsValid" /> to be <c>false</c>.</summary>
    Error,
    /// <summary>
    /// A soft failure shown to the user. Does not affect <see cref="ValidationResult.IsValid" />; inspect <see cref="ValidationResult.HasWarnings" />
    /// to see them.
    /// </summary>
    Warning,
    /// <summary>
    /// Informational notice. Does not affect <see cref="ValidationResult.IsValid" />; inspect <see cref="ValidationResult.HasInformation" /> to see them.
    /// </summary>
    Information
}