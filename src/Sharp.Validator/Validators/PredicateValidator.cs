namespace Sharp.Validator.Validators;

/// <summary>Wraps a custom predicate registered via <c>Must</c>. The predicate receives every value, <c>null</c> included.</summary>
public sealed class PredicateValidator<TModel, TProperty> : PropertyValidator<TModel, TProperty>
{
    // Exactly one is set.
    private readonly Func<TProperty, ValidationContext<TModel>, bool>? _predicate;

    private readonly Func<TProperty, bool>? _valuePredicate;

    /// <summary>Creates the validator over the given predicate.</summary>
    public PredicateValidator(Func<TProperty, ValidationContext<TModel>, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _predicate = predicate;
        ConfigureDefaults();
    }

    private PredicateValidator(Func<TProperty, bool> valuePredicate)
    {
        _valuePredicate = valuePredicate;
        ConfigureDefaults();
    }

    // Must(value => ...) keeps its predicate as written: a lambda that dropped the context to call it would cost every
    // check a second delegate call. Must has checked the predicate.
    internal static PredicateValidator<TModel, TProperty> ForValue(Func<TProperty, bool> predicate) => new(predicate);

    private void ConfigureDefaults() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.Must;
        options.ErrorMessage = "'{PropertyName}' does not satisfy the required condition.";
    });

    /// <inheritdoc />
    public override bool Validate(TProperty value, ValidationContext<TModel> context) =>
        _valuePredicate is { } valuePredicate ? valuePredicate(value) : _predicate!(value, context);
}

/// <summary>Wraps an async custom predicate registered via <c>MustAsync</c>. The predicate receives every value, <c>null</c> included.</summary>
public sealed class AsyncPredicateValidator<TModel, TProperty> : AsyncPropertyValidator<TModel, TProperty>
{
    // Exactly one is set.
    private readonly Func<TProperty, ValidationContext<TModel>, CancellationToken, Task<bool>>? _predicate;

    private readonly Func<TProperty, CancellationToken, Task<bool>>? _tokenPredicate;

    private readonly Func<TProperty, Task<bool>>? _valuePredicate;

    /// <summary>Creates the validator over the given async predicate.</summary>
    public AsyncPredicateValidator(Func<TProperty, ValidationContext<TModel>, CancellationToken, Task<bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _predicate = predicate;
        ConfigureDefaults();
    }

    private AsyncPredicateValidator(
        Func<TProperty, CancellationToken, Task<bool>>? tokenPredicate,
        Func<TProperty, Task<bool>>? valuePredicate
    )
    {
        _tokenPredicate = tokenPredicate;
        _valuePredicate = valuePredicate;
        ConfigureDefaults();
    }

    // MustAsync keeps each shorter predicate as written: a lambda that dropped the context, or the token as well, to
    // call it would cost every check a second delegate call. MustAsync has checked the predicate.
    internal static AsyncPredicateValidator<TModel, TProperty> ForValue(Func<TProperty, Task<bool>> predicate) =>
        new(null, predicate);

    internal static AsyncPredicateValidator<TModel, TProperty> ForValueAndToken(
        Func<TProperty, CancellationToken, Task<bool>> predicate
    ) => new(predicate, null);

    private void ConfigureDefaults() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.Must;
        options.ErrorMessage = "'{PropertyName}' does not satisfy the required condition.";
    });

    /// <inheritdoc />
    public override Task<bool> ValidateAsync(
        TProperty value,
        ValidationContext<TModel> context,
        CancellationToken cancellationToken
    ) => _valuePredicate is { } valuePredicate ? valuePredicate(value)
        : _tokenPredicate is { } tokenPredicate ? tokenPredicate(value, cancellationToken)
        : _predicate!(value, context, cancellationToken);
}