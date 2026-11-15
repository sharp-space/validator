using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Sharp.Validator.Result;
using Sharp.Validator.Validators;

namespace Sharp.Validator;

/// <summary>Untyped base for a per-property rule chain. Use the generic form to add validators.</summary>
public abstract class PropertyValidatorBuilder<TModel>
{
    private ConditionGate<TModel>[]? _conditions;

    private string _includedPropertyName = string.Empty;

    // The chain's own conditions in the order they run: the blocks around it prepend theirs, outermost ending up first,
    // and its Where calls append. A block shares one gate among all its chains.
    private List<ConditionGate<TModel>>? _ownConditions;

    // Only the library's chains derive from it: a chain is made by Property or Elements on a validator's builder.
    private protected PropertyValidatorBuilder()
    {
    }

    internal ValidatorBuilder<TModel> PrimaryBuilder { get; init; } = null!;

    internal LambdaExpression PropertyExpression { get; init; } = null!;

    internal PropertyValidatorBuilderOptions? PropertyOptions { get; init; }

    internal bool IsElementsBuilder { get; init; }

    internal PropertyValidationEvaluator<TModel>? PropertyEvaluator { get; set; }

    internal ElementsValidationEvaluator<TModel>? ElementsEvaluator { get; set; }

    internal ElementPropertyValidationEvaluator<TModel>? ElementPropertyEvaluator { get; set; }

    internal AsyncValidationEvaluator<TModel>? AsyncEvaluator { get; set; }

    /// <summary>The chain's single sync evaluator, whichever of the three shapes it is.</summary>
    internal SyncValidationEvaluator<TModel>? SyncEvaluator =>
        (SyncValidationEvaluator<TModel>?)PropertyEvaluator ?? (SyncValidationEvaluator<TModel>?)ElementsEvaluator ?? ElementPropertyEvaluator;

    private protected virtual PropertyValidatorBuilder<TModel>? ScopeParent => null;

    internal virtual PropertyValidatorBuilder<TModel>? ElementsSourceOwner => null;

    /// <summary>
    /// Frozen: the slot an Elements chain's source takes in the context when several evaluators walk it, so a run reads it once; -1 when one evaluator does,
    /// which reads it directly.
    /// </summary>
    internal int ElementSourceSlot { get; set; } = -1;

    /// <summary>
    /// Frozen: every condition gating this chain in the order they run, each once: its scope parent's, then its own. A block around an element chain and its
    /// child reaches the child both ways, and runs once.
    /// </summary>
    internal ConditionGate<TModel>[] Conditions => _conditions ?? [];

    /// <summary>Frozen: whether any condition gating this chain is async, which defers the whole chain to the async phase.</summary>
    internal bool HasEffectiveAsyncCondition { get; private set; }

    /// <summary>Frozen: whether this chain has any condition to evaluate.</summary>
    internal bool HasCondition { get; private set; }


    /// <summary>Root-first dotted path of the selected member, e.g. <c>x => x.Policy.Max</c> → <c>"Policy.Max"</c>.</summary>
    internal virtual string PropertyPath => field ??= PropertyExpression.GetPropertyPath();

    /// <summary>Frozen: whether the selector dereferences an intermediate member, so its accessor must be compiled null-safe.</summary>
    internal bool IsNestedPath { get; private set; }

    /// <summary>Frozen: whether the selector is a pure member chain, so <see cref="PropertyPath" /> is a faithful accessor-cache key.</summary>
    internal bool IsPathFaithful { get; private set; }

    internal PropertyValidatorBuilderOptionsSnapshot FrozenPropertyOptions { get; private set; }

    internal string NamePath { get; private set; } = string.Empty;

    internal ValidatorBuilderOptionsSnapshot BuilderOptions => PrimaryBuilder.FrozenOptions;

    internal string PropertyName { get; private set; } = string.Empty;

    /// <summary>Frozen: the name for this chain in messages, or <c>null</c> to use the reported name.</summary>
    internal string? DisplayName { get; private set; }

    /// <summary>The source path partial validation selects this chain by. Unlike the reported name, it ignores overrides and policies.</summary>
    internal virtual string FilterPath => PropertyPath;

    internal void AppendCondition(ConditionGate<TModel> gate) => (_ownConditions ??= []).Add(gate);

    internal void PrependCondition(ConditionGate<TModel> gate) => (_ownConditions ??= []).Insert(0, gate);

    private ConditionGate<TModel>[] FreezeConditions()
    {
        if (_conditions != null) return _conditions;

        var inherited = ScopeParent?.FreezeConditions() ?? [];
        if (_ownConditions is not { Count: > 0 } own) return _conditions = inherited;

        var conditions = new List<ConditionGate<TModel>>(inherited.Length + own.Count);
        conditions.AddRange(inherited);
        foreach (var gate in own)
            if (!conditions.Contains(gate))
                conditions.Add(gate);

        return _conditions = [.. conditions];
    }

    internal ValidatorBuilderOptionsSnapshot OptionsFor(ValidationContext<TModel> context) =>
        context.IsIncludedRun ? PrimaryBuilder.FrozenIncludedOptions : PrimaryBuilder.FrozenOptions;

    internal string PropertyNameFor(ValidationContext<TModel> context) =>
        context.IsIncludedRun ? _includedPropertyName : PropertyName;

    internal void Freeze(in ValidatorBuilderOptionsSnapshot builderOptions, in ValidatorMessageOptions messages)
    {
        // Settled before anything reads them: this chain's evaluators as they freeze below, and the accessor caches on
        // the first evaluation.
        IsNestedPath = PropertyExpression.IsNestedProperty();
        IsPathFaithful = PropertyExpression.IsPathFaithfulMemberChain();

        FrozenPropertyOptions = PropertyValidatorBuilderOptionsSnapshot.Create(PropertyOptions);
        NamePath = FrozenPropertyOptions.PropertyName ??
            PropertyNameComposer.ApplyPolicy(PropertyPath, builderOptions.PropertyNamePolicy);

        PropertyName = PropertyNameComposer.Wrap(NamePath, builderOptions);
        _includedPropertyName = PropertyNameComposer.Wrap(NamePath, PrimaryBuilder.FrozenIncludedOptions);

        DisplayName = FrozenPropertyOptions.DisplayName ?? messages.DisplayNameOf(PropertyExpression.GetLeafMember());

        FreezeValidators(PropertyEvaluator?.Validators, builderOptions, messages);
        FreezeValidators(ElementsEvaluator?.Validators, builderOptions, messages);
        FreezeValidators(ElementPropertyEvaluator?.Validators, builderOptions, messages);
        FreezeValidators(AsyncEvaluator?.Validators, builderOptions, messages);

        SyncEvaluator?.Freeze();
        AsyncEvaluator?.Freeze();

        var conditions = FreezeConditions();
        HasCondition = conditions.Length > 0;
        HasEffectiveAsyncCondition = Array.Exists(conditions, static gate => gate.IsAsync);

        return;

        static void FreezeValidators(
            List<PropertyValidator>? validators,
            in ValidatorBuilderOptionsSnapshot builderOptions,
            in ValidatorMessageOptions messages
        )
        {
            if (validators == null) return;

            foreach (var validator in validators)
            {
                validator.FreezeOptions(builderOptions, messages);
                if (validator is IIncludeValidator includeValidator)
                    includeValidator.Freeze();
            }
        }
    }

    internal ValidationCascadeMode? GetRuleCascadeMode() =>
        FrozenPropertyOptions.IsCascadeModeSet
            ? FrozenPropertyOptions.CascadeMode
            : BuilderOptions.CascadeMode;
}

/// <summary>A rule chain for a single property.</summary>
public class PropertyValidatorBuilder<TModel, TProperty> : PropertyValidatorBuilder<TModel>
{
    /// <summary>Creates a new property rule chain. Constructed via <see cref="ValidatorBuilder{TModel}.Property{TProperty}" />.</summary>
    internal PropertyValidatorBuilder(
        ValidatorBuilder<TModel> primaryBuilder,
        LambdaExpression propertyExpression,
        PropertyValidatorBuilderOptions? propertyOptions,
        bool isElementsBuilder
    )
    {
        PrimaryBuilder = primaryBuilder;
        PropertyExpression = propertyExpression;

        // Rejects an unsupported selector at registration instead of at first validation.
        propertyExpression.EnsureChainSelector();
        PropertyOptions = propertyOptions;
        IsElementsBuilder = isElementsBuilder;
    }

    /// <summary>Adds a parameterless validator of type <typeparamref name="TPropertyValidator" /> to the chain.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // ReSharper disable once VirtualMemberNeverOverridden.Global — public API: dropping virtual would change it.
    public virtual PropertyValidatorBuilder<TModel, TProperty> Add<TPropertyValidator>(Action<PropertyValidatorOptions>? configure = null)
        where TPropertyValidator : IPropertyValidator<TModel, TProperty>, new() => Add(new TPropertyValidator(), configure);

    /// <summary>
    /// Adds a synchronous validator instance to the chain, which may be a rule for a wider type, such as a <c>string?</c> rule on a <c>string</c> chain. A
    /// rule instance carries its options, so give each chain its own.
    /// </summary>
    public virtual PropertyValidatorBuilder<TModel, TProperty> Add(
        IPropertyValidator<TModel, TProperty> validator,
        Action<PropertyValidatorOptions>? configure = null
    )
    {
        var rule = TakeRule(validator, configure);
        if (!IsElementsBuilder)
        {
            PropertyEvaluator ??= new PropertyValidationEvaluator<TModel, TProperty>(this);
            PropertyEvaluator.Validators.Add(rule);
        }
        else
        {
            ElementsEvaluator ??= new ElementsValidationEvaluator<TModel, TProperty>(this);
            ElementsEvaluator.Validators.Add(rule);
        }

        return this;
    }

    /// <summary>
    /// Adds an asynchronous validator instance to the chain and marks the parent builder as async. As with the synchronous <c>Add</c>, the rule may be for a
    /// wider type, and a rule instance carries its options, so give each chain its own.
    /// </summary>
    public virtual PropertyValidatorBuilder<TModel, TProperty> Add(
        IAsyncPropertyValidator<TModel, TProperty> validator,
        Action<PropertyValidatorOptions>? configure = null
    )
    {
        var rule = TakeAsyncRule(validator, configure);

        PrimaryBuilder.IsAsync = true;
        AsyncEvaluator ??= new AsyncValidationEvaluator<TModel, TProperty>(this);
        AsyncEvaluator.Validators.Add(rule);

        return this;
    }

    /// <summary>
    /// Runs this chain only when <paramref name="condition" /> is true, including rules added before this call. Chained <c>.Where</c>s combine with AND.
    /// </summary>
    public virtual PropertyValidatorBuilder<TModel, TProperty> Where(
        Func<TModel, bool> condition
    )
    {
        ArgumentNullException.ThrowIfNull(condition);
        return AddCondition(new ConditionGate<TModel>(condition));
    }

    /// <inheritdoc cref="Where(Func{TModel, bool})" />
    public virtual PropertyValidatorBuilder<TModel, TProperty> Where(
        Func<TModel, ValidationContext<TModel>, bool> condition
    )
    {
        ArgumentNullException.ThrowIfNull(condition);
        return AddCondition(new ConditionGate<TModel>(condition));
    }

    /// <summary>Async variant of <see cref="Where(Func{TModel, bool})" />. Marks the parent builder as async.</summary>
    public virtual PropertyValidatorBuilder<TModel, TProperty> Where(
        Func<TModel, Task<bool>> condition
    )
    {
        ArgumentNullException.ThrowIfNull(condition);
        return AddCondition(new ConditionGate<TModel>(condition));
    }

    /// <inheritdoc cref="Where(Func{TModel, Task{bool}})" />
    public virtual PropertyValidatorBuilder<TModel, TProperty> Where(
        Func<TModel, CancellationToken, Task<bool>> condition
    )
    {
        ArgumentNullException.ThrowIfNull(condition);
        return AddCondition(new ConditionGate<TModel>(condition));
    }

    /// <inheritdoc cref="Where(Func{TModel, Task{bool}})" />
    public virtual PropertyValidatorBuilder<TModel, TProperty> Where(
        Func<TModel, ValidationContext<TModel>, CancellationToken, Task<bool>> condition
    )
    {
        ArgumentNullException.ThrowIfNull(condition);
        return AddCondition(new ConditionGate<TModel>(condition));
    }

    private PropertyValidatorBuilder<TModel, TProperty> AddCondition(ConditionGate<TModel> gate)
    {
        PrimaryBuilder.EnsureMutable();
        if (gate.IsAsync) PrimaryBuilder.IsAsync = true;

        AppendCondition(gate);

        return this;
    }


    // A rule whose type differs from the chain's in nullable annotations alone is the chain's type at run time, so it
    // is stored as it is; only a rule for a wider type, such as object?, runs behind an adapter. The rule itself takes
    // the chain's options either way, so its code still defaults to its own type's name.
    private protected PropertyValidator<TModel, TProperty> TakeRule(
        IPropertyValidator<TModel, TProperty> validator,
        Action<PropertyValidatorOptions>? configure
    )
    {
        ArgumentNullException.ThrowIfNull(validator);
        PrepareRule(validator.Rule, configure);
        return validator as PropertyValidator<TModel, TProperty> ?? new WiderRuleValidator<TModel, TProperty>(validator);
    }

    // The async form of TakeRule.
    private protected AsyncPropertyValidator<TModel, TProperty> TakeAsyncRule(
        IAsyncPropertyValidator<TModel, TProperty> validator,
        Action<PropertyValidatorOptions>? configure
    )
    {
        ArgumentNullException.ThrowIfNull(validator);
        PrepareRule(validator.Rule, configure);
        return validator as AsyncPropertyValidator<TModel, TProperty> ?? new AsyncWiderRuleValidator<TModel, TProperty>(validator);
    }

    private void PrepareRule(PropertyValidator validator, Action<PropertyValidatorOptions>? configure)
    {
        PrimaryBuilder.EnsureMutable();
        if (validator is IIncludeValidator includeValidator)
            includeValidator.PrepareForComposition(PrimaryBuilder);

        ConfigurePropertyValidator(validator, configure);
    }

    /// <summary>Applies rule-level options to <paramref name="validator" />.</summary>
    private protected void ConfigurePropertyValidator(PropertyValidator validator, Action<PropertyValidatorOptions>? configure)
    {
        if (validator.IsAttached)
        {
            throw new InvalidOperationException(
                $"This {validator.GetType().Name} instance already belongs to a rule chain. A rule carries its own options, " +
                "so give each chain its own instance.");
        }

        validator.IsAttached = true;
        MergeRuleOptions(validator, configure);

        // A custom rule may have handed its options to its own Configure callback; the chain has read them now.
        validator.LockHandedOutOptions();
    }

    private void MergeRuleOptions(PropertyValidator validator, Action<PropertyValidatorOptions>? configure)
    {
        string? errorMessage;
        ValidationErrorSeverity? errorSeverity;
        string? errorCode;
        if (configure != null)
        {
            var fresh = new PropertyValidatorOptions();
            configure(fresh);
            fresh.MakeReadOnly();
            errorMessage = fresh.ErrorMessage ?? PropertyOptions?.ErrorMessage;
            errorSeverity = fresh.ErrorSeverity ?? PropertyOptions?.ErrorSeverity;
            errorCode = fresh.ErrorCode ?? PropertyOptions?.ErrorCode;
        }
        else
        {
            if (PropertyOptions == null) return;

            errorMessage = PropertyOptions.ErrorMessage;
            errorSeverity = PropertyOptions.ErrorSeverity;
            errorCode = PropertyOptions.ErrorCode;
        }

        if (validator.Options == null)
        {
            validator.Options = new PropertyValidatorOptions
            {
                ErrorMessage = errorMessage,
                ErrorSeverity = errorSeverity,
                ErrorCode = errorCode,
                IsMessageOverridden = errorMessage != null
            };

            return;
        }

        if (errorMessage != null)
        {
            validator.Options.ErrorMessage = errorMessage;
            validator.Options.IsMessageOverridden = true;
        }

        if (errorSeverity != null) validator.Options.ErrorSeverity = errorSeverity;
        if (errorCode != null) validator.Options.ErrorCode = errorCode;
    }
}

/// <summary>
/// A rule chain that runs validators against each element of an enumerable property. Use <see cref="Property{TElementProperty}" /> for a property
/// of each element.
/// </summary>
public sealed class ElementValidatorBuilder<TModel, TElement> : PropertyValidatorBuilder<TModel, TElement>
{
    internal ElementValidatorBuilder(
        ValidatorBuilder<TModel> primaryBuilder,
        LambdaExpression propertyExpression,
        PropertyValidatorBuilderOptions? propertyOptions
    ) : base(primaryBuilder, propertyExpression, propertyOptions, true)
    {
    }

    internal override PropertyValidatorBuilder<TModel> ElementsSourceOwner => this;

    /// <summary>
    /// Starts a rule chain against a property of each element. The collection path and this builder's conditions are inherited; direct-element
    /// severity/code/cascade defaults are not.
    /// </summary>
    public ElementPropertyValidatorBuilder<TModel, TElement, TElementProperty> Property<TElementProperty>(
        Expression<Func<TElement, TElementProperty>> expression,
        Action<PropertyValidatorBuilderOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(expression);
        PrimaryBuilder.EnsureMutable();
        PropertyValidatorBuilderOptions? options = null;
        if (configure != null)
        {
            options = new PropertyValidatorBuilderOptions();
            configure(options);
            options.MakeReadOnly();
        }

        var propertyBuilder = new ElementPropertyValidatorBuilder<TModel, TElement, TElementProperty>(
            this, expression, options);

        PrimaryBuilder.PropertyBuilders.Add(propertyBuilder);
        return propertyBuilder;
    }

    /// <inheritdoc />
    public override ElementValidatorBuilder<TModel, TElement> Where(Func<TModel, bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        base.Where(condition);

        return this;
    }

    /// <inheritdoc />
    public override ElementValidatorBuilder<TModel, TElement> Where(
        Func<TModel, ValidationContext<TModel>, bool> condition
    )
    {
        ArgumentNullException.ThrowIfNull(condition);
        base.Where(condition);

        return this;
    }

    /// <inheritdoc />
    public override ElementValidatorBuilder<TModel, TElement> Where(Func<TModel, Task<bool>> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        base.Where(condition);

        return this;
    }

    /// <inheritdoc />
    public override ElementValidatorBuilder<TModel, TElement> Where(
        Func<TModel, CancellationToken, Task<bool>> condition
    )
    {
        ArgumentNullException.ThrowIfNull(condition);
        base.Where(condition);

        return this;
    }

    /// <inheritdoc />
    public override ElementValidatorBuilder<TModel, TElement> Where(
        Func<TModel, ValidationContext<TModel>, CancellationToken, Task<bool>> condition
    )
    {
        ArgumentNullException.ThrowIfNull(condition);
        base.Where(condition);

        return this;
    }
}

/// <summary>A rule chain that runs validators against a property of each element in an enumerable property.</summary>
public sealed class ElementPropertyValidatorBuilder<TModel, TElement, TProperty>
    : PropertyValidatorBuilder<TModel, TProperty>
{
    internal ElementPropertyValidatorBuilder(
        ElementValidatorBuilder<TModel, TElement> elementsBuilder,
        LambdaExpression propertyExpression,
        PropertyValidatorBuilderOptions? propertyOptions
    ) : base(elementsBuilder.PrimaryBuilder, propertyExpression, propertyOptions, false) => ElementsBuilder = elementsBuilder;

    internal ElementValidatorBuilder<TModel, TElement> ElementsBuilder { get; }

    private protected override PropertyValidatorBuilder<TModel> ScopeParent => ElementsBuilder;

    internal override PropertyValidatorBuilder<TModel> ElementsSourceOwner => ElementsBuilder;

    internal override string PropertyPath => field ??= PropertyExpression.GetPropertyPath(true);

    internal override string FilterPath => ElementsBuilder.PropertyPath;

    /// <inheritdoc />
    public override PropertyValidatorBuilder<TModel, TProperty> Add(
        IPropertyValidator<TModel, TProperty> validator,
        Action<PropertyValidatorOptions>? configure = null
    )
    {
        var rule = TakeRule(validator, configure);
        ElementPropertyEvaluator ??= new ElementPropertyValidationEvaluator<TModel, TElement, TProperty>(this);
        ElementPropertyEvaluator.Validators.Add(rule);

        return this;
    }

    /// <inheritdoc />
    public override PropertyValidatorBuilder<TModel, TProperty> Add(
        IAsyncPropertyValidator<TModel, TProperty> validator,
        Action<PropertyValidatorOptions>? configure = null
    )
    {
        var rule = TakeAsyncRule(validator, configure);
        PrimaryBuilder.IsAsync = true;
        AsyncEvaluator ??= new AsyncElementPropertyValidationEvaluator<TModel, TElement, TProperty>(this);
        AsyncEvaluator.Validators.Add(rule);

        return this;
    }
}