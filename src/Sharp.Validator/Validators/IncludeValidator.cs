using Sharp.Validator.Result;

namespace Sharp.Validator.Validators;

internal sealed class IncludeDerivedValidator<TModel, TProperty, TDerived>(
    Validator<TDerived> validator,
    IncludeValidatorOptions options
) : PropertyValidator<TModel, TProperty>, ISyncIncludeValidator<TModel, TProperty> where TDerived : TProperty
{
    private bool? _includeParentPropertyName;

    // The errors are the child's, with the codes and messages its rules gave them.
    private protected override bool IsLibraryRule => true;

    bool ISyncIncludeValidator<TModel, TProperty>.ValidateIncluded(
        TProperty value,
        ValidationContext<TModel> context,
        PropertyPathValue parentPath,
        in ValidatorBuilderOptionsSnapshot parentOptions,
        ref object? childContext
    ) => ValidateIncluded(value, context, parentPath, parentOptions, ref childContext);

    void IIncludeValidator.PrepareForComposition(object primaryBuilder) =>
        IncludeValidatorComposition.Prepare(primaryBuilder, validator, false);

    void IIncludeValidator.Freeze() =>
        _includeParentPropertyName = options.IncludeParentPropertyName;

    public override bool Validate(TProperty value, ValidationContext<TModel> context)
    {
        object? childContext = null;
        return ValidateIncluded(value, context, default, default, ref childContext);
    }

    private bool ValidateIncluded(
        TProperty value,
        ValidationContext<TModel> context,
        PropertyPathValue parentPath,
        in ValidatorBuilderOptionsSnapshot parentOptions,
        ref object? childContext
    )
    {
        if (value is not TDerived derived) return true;

        var errors = validator.ValidateIncluded(IncludeChildContext.Bind(context, derived, ref childContext));
        return errors == null || IncludeErrorMerger.Merge(
            errors, context, parentPath,
            _includeParentPropertyName ?? options.IncludeParentPropertyName,
            parentOptions);
    }
}

internal sealed class AsyncIncludeDerivedValidator<TModel, TProperty, TDerived>(
    Validator<TDerived> validator,
    IncludeValidatorOptions options
) : AsyncPropertyValidator<TModel, TProperty>, IAsyncIncludeValidator<TModel, TProperty> where TDerived : TProperty
{
    private bool? _includeParentPropertyName;

    // The errors are the child's, with the codes and messages its rules gave them.
    private protected override bool IsLibraryRule => true;

    Task<bool> IAsyncIncludeValidator<TModel, TProperty>.ValidateIncludedAsync(
        TProperty value,
        ValidationContext<TModel> context,
        PropertyPathValue parentPath,
        in ValidatorBuilderOptionsSnapshot parentOptions,
        IncludeChildSlot? slot,
        CancellationToken cancellationToken
    ) => ValidateIncludedAsync(value, context, parentPath, parentOptions, slot, cancellationToken);

    void IIncludeValidator.PrepareForComposition(object primaryBuilder) =>
        IncludeValidatorComposition.Prepare(primaryBuilder, validator, true);

    void IIncludeValidator.Freeze() =>
        _includeParentPropertyName = options.IncludeParentPropertyName;

    public override Task<bool> ValidateAsync(
        TProperty value,
        ValidationContext<TModel> context,
        CancellationToken cancellationToken
    ) => ValidateIncludedAsync(value, context, default, default, null, cancellationToken);

    // One method serves a single value, with no slot, and an element walk's slot, on purpose. A build that split off a
    // slot-free copy, and inlined the walks' slot test, won back 1–3% on walks of up to two elements, but a 200-element
    // element-property include ran 8–28% slower once other walks had trained this shared code's profile.
    private async Task<bool> ValidateIncludedAsync(
        TProperty value,
        ValidationContext<TModel> context,
        PropertyPathValue parentPath,
        ValidatorBuilderOptionsSnapshot parentOptions,
        IncludeChildSlot? slot,
        CancellationToken cancellationToken
    )
    {
        if (value is not TDerived derived) return true;

        var child = slot is null ? context.CreateChild(derived) : IncludeChildContext.Bind(context, derived, ref slot.Context);
        var result = await validator.ValidateAsync(child, cancellationToken).ConfigureAwait(false);
        return result.ErrorList is not { Count: > 0 } errors || IncludeErrorMerger.Merge(
            errors, context, parentPath,
            _includeParentPropertyName ?? options.IncludeParentPropertyName,
            parentOptions);
    }
}

internal interface IIncludeValidator
{
    void PrepareForComposition(object primaryBuilder);

    void Freeze();
}

// ReSharper disable once TypeParameterCanBeVariant — most rules aren't includes, and a variant interface sends that
// check through the cast cache.
internal interface ISyncIncludeValidator<TModel, TProperty> : IIncludeValidator
{
    bool ValidateIncluded(
        TProperty value,
        ValidationContext<TModel> context,
        PropertyPathValue parentPath,
        in ValidatorBuilderOptionsSnapshot parentOptions,
        ref object? childContext
    );
}

// ReSharper disable once TypeParameterCanBeVariant — invariant for the reason ISyncIncludeValidator gives.
internal interface IAsyncIncludeValidator<TModel, TProperty> : IIncludeValidator
{
    // slot is the enclosing element walk's reusable child context, or null for a single value.
    Task<bool> ValidateIncludedAsync(
        TProperty value,
        ValidationContext<TModel> context,
        PropertyPathValue parentPath,
        in ValidatorBuilderOptionsSnapshot parentOptions,
        IncludeChildSlot? slot,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// The child context an async element walk's includes reuse. A walk awaits one element's child before it binds the next, so the context is free again by
/// then, as with the sync evaluators' slot; an async method can't hold that slot by reference, so it lives in this object, made once per walk.
/// </summary>
internal sealed class IncludeChildSlot
{
    internal object? Context;
}

/// <summary>Options controlling how an included child validator's property names are reported.</summary>
public sealed class IncludeValidatorOptions
{
    private bool _readOnly;

    /// <summary>
    /// Whether child error names are prefixed with the validated parent's resolved, indexed path. Custom parent names belong on the options of the enclosing
    /// Property or Elements chain.
    /// </summary>
    public bool IncludeParentPropertyName
    {
        get;
        set => field = Writable(value);
    } = true;

    internal void MakeReadOnly() => _readOnly = true;

    private T Writable<T>(T value) =>
        _readOnly ? throw new InvalidOperationException(ReadOnlyOptions.ReadWhenTheCallbackReturned) : value;
}

file static class IncludeChildContext
{
    // The slot is the enclosing chain's: its evaluator runs one element at a time, so the context is free again by the
    // time the next element arrives. A second Include of another type in the chain just replaces it.
    internal static ValidationContext<TChild> Bind<TModel, TChild>(
        ValidationContext<TModel> context,
        TChild value,
        ref object? childContext
    )
    {
        if (childContext is ValidationContext<TChild> reusable)
        {
            reusable.Rebind(value);
            return reusable;
        }

        var child = context.CreateChild(value);
        childContext = child;
        return child;
    }
}

file static class IncludeErrorMerger
{
    internal static bool Merge<TModel>(
        List<ValidationError> errors,
        ValidationContext<TModel> context,
        PropertyPathValue parentPath,
        bool includeParentPropertyName,
        in ValidatorBuilderOptionsSnapshot parentOptions
    )
    {
        var parentCorePath = parentPath.FormatCore();
        for (var index = 0; index < errors.Count; index++)
        {
            var error = errors[index];
            context.AddError(new ValidationError(
                PropertyNameComposer.FormatIncluded(
                    parentCorePath, error.PropertyName, includeParentPropertyName, parentOptions),
                PropertyNameComposer.WrapMessage(error.Message ?? string.Empty, parentOptions),
                error.Code,
                error.Severity));
        }

        return false;
    }
}

internal static class IncludeValidatorComposition
{
    internal static void EnsureCanCompose<TProperty>(object primaryBuilder, Validator<TProperty> childValidator)
    {
        ArgumentNullException.ThrowIfNull(primaryBuilder);
        ArgumentNullException.ThrowIfNull(childValidator);
        if (ReferenceEquals(primaryBuilder, childValidator.Builder))
        {
            throw new InvalidOperationException(
                "A validator cannot include itself. To validate a recursive model, compose a new validator per level " +
                "down to a fixed depth.");
        }
    }

    internal static bool Prepare<TProperty>(
        object primaryBuilder,
        Validator<TProperty> childValidator,
        bool allowAsync
    )
    {
        EnsureCanCompose(primaryBuilder, childValidator);

        // Reject a known-incompatible adapter before freezing the child so a failed direct Add leaves both
        // definitions untouched. Configuration is construction-time only and is not supported concurrently
        // with Include composition.
        if (!allowAsync && childValidator.Builder.IsAsync)
            throw AsyncChildRequiresAsyncInclude();

        childValidator.Builder.Freeze();
        if (!allowAsync && childValidator.Builder.IsAsync)
            throw AsyncChildRequiresAsyncInclude();

        return childValidator.Builder.IsAsync;
    }

    private static InvalidOperationException AsyncChildRequiresAsyncInclude() => new(
        "An asynchronous child validator requires the async Include path.");
}