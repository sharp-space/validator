namespace Sharp.Validator.Result;

/// <summary>
/// Aggregated validation outcome. Immutable: neither the collection a result was built from nor a cast of <see cref="Errors" /> can change it, so one
/// result can be shared, and every validation that passes returns the same empty one.
/// </summary>
public sealed class ValidationResult
{
    private static readonly ValidationErrorCollection NoErrors = new([]);
    // Null when there are no entries, so the all-valid path allocates no list and IsValid/HasErrors/... never allocate.
    // Errors hands out this read-only collection; the list inside it never leaves the result.
    private readonly ValidationErrorCollection? _errors;

    /// <summary>Creates a successful (empty) result.</summary>
    public ValidationResult()
    {
    }

    /// <summary>
    /// Creates a result over a copy of the given errors, so changing the collection afterwards doesn't change the result. A <c>null</c> or empty collection
    /// means no errors.
    /// </summary>
    public ValidationResult(IEnumerable<ValidationError>? errors)
    {
        if (errors is null) return;

        List<ValidationError> copy = [.. errors];
        if (copy.Count > 0) _errors = new ValidationErrorCollection(copy);
    }

    // A validator builds a new list for each run that records an entry and never touches it again, so the result owns
    // it without a copy.
    private ValidationResult(List<ValidationError> errors) => _errors = new ValidationErrorCollection(errors);

    /// <summary>A result with no entries. Validators return it for every model that passes without a warning.</summary>
    public static ValidationResult Empty { get; } = new();

    /// <summary>
    /// True when no <see cref="ValidationErrorSeverity.Error" />-level entries were produced. <see cref="ValidationErrorSeverity.Warning" /> and
    /// <see cref="ValidationErrorSeverity.Information" /> entries do not flip this to false — inspect <see cref="HasWarnings" /> /
    /// <see cref="HasInformation" /> (or filter <see cref="Errors" />) if you need to surface them.
    /// </summary>
    public bool IsValid => !HasErrors;

    /// <summary>True when at least one entry has <see cref="ValidationErrorSeverity.Error" />.</summary>
    public bool HasErrors => HasSeverity(ValidationErrorSeverity.Error);

    /// <summary>True when at least one entry has <see cref="ValidationErrorSeverity.Warning" />.</summary>
    public bool HasWarnings => HasSeverity(ValidationErrorSeverity.Warning);

    /// <summary>True when at least one entry has <see cref="ValidationErrorSeverity.Information" />.</summary>
    public bool HasInformation => HasSeverity(ValidationErrorSeverity.Information);

    /// <summary>
    /// The entries of every severity in the order they were reported, as a read-only collection; empty when valid. Every read returns the same collection,
    /// and a <c>foreach</c> over it allocates nothing.
    /// </summary>
    public ValidationErrorCollection Errors => _errors ?? NoErrors;

    /// <summary>The entries without the collection, for a validator merging an included child's result into its own.</summary>
    internal List<ValidationError>? ErrorList => _errors?.List;

    /// <summary>Takes over a list nothing else holds. For the validators, which build a new list for every run.</summary>
    internal static ValidationResult FromOwnedList(List<ValidationError> errors) => new(errors);

    /// <summary>
    /// The messages of every entry, warnings and information included, grouped by <see cref="ValidationError.PropertyName" /> in the order they were
    /// reported: the shape ASP.NET Core's <c>ValidationProblemDetails</c> takes. An entry without a message contributes an empty string.
    /// </summary>
    public Dictionary<string, string[]> ToDictionary()
    {
        var errors = _errors?.List;
        if (errors is null) return new Dictionary<string, string[]>(StringComparer.Ordinal);

        var messages = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var index = 0; index < errors.Count; index++)
        {
            var error = errors[index];
            if (!messages.TryGetValue(error.PropertyName, out var list)) messages.Add(error.PropertyName, list = []);

            list.Add(error.Message ?? string.Empty);
        }

        var dictionary = new Dictionary<string, string[]>(messages.Count, StringComparer.Ordinal);
        foreach (var (propertyName, list) in messages) dictionary.Add(propertyName, [.. list]);

        return dictionary;
    }

    private bool HasSeverity(ValidationErrorSeverity severity)
    {
        var errors = _errors?.List;
        if (errors is null) return false;

        for (var index = 0; index < errors.Count; index++)
            if (errors[index].Severity == severity)
                return true;

        return false;
    }
}