using System.Collections;

namespace Sharp.Validator.Validators;

/// <summary>Value must be empty: <c>null</c>, a whitespace-only string, an empty collection/sequence, or the type's default value.</summary>
public sealed class EmptyValidator<TModel, TProperty> : PropertyValidator<TModel, TProperty>
{
    /// <summary>Creates the validator.</summary>
    public EmptyValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.Empty;
        options.ErrorMessage = "'{PropertyName}' must be empty.";
    });

    /// <inheritdoc />
    public override bool Validate(TProperty value, ValidationContext<TModel> context)
    {
        if (value == null) return true;

        switch (value)
        {
            case string stringValue: return string.IsNullOrWhiteSpace(stringValue);
            case ICollection collection: return collection.Count == 0;
            case IEnumerable enumerable:
            {
                var enumerator = enumerable.GetEnumerator();
                using var disposable = enumerator as IDisposable;
                return !enumerator.MoveNext();
            }
            default: return EqualityComparer<TProperty>.Default.Equals(value, default);
        }
    }
}

/// <summary>
/// Value must not be empty: not <c>null</c>, not a whitespace-only string, not an empty collection/sequence, and not the type's default value.
/// </summary>
public sealed class NotEmptyValidator<TModel, TProperty> : PropertyValidator<TModel, TProperty>
{
    /// <summary>Creates the validator.</summary>
    public NotEmptyValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.NotEmpty;
        options.ErrorMessage = "'{PropertyName}' must not be empty.";
    });

    /// <inheritdoc />
    public override bool Validate(TProperty value, ValidationContext<TModel> context)
    {
        if (value == null) return false;

        switch (value)
        {
            case string @string: return !string.IsNullOrWhiteSpace(@string);
            case ICollection collection: return collection.Count != 0;
            case IEnumerable enumerable:
            {
                var enumerator = enumerable.GetEnumerator();
                using var disposable = enumerator as IDisposable;
                return enumerator.MoveNext();
            }
            default: return !EqualityComparer<TProperty>.Default.Equals(value, default);
        }
    }
}