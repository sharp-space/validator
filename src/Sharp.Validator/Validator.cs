using System.Runtime.CompilerServices;
using Sharp.Validator.Result;

namespace Sharp.Validator;

/// <summary>Base class for validators that operate on instances of <typeparamref name="TModel" />.</summary>
public class Validator<TModel> : IValidator<TModel>, IValidatorConfigurationTarget
{
    /// <summary>For derived validators that build their rules in their own constructor.</summary>
    protected Validator()
    {
    }

    /// <summary>Creates a validator and applies the supplied builder configuration.</summary>
    public Validator(Action<ValidatorBuilder<TModel>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Builder = new());
    }

    /// <summary>The underlying rule builder. Use <see cref="Configure" /> for global options.</summary>
    protected internal ValidatorBuilder<TModel> Builder
    {
        get => field ??= new();
        private init;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationResult Validate(object model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Validate((TModel)model);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationResult Validate(TModel model) => Validate(new ValidationContext<TModel>(model));

    /// <inheritdoc />
    public ValidationResult Validate(TModel model, Action<ValidationContext<TModel>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var context = new ValidationContext<TModel>(model);
        configure(context);
        return Validate(context);
    }

    /// <inheritdoc />
    public ValidationResult Validate(ValidationContext<TModel> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PrepareSyncRun();

        return EvaluateSync(context);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ValidationResult> ValidateAsync(object model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ValidateAsync((TModel)model, cancellationToken);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ValidationResult> ValidateAsync(TModel model, CancellationToken cancellationToken = default) =>
        ValidateAsync(new ValidationContext<TModel>(model), cancellationToken);

    /// <inheritdoc />
    public ValueTask<ValidationResult> ValidateAsync(
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

    /// <summary>
    /// Runs sync rules then async rules against an existing context. A canceled <paramref name="cancellationToken" /> stops the validation before any rule
    /// runs and before each later chain with async rules, and the cancellation or an exception a rule throws comes back in the returned task.
    /// </summary>
    public ValueTask<ValidationResult> ValidateAsync(ValidationContext<TModel> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        Builder.Freeze();
        if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled<ValidationResult>(cancellationToken);

        // Synchronous fast-path: with no async rule registered there is nothing to await, so skip the async state
        // machine (and the async loop below) entirely and return the completed result inline — this is where the
        // ValueTask return type pays off. IsAsync is the same flag the sync Validate() trusts; an async Include child
        // sets it (and registers an AsyncEvaluator), so this never skips async work.
        if (!Builder.IsAsync) return EvaluateSyncAsTask(context);

        return ValidateAsyncCore(context, cancellationToken);
    }

    void IValidatorConfigurationTarget.ApplyDefaults(ValidatorBuilderOptions defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        Builder.EnsureMutable();
        Builder.ApplyScanDefaults(defaults);
    }

    /// <summary>
    /// Sets builder-wide options such as cascade mode, prefixes, and naming policy. Must be called before the first validation or before this validator is
    /// included by another validator, either of which freezes the validator definition.
    /// </summary>
    public void Configure(Action<ValidatorBuilderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Builder.EnsureMutable();
        configure(Builder.Options ??= new());
    }

    private void PrepareSyncRun()
    {
        Builder.Freeze();
        if (Builder.IsAsync)
        {
            throw new InvalidOperationException(
                "Asynchronous validator cannot call the Validate method. Please use the ValidateAsync method instead.");
        }
    }

    /// <summary>Runs as an Include child: same rules as <see cref="Validate(ValidationContext{TModel})" />, no result object.</summary>
    internal List<ValidationError>? ValidateIncluded(ValidationContext<TModel> context)
    {
        PrepareSyncRun();
        context.BeginRun();

        try
        {
            var conditions = new ConditionResults();
            EvaluateSyncRules(context, ref conditions);
            return context.TakeErrors();
        }
        catch
        {
            context.AbortValidation();
            throw;
        }
    }

    private ValidationResult EvaluateSync(ValidationContext<TModel> context)
    {
        // Outside the try: a refused run must leave the running one's state alone, which the catch would reset.
        context.BeginRun();

        try
        {
            var conditions = new ConditionResults();
            EvaluateSyncRules(context, ref conditions);

            // Build the result only after collection so the (lazily allocated) error list is captured
            // complete; on the all-valid path no list was allocated and the shared empty result comes
            // back. See ValidationContext.ToResult.
            return context.ToResult();
        }
        catch
        {
            context.AbortValidation();
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    // Kept out of line: the JIT otherwise inlines this loop, which takes the conditions by ref, into the try blocks of
    // EvaluateSync and ValidateIncluded. Their catch keeps the context in memory, so every step would reload it from
    // the stack; inlined, an Include over elements ran about 5% slower.
    private void EvaluateSyncRules(ValidationContext<TModel> context, ref ConditionResults conditions)
    {
        var builder = Builder;
        var plan = builder.SyncPlan;
        var stopOnChainFailure = builder.StopOnChainFailure;
        for (var index = 0; index < plan.Length; index++)
        {
            ref readonly var step = ref plan[index];

            // Selection first: a chain the caller excluded must not run its conditions either.
            if (!context.SelectChain(step.Builder)) continue;
            if (step.Conditions is { } gates && !ConditionGate<TModel>.AllMet(gates, context, ref conditions)) continue;

            step.Evaluator.Evaluate(context);

            if (stopOnChainFailure && context.HasBlockingError) return;
        }
    }

    // A rule's exception comes back in the task, as it does from the async path, so a caller that starts several
    // validations before awaiting them sees it where it awaits. Only a null argument, or a configuration error found
    // when the validator freezes, throws from the call itself.
    private ValueTask<ValidationResult> EvaluateSyncAsTask(ValidationContext<TModel> context)
    {
        try
        {
            return new ValueTask<ValidationResult>(EvaluateSync(context));
        }
        catch (Exception exception)
        {
            return ValueTask.FromException<ValidationResult>(exception);
        }
    }

    private async ValueTask<ValidationResult> ValidateAsyncCore(ValidationContext<TModel> context, CancellationToken cancellationToken)
    {
        context.BeginRun();

        try
        {
            // Collect sync errors first. The result is built only at the very end (context.ToResult) so it captures
            // errors the async loop adds afterwards — building it up front would, with the lazy list, capture a null
            // reference and silently drop async errors. Stop reacts to the first Error-severity entry
            // (HasBlockingError), the same predicate the sync path uses, so both entry points cascade identically.
            // The async phase reuses the verdicts of the conditions the sync phase ran.
            var conditions = new ConditionResults();
            EvaluateSyncRules(context, ref conditions);
            var cascadeMode = Builder.FrozenOptions.CascadeMode;

            // With Stop on the validator, no chain records a sync failure for HasRuleChainFailure below: this end, and
            // the one after a deferred chain's sync rules, keep a failed chain's async rules from starting.
            if (cascadeMode == ValidationCascadeMode.Stop && context.HasBlockingError)
                return context.ToResult();

            foreach (var propertyBuilder in Builder.AsyncPlan)
            {
                // A token canceled by an earlier chain, or while it ran, keeps the next chain's conditions and rules
                // from starting. The element walks also check between elements.
                cancellationToken.ThrowIfCancellationRequested();
                var evaluator = propertyBuilder.AsyncEvaluator;
                var hasAsyncCondition = propertyBuilder.HasEffectiveAsyncCondition;

                if (!context.SelectChain(propertyBuilder)) continue;

                switch (hasAsyncCondition)
                {
                    // With an async condition the conditions are evaluated below, in order, awaiting the async ones.
                    case false when propertyBuilder.HasCondition &&
                        !ConditionGate<TModel>.AllMet(propertyBuilder.Conditions, context, ref conditions):
                        continue;
                    case true:
                    {
                        var index = 0;
                        var met = ConditionGate<TModel>.MeetUntilAwait(propertyBuilder.Conditions, ref index, null, context, ref conditions);
                        while (met == null)
                        {
                            var verdict = await propertyBuilder.Conditions[index].RunAsync(context, cancellationToken)
                                .ConfigureAwait(false);

                            met = ConditionGate<TModel>.MeetUntilAwait(propertyBuilder.Conditions, ref index, verdict, context, ref conditions);
                        }

                        if (met == false) continue;

                        // This chain skipped the sync phase (its condition had to be awaited first), so run its
                        // deferred sync evaluators now, before its async validators.
                        var propertyEvaluator = propertyBuilder.PropertyEvaluator;
                        if (propertyEvaluator != null) propertyEvaluator.Evaluate(context);
                        else if (propertyBuilder.ElementsEvaluator != null)
                            propertyBuilder.ElementsEvaluator.Evaluate(context);
                        else propertyBuilder.ElementPropertyEvaluator?.Evaluate(context);

                        // With Stop on the validator, the chain recorded no failure to skip its async rules by:
                        // this end does.
                        if (cascadeMode == ValidationCascadeMode.Stop && context.HasBlockingError) return context.ToResult();

                        break;
                    }
                }

                if (evaluator == null) continue;

                if (context.HasRuleChainFailure(propertyBuilder) &&
                    propertyBuilder.GetRuleCascadeMode() == ValidationCascadeMode.Stop) continue;

                var pending = evaluator.EvaluateAsync(context, cancellationToken);
                if (!pending.IsCompletedSuccessfully) await pending.ConfigureAwait(false);

                // Mirror the sync loop's builder-level gate: with Stop, a failed chain halts every later chain.
                if (cascadeMode == ValidationCascadeMode.Stop && context.HasBlockingError) return context.ToResult();
            }

            return context.ToResult();
        }
        catch
        {
            context.AbortValidation();
            throw;
        }
    }
}