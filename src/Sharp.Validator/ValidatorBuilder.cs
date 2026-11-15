using System.Linq.Expressions;

namespace Sharp.Validator;

/// <summary>Builds the rule set for a <see cref="Validator{TModel}" />.</summary>
public sealed class ValidatorBuilder<TModel>
{
    private readonly Lock _freezeGate = new();

    private volatile bool _isFrozen;

    private ValidatorBuilderOptions? _scanDefaults;

    // A validator makes its own builder; one made apart from a validator has nothing to freeze it or run its rules.
    internal ValidatorBuilder()
    {
    }

    internal List<PropertyValidatorBuilder<TModel>> PropertyBuilders => field ??= [];

    internal bool IsAsync { get; set; }

    internal ValidatorBuilderOptionsSnapshot FrozenOptions { get; private set; }

    internal ValidatorBuilderOptionsSnapshot FrozenIncludedOptions { get; private set; }

    internal SyncRuleStep<TModel>[] SyncPlan { get; private set; } = [];

    internal PropertyValidatorBuilder<TModel>[] AsyncPlan { get; private set; } = [];

    internal bool StopOnChainFailure { get; private set; }

    /// <summary>Frozen: how many element sources a run of this validator keeps in its context, one per slot.</summary>
    internal int ElementSourceCount { get; private set; }

    internal bool IsFrozen => _isFrozen;

    /// <summary>
    /// Options set on this builder through <c>Configure</c>. Fields left unset fall back to the <c>AddValidators</c> scan defaults, then to
    /// <see cref="ValidatorBuilderOptions.Global" />, when the builder freezes.
    /// </summary>
    public ValidatorBuilderOptions? Options { get; internal set; }

    internal void EnsureMutable()
    {
        if (_isFrozen)
        {
            throw new InvalidOperationException(
                "A validator cannot be configured after validation or Include composition has frozen it.");
        }
    }

    internal void Freeze()
    {
        if (_isFrozen) return;

        lock (_freezeGate)
        {
            if (_isFrozen) return;

            // The validator reads these now and never again, so a change made to them later would go unseen: it throws.
            // Global is created here if no one has read it yet, so a setting made on it after this freeze throws too,
            // rather than reach only the validators that freeze later.
            var global = ValidatorBuilderOptions.Global;
            Options?.MakeReadOnly();
            _scanDefaults?.MakeReadOnly();
            global.MakeReadOnly();

            FrozenOptions = ValidatorBuilderOptionsSnapshot.Create(Options, _scanDefaults, global);
            FrozenIncludedOptions = FrozenOptions.WithLocalWrappersOnly(Options);
            var messages = new ValidatorMessageOptions(Options, _scanDefaults, global);
            foreach (var propertyBuilder in PropertyBuilders)
                propertyBuilder.Freeze(FrozenOptions, messages);

            var elementSourceUses = new Dictionary<PropertyValidatorBuilder<TModel>, int>();
            foreach (var propertyBuilder in PropertyBuilders)
            {
                var owner = propertyBuilder.ElementsSourceOwner;
                if (owner == null) continue;

                var evaluatorCount = 0;
                if (propertyBuilder.ElementsEvaluator != null || propertyBuilder.ElementPropertyEvaluator != null)
                    evaluatorCount++;

                if (propertyBuilder.AsyncEvaluator != null)
                    evaluatorCount++;

                if (evaluatorCount == 0) continue;

                elementSourceUses[owner] = elementSourceUses.GetValueOrDefault(owner) + evaluatorCount;
            }

            // A source that more than one evaluator walks is read once per validation and kept in the context, at a
            // slot unique within this validator.
            var elementSourceCount = 0;
            foreach (var (owner, useCount) in elementSourceUses)
                if (useCount > 1)
                    owner.ElementSourceSlot = elementSourceCount++;

            ElementSourceCount = elementSourceCount;

            // Chains gated by an async Where are deferred to the async phase, and a chain with only async rules has no
            // sync work, so neither belongs to the plan the sync loop walks.
            var plan = new SyncRuleStep<TModel>[PropertyBuilders.Count];
            var stepCount = 0;
            foreach (var propertyBuilder in PropertyBuilders)
                if (propertyBuilder is { SyncEvaluator: { } evaluator, HasEffectiveAsyncCondition: false })
                {
                    plan[stepCount++] = new SyncRuleStep<TModel>(
                        propertyBuilder, evaluator, propertyBuilder.HasCondition ? propertyBuilder.Conditions : null);
                }

            if (stepCount != plan.Length) Array.Resize(ref plan, stepCount);

            SyncPlan = plan;

            if (IsAsync)
                AsyncPlan = PropertyBuilders.FindAll(static x => x.AsyncEvaluator != null || x.HasEffectiveAsyncCondition).ToArray();

            // A run reaches a condition once per phase of each chain it gates. Only one it can reach more than once,
            // such as a block's, an element chain's, or one on a chain with sync and async rules, gets a slot to keep
            // its verdict in; the rest run straight through.
            foreach (var step in SyncPlan)
            foreach (var gate in step.Builder.Conditions)
                gate.Reach++;

            foreach (var propertyBuilder in AsyncPlan)
            foreach (var gate in propertyBuilder.Conditions)
                gate.Reach++;

            var slotCount = 0;
            foreach (var propertyBuilder in PropertyBuilders)
            foreach (var gate in propertyBuilder.Conditions)
                if (gate is { Reach: > 1, Slot: < 0 })
                    gate.Slot = slotCount++;

            StopOnChainFailure = FrozenOptions.CascadeMode == ValidationCascadeMode.Stop;

            _isFrozen = true;
        }
    }

    internal void ApplyScanDefaults(ValidatorBuilderOptions defaults) => _scanDefaults = defaults;

    /// <summary>Starts a rule chain for a property of <typeparamref name="TProperty" />.</summary>
    public PropertyValidatorBuilder<TModel, TProperty> Property<TProperty>(
        Expression<Func<TModel, TProperty>> expression,
        Action<PropertyValidatorBuilderOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(expression);
        EnsureMutable();

        return CreatePropertyBuilder<TProperty>(expression, configure);
    }

    /// <summary>
    /// Starts a rule chain that runs against each element of an enumerable property. Use
    /// <see cref="ElementValidatorBuilder{TModel,TElement}.Property{TElementProperty}" /> for child properties.
    /// </summary>
    public ElementValidatorBuilder<TModel, TProperty> Elements<TProperty>(
        Expression<Func<TModel, IEnumerable<TProperty>?>> expression,
        Action<PropertyValidatorBuilderOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(expression);
        EnsureMutable();

        return CreateElementsBuilder<TProperty>(expression, configure);
    }

    /// <summary>Applies <paramref name="condition" /> to every rule chain that <paramref name="configure" /> starts.</summary>
    public void Where(Func<TModel, bool> condition, Action configure)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(configure);
        AddCondition(new ConditionGate<TModel>(condition), configure);
    }

    /// <inheritdoc cref="Where(Func{TModel, bool}, Action)" />
    public void Where(Func<TModel, ValidationContext<TModel>, bool> condition, Action configure)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(configure);
        AddCondition(new ConditionGate<TModel>(condition), configure);
    }

    /// <summary>Async variant of <see cref="Where(Func{TModel, bool}, Action)" />.</summary>
    public void Where(Func<TModel, Task<bool>> condition, Action configure)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(configure);
        AddCondition(new ConditionGate<TModel>(condition), configure);
    }

    /// <inheritdoc cref="Where(Func{TModel, Task{bool}}, Action)" />
    public void Where(Func<TModel, CancellationToken, Task<bool>> condition, Action configure)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(configure);
        AddCondition(new ConditionGate<TModel>(condition), configure);
    }

    /// <inheritdoc cref="Where(Func{TModel, Task{bool}}, Action)" />
    public void Where(Func<TModel, ValidationContext<TModel>, CancellationToken, Task<bool>> condition, Action configure)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(configure);
        AddCondition(new ConditionGate<TModel>(condition), configure);
    }

    private void AddCondition(ConditionGate<TModel> gate, Action configure)
    {
        EnsureMutable();

        // An async condition can only be awaited on the async path, even when it gates sync-only rules — without this
        // flag, Validate() would run those rules unconditionally and never evaluate the condition.
        if (gate.IsAsync) IsAsync = true;

        var propertyBuilderCount = PropertyBuilders.Count;
        configure();

        // One gate for the whole block, prepended to each of its chains: the block encloses every condition they
        // already hold, so it runs before them, and sharing the gate lets a validation run it once for all of them.
        for (var index = propertyBuilderCount; index < PropertyBuilders.Count; index++)
            PropertyBuilders[index].PrependCondition(gate);
    }

    private PropertyValidatorBuilder<TModel, TProperty> CreatePropertyBuilder<TProperty>(
        LambdaExpression expression,
        Action<PropertyValidatorBuilderOptions>? configure,
        bool isElementsBuilder = false
    )
    {
        PropertyValidatorBuilderOptions? options = null;
        if (configure != null)
        {
            options = new PropertyValidatorBuilderOptions();
            configure(options);
            options.MakeReadOnly();
        }

        var propertyBuilder = new PropertyValidatorBuilder<TModel, TProperty>(
            this, expression, options, isElementsBuilder);

        PropertyBuilders.Add(propertyBuilder);
        return propertyBuilder;
    }

    private ElementValidatorBuilder<TModel, TProperty> CreateElementsBuilder<TProperty>(
        LambdaExpression expression,
        Action<PropertyValidatorBuilderOptions>? configure
    )
    {
        PropertyValidatorBuilderOptions? options = null;
        if (configure != null)
        {
            options = new PropertyValidatorBuilderOptions();
            configure(options);
            options.MakeReadOnly();
        }

        var propertyBuilder = new ElementValidatorBuilder<TModel, TProperty>(
            this, expression, options);

        PropertyBuilders.Add(propertyBuilder);
        return propertyBuilder;
    }
}

/// <summary>
/// One entry of the frozen sync plan: the chain, the evaluator resolved from its three-way slot, and the conditions gating it, <c>null</c> when
/// it has none.
/// </summary>
internal readonly record struct SyncRuleStep<TModel>(
    PropertyValidatorBuilder<TModel> Builder,
    SyncValidationEvaluator<TModel> Evaluator,
    ConditionGate<TModel>[]? Conditions
);