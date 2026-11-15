namespace Sharp.Validator.Result;

/// <summary>Exception that wraps a set of <see cref="ValidationError" />s for throwing-on-failure flows.</summary>
public class ValidationException : Exception
{
    /// <summary>Creates an exception with a leading message followed by the formatted error list.</summary>
    public ValidationException(string message, ValidationError[] errors) : base($"{message}\n{BuildErrorMessage(errors)}") => Errors = [.. errors];

    /// <summary>Creates an exception whose message is the formatted error list.</summary>
    public ValidationException(ValidationError[] errors) : base('\n' + BuildErrorMessage(errors)) => Errors = [.. errors];

    /// <summary>The errors that triggered this exception.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    // Use LF explicitly so the resulting message is stable across Windows/Linux/macOS hosts — makes assertions in tests
    // and log scrapers deterministic. Null-checked here because this runs inside the base-constructor call — the
    // alternative is an NRE thrown while constructing the exception itself, the worst possible diagnostic.
    private static string BuildErrorMessage(ValidationError[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Length == 0
            ? "Validation failed, but no errors were recorded."
            : string.Join('\n', errors.Select(x => $" - {x}"));
    }
}