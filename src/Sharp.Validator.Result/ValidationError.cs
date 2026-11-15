namespace Sharp.Validator.Result;

/// <summary>
/// A single validation failure. Immutable, so a rule with a fixed message reports the same instance from every validation. Two errors are equal when
/// their name, message, code, and severity are, compared ordinally, so whether an instance is shared never shows outside
/// <see cref="object.ReferenceEquals" />.
/// </summary>
// Each parameter has its property's exact type, so System.Text.Json can bind this constructor when reading an error
// back; a ValidationErrorSeverity? parameter made deserialization throw.
public sealed class ValidationError(
    string propertyName,
    string? message = null,
    string? code = null,
    ValidationErrorSeverity severity = ValidationErrorSeverity.Error
) : IEquatable<ValidationError>
{
    /// <summary>Reported property name. May include prefixes/suffixes set on the parent builder.</summary>
    public string PropertyName { get; } = propertyName;

    /// <summary>Resolved error message with placeholders substituted.</summary>
    public string? Message { get; } = message;

    /// <summary>Stable identifier for the failure type (e.g. <c>NotEmpty</c>, <c>Email</c>).</summary>
    public string? Code { get; } = code;

    /// <summary>Severity. Defaults to <see cref="ValidationErrorSeverity.Error" />.</summary>
    public ValidationErrorSeverity Severity { get; } = severity;

    /// <inheritdoc />
    public bool Equals(ValidationError? other) =>
        other is not null &&
        (ReferenceEquals(this, other) ||
            (Severity == other.Severity &&
                string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal) &&
                string.Equals(Message, other.Message, StringComparison.Ordinal) &&
                string.Equals(Code, other.Code, StringComparison.Ordinal)));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ValidationError);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(PropertyName, Message, Code, Severity);

    /// <summary>True when both are <c>null</c> or <see cref="Equals(ValidationError?)" /> holds.</summary>
    public static bool operator ==(ValidationError? left, ValidationError? right) =>
        left?.Equals(right) ?? right is null;

    /// <summary>The negation of <c>==</c>.</summary>
    public static bool operator !=(ValidationError? left, ValidationError? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => $"{PropertyName}: {Message ?? nameof(ValidationError)}";
}