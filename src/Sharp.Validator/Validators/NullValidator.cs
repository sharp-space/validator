namespace Sharp.Validator.Validators;

/// <summary>Value must be <c>null</c>.</summary>
public sealed class NullValidator<TModel, TProperty> : PropertyValidator<TModel, TProperty>
{
    /// <summary>Creates the validator.</summary>
    public NullValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.Null;
        options.ErrorMessage = "'{PropertyName}' must be null.";
    });

    /// <inheritdoc />
    public override bool Validate(TProperty value, ValidationContext<TModel> context) => value == null;
}

/// <summary>Value must not be <c>null</c>.</summary>
public sealed class NotNullValidator<TModel, TProperty> : PropertyValidator<TModel, TProperty>
{
    /// <summary>Creates the validator.</summary>
    public NotNullValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.NotNull;
        options.ErrorMessage = "'{PropertyName}' must not be null.";
    });

    /// <inheritdoc />
    public override bool Validate(TProperty value, ValidationContext<TModel> context) => value != null;
}