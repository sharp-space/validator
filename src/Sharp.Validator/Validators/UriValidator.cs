namespace Sharp.Validator.Validators;

/// <summary>
/// Value must parse as a URI of the given <see cref="UriKind" />, optionally restricted to a scheme allowlist. With
/// <see cref="UriKind.RelativeOrAbsolute" /> only absolute values are scheme-checked — relative URIs have no scheme. Surrounding whitespace and control
/// characters are rejected, and an absolute URI needs a written scheme, so the verdict doesn't depend on the host OS's file paths.
/// </summary>
public sealed class UriValidator<TModel> : PropertyValidator<TModel, string?>
{
    private readonly string[]? _allowedSchemes;
    private readonly UriKind _kind;

    /// <summary>
    /// Creates the validator. Throws when schemes are supplied for <see cref="UriKind.Relative" /> — relative URIs have no scheme, so the restriction
    /// could never apply.
    /// </summary>
    public UriValidator(UriKind kind = UriKind.Absolute, params string[]? allowedSchemes)
    {
        _kind = kind;
        _allowedSchemes = allowedSchemes is { Length: > 0 } ? [.. allowedSchemes] : null;
        if (kind == UriKind.Relative && _allowedSchemes != null)
        {
            throw new ArgumentException(
                "Scheme restrictions cannot apply to relative URIs — they have no scheme. " +
                "Use UriKind.Absolute, or UriKind.RelativeOrAbsolute where only absolute values are scheme-checked.",
                nameof(allowedSchemes));
        }

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.Uri;
            options.ErrorMessage = "'{PropertyName}' is not a valid URI.";
        });
    }

    // Url() is Uri() over absolute http and https URIs, reported under its own code and named as a URL.
    internal static UriValidator<TModel> ForUrl()
    {
        var validator = new UriValidator<TModel>(UriKind.Absolute, "http", "https");

        validator.SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.Url;
            options.ErrorMessage = "'{PropertyName}' is not a valid URL.";
        });

        return validator;
    }

    /// <inheritdoc />
    public override bool Validate(string? value, ValidationContext<TModel> context)
    {
        if (string.IsNullOrEmpty(value)) return true;

        // Uri.TryCreate trims whitespace, so without this the stored string could differ from the one that passed.
        if (HasEdgeWhitespaceOrControl(value)) return false;

        if (!Uri.TryCreate(value, _kind, out var uri)) return false;

        // "/etc/passwd" parses as an implicit file: URI on Unix only; "C:\temp" does everywhere. Neither
        // wrote a scheme.
        if (uri is { IsAbsoluteUri: true, IsFile: true } && !value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (_allowedSchemes == null) return true;

        // For relative URIs Scheme isn't meaningful — only check schemes when we have an absolute URI.
        if (!uri.IsAbsoluteUri) return true;

        for (var index = 0; index < _allowedSchemes.Length; index++)
            if (string.Equals(uri.Scheme, _allowedSchemes[index], StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static bool HasEdgeWhitespaceOrControl(string value)
    {
        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])) return true;

        foreach (var character in value)
            if (char.IsControl(character))
                return true;

        return false;
    }
}