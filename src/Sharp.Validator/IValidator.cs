using Sharp.Validator.Result;

namespace Sharp.Validator;

/// <summary>
/// Weakly-typed validation entry point for consumers that don't know the model type at compile time (middleware, pipelines). Strongly-typed callers
/// should prefer <see cref="IValidator{TModel}" />.
/// </summary>
public interface IValidator
{
    /// <summary>Validates the given object after casting it to the validator's model type.</summary>
    ValidationResult Validate(object model);

    /// <summary>Asynchronously validates the given object after casting it to the validator's model type.</summary>
    ValueTask<ValidationResult> ValidateAsync(object model, CancellationToken cancellationToken = default);
}

/// <summary>
/// Validates instances of <typeparamref name="TModel" />. An implementation writes the two members that take a <see cref="ValidationContext{TModel}" />;
/// the others build a context and call them, and <see cref="Validator{TModel}" /> declares its own for speed.
/// </summary>
public interface IValidator<TModel> : IValidator
{
    // The weakly typed entry points check for null before the cast, which would let a null through to a model that
    // can't be null, and cast to the model type.
    ValidationResult IValidator.Validate(object model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Validate((TModel)model);
    }

    ValueTask<ValidationResult> IValidator.ValidateAsync(object model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ValidateAsync((TModel)model, cancellationToken);
    }

    /// <summary>Validates the model owned by an existing context. Throws if any async rule is registered.</summary>
    ValidationResult Validate(ValidationContext<TModel> context);

    /// <summary>Asynchronously validates the model owned by an existing context.</summary>
    ValueTask<ValidationResult> ValidateAsync(
        ValidationContext<TModel> context,
        CancellationToken cancellationToken = default
    );

    /// <summary>Validates the model.</summary>
    ValidationResult Validate(TModel model) => Validate(new ValidationContext<TModel>(model));

    /// <summary>Validates the model after <paramref name="configure" /> has set up its context.</summary>
    ValidationResult Validate(TModel model, Action<ValidationContext<TModel>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var context = new ValidationContext<TModel>(model);
        configure(context);
        return Validate(context);
    }

    /// <summary>Asynchronously validates the model.</summary>
    ValueTask<ValidationResult> ValidateAsync(TModel model, CancellationToken cancellationToken = default) =>
        ValidateAsync(new ValidationContext<TModel>(model), cancellationToken);

    /// <summary>Asynchronously validates the model after <paramref name="configure" /> has set up its context.</summary>
    ValueTask<ValidationResult> ValidateAsync(
        TModel model,
        Action<ValidationContext<TModel>> configure,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(configure);

        var context = new ValidationContext<TModel>(model);
        configure(context);
        return ValidateAsync(context, cancellationToken);
    }
}