using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sharp.Validator.Result;
using Sharp.Validator.Validators;

namespace Sharp.Validator;

internal abstract class ValidationEvaluator<TModel>
{
    internal PropertyValidatorBuilder<TModel> Builder { get; init; } = null!;
}

/// <summary>Common shape of the three synchronous evaluators, so the rule plan can dispatch through one virtual slot.</summary>
internal abstract class SyncValidationEvaluator<TModel> : ValidationEvaluator<TModel>
{
    private protected bool HasIncludes;

    // Frozen snapshot. Reading these instead of walking Builder -> PrimaryBuilder -> FrozenOptions avoids copying the
    // 48/56-byte option structs on every chain and, for element chains, on every element.
    private protected bool StopOnFailure;

    // Only an async chain with Stop asks HasRuleChainFailure, to skip its async rules once a sync rule has failed, and
    // only while the run goes on: Stop on the validator ends the run at that failure, before the async phase can ask.
    // So no other chain may pay for the record: the context's extras and a HashSet.
    private protected bool TrackChainFailure;

    internal List<PropertyValidator> Validators { get; } = [];

    internal virtual void Freeze()
    {
        StopOnFailure = Builder.GetRuleCascadeMode() == ValidationCascadeMode.Stop;
        TrackChainFailure = StopOnFailure && Builder.AsyncEvaluator != null &&
            Builder.BuilderOptions.CascadeMode != ValidationCascadeMode.Stop;

        foreach (var validator in Validators)
            if (validator is IIncludeValidator)
                HasIncludes = true;
    }

    internal abstract void Evaluate(ValidationContext<TModel> context);

    // A rule that only recorded Warning or Information entries is not a failure of the chain: those severities are
    // non-blocking, so Stop must not let them hide a later Error rule.
    private protected bool EndFailedRule(ValidationContext<TModel> context, int blockingBefore)
    {
        context.ResetRuleMessageState();

        if (context.BlockingErrorCount == blockingBefore) return false;
        if (TrackChainFailure) context.MarkRuleChainFailed(Builder);

        return StopOnFailure;
    }
}

internal abstract class PropertyValidationEvaluator<TModel> : SyncValidationEvaluator<TModel>;

internal class PropertyValidationEvaluator<TModel, TProperty> : PropertyValidationEvaluator<TModel>
{
    private readonly ChainValueReader<TModel, TProperty> _valueReader;

    private bool _isNestedPath;

    private PropertyValidator<TModel, TProperty>[] _rules = [];

    // Lazily, two slots per rule (root run, Include run — they compose different wrappers): the error a failure records
    // when its message is fully determined at freeze time. ValidationError is immutable, so results can share it, and
    // the failure then allocates nothing of its own.
    private ValidationError?[]? _staticErrors;

    public PropertyValidationEvaluator(PropertyValidatorBuilder<TModel> builder)
    {
        Builder = builder;
        _valueReader = new(builder);
    }

    internal override void Freeze()
    {
        base.Freeze();
        _rules = RulePlan.Snapshot<TModel, TProperty>(Validators);
        _isNestedPath = Builder.IsNestedPath;
    }

    internal override void Evaluate(ValidationContext<TModel> context)
    {
        TProperty property;
        if (_isNestedPath)
        {
            // Nested selector (x => x.A.B): a null intermediate yields HasValue == false, so we skip the whole rule
            // chain rather than throwing — guard presence with NotNull on x.A.
            var accessed = _valueReader.ReadNested(context.Model);
            if (!accessed.HasValue) return;

            property = accessed.Value;
        }
        else property = _valueReader.Read(context.Model);

        var rules = _rules;
        if (HasIncludes)
        {
            EvaluateWithIncludes(rules, property, context);
            return;
        }

        for (var index = 0; index < rules.Length; index++)
        {
            if (rules[index].Validate(property, context))
            {
                context.ResetRuleMessageState();
                continue;
            }

            if (OnRuleFailed(index, property, context)) return;
        }
    }

    private void EvaluateWithIncludes(
        PropertyValidator<TModel, TProperty>[] rules,
        TProperty property,
        ValidationContext<TModel> context
    )
    {
        var propertyName = Builder.PropertyNameFor(context);
        var builderOptions = Builder.OptionsFor(context);
        object? childContext = null;
        for (var index = 0; index < rules.Length; index++)
        {
            var blockingBefore = context.BlockingErrorCount;
            if (RuleEvaluator.Evaluate(
                    rules[index], property, context, Builder.DisplayName, Builder.NamePath, propertyName, builderOptions, ref childContext)) continue;

            if (context.BlockingErrorCount == blockingBefore) continue;

            if (TrackChainFailure) context.MarkRuleChainFailed(Builder);

            if (StopOnFailure) return;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    // Returns whether the chain stops here.
    private bool OnRuleFailed(int index, TProperty property, ValidationContext<TModel> context)
    {
        var rule = _rules[index];
        var propertyName = Builder.PropertyNameFor(context);
        var blockingBefore = context.BlockingErrorCount;

        var cached = context.ErrorMessage == null
            ? (_staticErrors ??= new ValidationError?[_rules.Length * 2])[(index * 2) + (context.IsIncludedRun ? 1 : 0)] ??=
            ErrorMessageExtensions.ResolveStaticError(propertyName, Builder.DisplayName, Builder.OptionsFor(context), rule.Options!)
            : null;

        if (cached is not null && !ReferenceEquals(cached, ErrorMessageExtensions.NoStaticError)) context.AddError(cached);
        else context.AddValidationError(propertyName, Builder.DisplayName, property, Builder.OptionsFor(context), rule);

        return EndFailedRule(context, blockingBefore);
    }
}

internal abstract class ElementsValidationEvaluator<TModel> : SyncValidationEvaluator<TModel>;

internal class ElementsValidationEvaluator<TModel, TProperty> : ElementsValidationEvaluator<TModel>
{
    private readonly ElementsReader<TModel, TProperty> _elementsReader;

    private PropertyValidator<TModel, TProperty>[] _rules = [];

    public ElementsValidationEvaluator(PropertyValidatorBuilder<TModel> builder)
    {
        Builder = builder;
        _elementsReader = new(builder);
    }

    internal override void Freeze()
    {
        base.Freeze();
        _rules = RulePlan.Snapshot<TModel, TProperty>(Validators);
    }

    internal override void Evaluate(ValidationContext<TModel> context)
    {
        var elements = _elementsReader.Read(context);
        if (elements == null) return;

        // An Include in this chain reuses one child context across the run instead of allocating one per element.
        object? childContext = null;

        // Element-outer / validator-inner: iterate the source exactly once and group errors by element index.
        // List<T> and arrays run over a span, so an element costs neither an iterator nor two interface calls.
        if (elements is List<TProperty> list)
        {
            // The span is the backing array as the walk began, so a rule that resized the list left the walk reading a
            // cleared slot or missing an added element. List<T>'s own enumerator refuses that, and so does this.
            var span = CollectionsMarshal.AsSpan(list);
            EvaluateSpan(span, context, ref childContext);
            if (list.Count != span.Length) ElementSourceGuard.ThrowListChanged();

            return;
        }

        if (elements is TProperty[] array)
        {
            EvaluateSpan(array, context, ref childContext);
            return;
        }

        if (elements is IList<TProperty> indexed)
        {
            for (var index = 0; index < indexed.Count; index++)
                if (!EvaluateElement(indexed[index], index, context, ref childContext))
                    return;

            return;
        }

        var elementIndex = 0;
        foreach (var element in elements)
        {
            if (!EvaluateElement(element, elementIndex, context, ref childContext)) return;

            elementIndex++;
        }
    }

    private void EvaluateSpan(ReadOnlySpan<TProperty> elements, ValidationContext<TModel> context, ref object? childContext)
    {
        for (var index = 0; index < elements.Length; index++)
            if (!EvaluateElement(elements[index], index, context, ref childContext))
                return;
    }

    // Returns whether the iteration continues.
    private bool EvaluateElement(TProperty element, int index, ValidationContext<TModel> context, ref object? childContext)
    {
        var rules = _rules;
        if (HasIncludes) return EvaluateElementWithIncludes(rules, element, index, context, ref childContext);

        for (var ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
        {
            if (rules[ruleIndex].Validate(element, context))
            {
                context.ResetRuleMessageState();
                continue;
            }

            if (OnRuleFailed(rules[ruleIndex], element, index, context)) return false;
        }

        return true;
    }

    private bool EvaluateElementWithIncludes(
        PropertyValidator<TModel, TProperty>[] rules,
        TProperty element,
        int index,
        ValidationContext<TModel> context,
        ref object? childContext
    )
    {
        var builderOptions = Builder.OptionsFor(context);
        for (var ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
        {
            var blockingBefore = context.BlockingErrorCount;
            if (RuleEvaluator.EvaluateElement(
                    rules[ruleIndex], element, context, Builder.DisplayName, Builder.NamePath, index, null, builderOptions,
                    ref childContext)) continue;

            if (context.BlockingErrorCount == blockingBefore) continue;

            if (TrackChainFailure) context.MarkRuleChainFailed(Builder);

            if (StopOnFailure) return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool OnRuleFailed(
        PropertyValidator<TModel, TProperty> rule,
        TProperty element,
        int index,
        ValidationContext<TModel> context
    )
    {
        var builderOptions = Builder.OptionsFor(context);
        var blockingBefore = context.BlockingErrorCount;
        var propertyName = PropertyNameComposer.FormatElement(Builder.NamePath, index, null, builderOptions);
        context.AddValidationError(propertyName, Builder.DisplayName, element, builderOptions, rule);
        return EndFailedRule(context, blockingBefore);
    }
}

internal abstract class ElementPropertyValidationEvaluator<TModel> : SyncValidationEvaluator<TModel>;

internal class ElementPropertyValidationEvaluator<TModel, TElement, TProperty>
    : ElementPropertyValidationEvaluator<TModel>
{
    private readonly ElementsReader<TModel, TElement> _elementsReader;

    private readonly ElementMemberReader<TModel, TElement, TProperty> _memberReader;

    private PropertyValidator<TModel, TProperty>[] _rules = [];

    public ElementPropertyValidationEvaluator(ElementPropertyValidatorBuilder<TModel, TElement, TProperty> builder)
    {
        Builder = builder;
        ElementPropertyBuilder = builder;
        _memberReader = new(builder);
        _elementsReader = new(builder);
    }

    private ElementPropertyValidatorBuilder<TModel, TElement, TProperty> ElementPropertyBuilder { get; }

    internal override void Freeze()
    {
        base.Freeze();
        _rules = RulePlan.Snapshot<TModel, TProperty>(Validators);
        _memberReader.Freeze();
    }

    internal override void Evaluate(ValidationContext<TModel> context)
    {
        var elements = _elementsReader.Read(context);
        if (elements == null) return;

        object? childContext = null;
        if (elements is List<TElement> list)
        {
            var span = CollectionsMarshal.AsSpan(list);
            EvaluateSpan(span, context, ref childContext);
            if (list.Count != span.Length) ElementSourceGuard.ThrowListChanged();

            return;
        }

        if (elements is TElement[] array)
        {
            EvaluateSpan(array, context, ref childContext);
            return;
        }

        if (elements is IList<TElement> indexed)
        {
            for (var index = 0; index < indexed.Count; index++)
            {
                if (!_memberReader.TryRead(indexed[index], out var property)) continue;
                if (!EvaluateProperty(context, property, index, ref childContext)) return;
            }

            return;
        }

        var elementIndex = 0;
        foreach (var element in elements)
        {
            if (_memberReader.TryRead(element, out var property) &&
                !EvaluateProperty(context, property, elementIndex, ref childContext)) return;

            elementIndex++;
        }
    }

    private void EvaluateSpan(ReadOnlySpan<TElement> elements, ValidationContext<TModel> context, ref object? childContext)
    {
        // A nested selector's read has no catch, so the JIT inlines it.
        if (_memberReader.IsNestedPath)
        {
            for (var index = 0; index < elements.Length; index++)
            {
                if (!_memberReader.TryReadNested(elements[index], out var property)) continue;
                if (!EvaluateProperty(context, property, index, ref childContext)) return;
            }

            return;
        }

        // Any other read is written out here, with its accessor fetched once per walk, in place of a TryRead call per
        // element: TryRead's catch keeps the JIT from inlining it, even when asked to, and the call took about 40% of
        // a 10,000-element walk.
        var read = _memberReader.Accessor;
        for (var index = 0; index < elements.Length; index++)
        {
            var element = elements[index];
            if (element is null) continue;

            TProperty property;

            try
            {
                property = read(element);
            }
            catch (NullReferenceException exception)
            {
                throw SelectorErrors.Failed(Builder.PropertyExpression, exception);
            }

            if (!EvaluateProperty(context, property, index, ref childContext)) return;
        }
    }

    private bool EvaluateProperty(ValidationContext<TModel> context, TProperty property, int elementIndex, ref object? childContext)
    {
        var rules = _rules;
        if (HasIncludes) return EvaluatePropertyWithIncludes(rules, context, property, elementIndex, ref childContext);

        for (var ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
        {
            if (rules[ruleIndex].Validate(property, context))
            {
                context.ResetRuleMessageState();
                continue;
            }

            if (OnRuleFailed(rules[ruleIndex], property, elementIndex, context)) return false;
        }

        return true;
    }

    private bool EvaluatePropertyWithIncludes(
        PropertyValidator<TModel, TProperty>[] rules,
        ValidationContext<TModel> context,
        TProperty property,
        int elementIndex,
        ref object? childContext
    )
    {
        var builderOptions = Builder.OptionsFor(context);
        for (var ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
        {
            var blockingBefore = context.BlockingErrorCount;
            if (RuleEvaluator.EvaluateElement(
                    rules[ruleIndex],
                    property,
                    context,
                    Builder.DisplayName,
                    ElementPropertyBuilder.ElementsBuilder.NamePath,
                    elementIndex,
                    Builder.NamePath,
                    builderOptions,
                    ref childContext)) continue;

            if (context.BlockingErrorCount == blockingBefore) continue;

            if (TrackChainFailure) context.MarkRuleChainFailed(Builder);

            if (StopOnFailure) return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool OnRuleFailed(
        PropertyValidator<TModel, TProperty> rule,
        TProperty property,
        int elementIndex,
        ValidationContext<TModel> context
    )
    {
        var builderOptions = Builder.OptionsFor(context);
        var blockingBefore = context.BlockingErrorCount;
        var propertyName = PropertyNameComposer.FormatElement(
            ElementPropertyBuilder.ElementsBuilder.NamePath, elementIndex, Builder.NamePath, builderOptions);

        context.AddValidationError(propertyName, Builder.DisplayName, property, builderOptions, rule);
        return EndFailedRule(context, blockingBefore);
    }
}

file static class RulePlan
{
    internal static PropertyValidator<TModel, TProperty>[] Snapshot<TModel, TProperty>(List<PropertyValidator> validators)
    {
        var rules = new PropertyValidator<TModel, TProperty>[validators.Count];
        for (var index = 0; index < rules.Length; index++)
            rules[index] = Unsafe.As<PropertyValidator<TModel, TProperty>>(validators[index]);

        return rules;
    }
}

internal abstract class AsyncValidationEvaluator<TModel> : ValidationEvaluator<TModel>
{
    // Whether an async Include is among the rules, so only an element walk with one sets up a reusable child slot.
    private protected bool HasIncludes;

    // A failure here records nothing on the context, unlike a sync rule's in an async chain: the async phase asks
    // HasRuleChainFailure only before a chain's async rules start, so no one would read the record, and making it costs
    // the context's extras and a HashSet.
    internal List<PropertyValidator> Validators { get; } = [];

    internal virtual void Freeze()
    {
        foreach (var validator in Validators)
            if (validator is IIncludeValidator)
                HasIncludes = true;
    }

    // One child context serves every element's include in a walk, as in the sync evaluators. A collection of fewer than
    // two elements has nothing to reuse it for, so its walk makes no slot, and each include makes its own context, as
    // for a single value.
    private protected IncludeChildSlot? CreateIncludeSlot<TElement>(IEnumerable<TElement> elements) =>
        HasIncludes && elements is not ICollection<TElement> { Count: < 2 } ? new IncludeChildSlot() : null;

    // A list walk already knows its count, which spares the interface check above: size is a List<T>'s count, read for
    // its resize guard, and any other list's Count is read only when the chain has an include.
    private protected IncludeChildSlot? CreateIncludeSlot<TElement>(IList<TElement> list, int size) =>
        HasIncludes && (size >= 0 ? size : list.Count) > 1 ? new IncludeChildSlot() : null;

    // Task on purpose — a ValueTask conversion was benchmarked and changed nothing (109.7 vs 110.0 ns, identical
    // allocations): a non-generic async Task that completes synchronously already returns the cached
    // Task.CompletedTask, so there is no per-evaluation Task to save. Don't churn this again without a
    // measurement showing otherwise.
    internal abstract Task EvaluateAsync(ValidationContext<TModel> context, CancellationToken cancellationToken);
}

internal class AsyncValidationEvaluator<TModel, TProperty> : AsyncValidationEvaluator<TModel>
{
    private readonly ElementsReader<TModel, TProperty> _elementsReader;

    private readonly ChainValueReader<TModel, TProperty> _valueReader;

    public AsyncValidationEvaluator(PropertyValidatorBuilder<TModel> builder)
    {
        Builder = builder;
        _valueReader = new(builder);
        _elementsReader = new(builder);
    }

    // Validators still suspended on real I/O take the awaiting path below; everything that completes synchronously
    // (cache hits, in-memory checks) runs here without any state machine per rule.
    internal override Task EvaluateAsync(ValidationContext<TModel> context, CancellationToken cancellationToken)
    {
        if (Builder.IsElementsBuilder) return EvaluateElements(context, cancellationToken);

        TProperty property;
        if (Builder.IsNestedPath)
        {
            // Nested selector: a null intermediate skips the chain instead of throwing (see sync evaluator).
            var accessed = _valueReader.ReadNested(context.Model);
            if (!accessed.HasValue) return Task.CompletedTask;

            property = accessed.Value;
        }
        else property = _valueReader.Read(context.Model);

        for (var validatorIndex = 0; validatorIndex < Validators.Count; validatorIndex++)
        {
            var validator = Unsafe.As<AsyncPropertyValidator<TModel, TProperty>>(Validators[validatorIndex]);
            if (validator is IAsyncIncludeValidator<TModel, TProperty>)
                return EvaluateRemainingAsync(property, validatorIndex, context, cancellationToken);

            var pending = validator.ValidateAsync(property, context, cancellationToken);
            if (!pending.IsCompletedSuccessfully)
                return ResumeAsync(pending, validator, property, validatorIndex, context, cancellationToken);

            if (OnRuleCompleted(pending.Result, validator, property, context)) return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    // Records the outcome; true means the chain stops here.
    private bool OnRuleCompleted(
        bool isValid,
        AsyncPropertyValidator<TModel, TProperty> validator,
        TProperty property,
        ValidationContext<TModel> context
    )
    {
        if (isValid)
        {
            context.ResetRuleMessageState();
            return false;
        }

        var blockingBefore = context.BlockingErrorCount;
        context.AddValidationError(Builder.PropertyNameFor(context), Builder.DisplayName, property, Builder.OptionsFor(context), validator);
        context.ResetRuleMessageState();
        if (context.BlockingErrorCount == blockingBefore) return false;

        return Builder.GetRuleCascadeMode() == ValidationCascadeMode.Stop;
    }

    private async Task ResumeAsync(
        Task<bool> pending,
        AsyncPropertyValidator<TModel, TProperty> validator,
        TProperty property,
        int validatorIndex,
        ValidationContext<TModel> context,
        CancellationToken cancellationToken
    )
    {
        if (OnRuleCompleted(await pending.ConfigureAwait(false), validator, property, context)) return;

        await EvaluateRemainingAsync(property, validatorIndex + 1, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task EvaluateRemainingAsync(
        TProperty property,
        int firstValidator,
        ValidationContext<TModel> context,
        CancellationToken cancellationToken
    )
    {
        var builderOptions = Builder.OptionsFor(context);
        var cascadeMode = Builder.GetRuleCascadeMode();
        var propertyName = Builder.PropertyNameFor(context);
        for (var validatorIndex = firstValidator; validatorIndex < Validators.Count; validatorIndex++)
        {
            var validator = Unsafe.As<AsyncPropertyValidator<TModel, TProperty>>(Validators[validatorIndex]);
            var blockingBefore = context.BlockingErrorCount;
            if (await RuleEvaluator.EvaluateAsync(
                    validator,
                    property,
                    context,
                    Builder.DisplayName,
                    Builder.NamePath,
                    propertyName,
                    builderOptions,
                    cancellationToken).ConfigureAwait(false)) continue;

            if (context.BlockingErrorCount == blockingBefore) continue;

            if (cascadeMode == ValidationCascadeMode.Stop) return;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    // A failed element rule's error, named as RuleEvaluator.EvaluateElementAsync names it.
    private void AddElementError(
        AsyncPropertyValidator<TModel, TProperty> validator,
        TProperty element,
        int index,
        ValidatorBuilderOptionsSnapshot builderOptions,
        ValidationContext<TModel> context
    )
    {
        var propertyName = PropertyNameComposer.FormatElement(Builder.NamePath, index, null, builderOptions);
        context.AddValidationError(propertyName, Builder.DisplayName, element, builderOptions, validator);
    }

    // A walk over a list, with no Include, runs here while each rule's task has already completed, so an element costs
    // no state machine: in an async method every local lived in it, which made the walk about twice as slow on x64. The
    // first rule still pending hands the rest of the walk to WalkListAsync. Any other walk awaits as it goes.
    private Task EvaluateElements(ValidationContext<TModel> context, CancellationToken cancellationToken)
    {
        var elements = _elementsReader.Read(context);
        if (elements == null) return Task.CompletedTask;

        if (HasIncludes || elements is not IList<TProperty> list)
            return EvaluateElementsAsync(elements, context, cancellationToken);

        // A List<T> that changes size throws, as in the sync evaluator: before each element and wherever the walk ends.
        var size = ElementSourceGuard.SizeOf(list);
        for (var index = 0; index < list.Count; index++)
        {
            ElementSourceGuard.ThrowIfResized(list, size);
            cancellationToken.ThrowIfCancellationRequested();
            var element = list[index];
            for (var validatorIndex = 0; validatorIndex < Validators.Count; validatorIndex++)
            {
                var validator = Unsafe.As<AsyncPropertyValidator<TModel, TProperty>>(Validators[validatorIndex]);
                var blockingBefore = context.BlockingErrorCount;
                Task<bool> pending;

                try
                {
                    pending = validator.ValidateAsync(element, context, cancellationToken);
                }
                catch
                {
                    context.ResetRuleMessageState();
                    throw;
                }

                if (!pending.IsCompletedSuccessfully)
                {
                    return WalkListAsync(
                        pending, blockingBefore, list, size, index, element, validatorIndex, context,
                        cancellationToken);
                }

                if (pending.Result)
                {
                    context.ResetRuleMessageState();
                    continue;
                }

                if (OnRuleFailed(validator, element, index, blockingBefore, context)) continue;

                ElementSourceGuard.ThrowIfResized(list, size);

                return Task.CompletedTask;
            }
        }

        ElementSourceGuard.ThrowIfResized(list, size);

        return Task.CompletedTask;
    }

    // The rest of a list walk from a rule whose task was still pending, as EvaluateElements walks it, awaiting
    // each rule's task.
    private async Task WalkListAsync(
        Task<bool> pending,
        int blockingBefore,
        IList<TProperty> list,
        int size,
        int index,
        TProperty element,
        int validatorIndex,
        ValidationContext<TModel> context,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var validator = Unsafe.As<AsyncPropertyValidator<TModel, TProperty>>(Validators[validatorIndex]);
            bool isValid;

            try
            {
                isValid = await pending.ConfigureAwait(false);
            }
            catch
            {
                context.ResetRuleMessageState();
                throw;
            }

            if (isValid) context.ResetRuleMessageState();
            else if (!OnRuleFailed(validator, element, index, blockingBefore, context)) break;

            // The next rule, of this element or of the next one.
            if (++validatorIndex == Validators.Count)
            {
                if (++index >= list.Count) break;

                ElementSourceGuard.ThrowIfResized(list, size);
                cancellationToken.ThrowIfCancellationRequested();
                element = list[index];
                validatorIndex = 0;
            }

            blockingBefore = context.BlockingErrorCount;

            try
            {
                pending = Unsafe.As<AsyncPropertyValidator<TModel, TProperty>>(Validators[validatorIndex])
                    .ValidateAsync(element, context, cancellationToken);
            }
            catch
            {
                context.ResetRuleMessageState();
                throw;
            }
        }

        ElementSourceGuard.ThrowIfResized(list, size);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    // A failed element rule, recorded as RuleEvaluator.EvaluateElementAsync records it. True means the walk goes on.
    private bool OnRuleFailed(
        AsyncPropertyValidator<TModel, TProperty> validator,
        TProperty element,
        int index,
        int blockingBefore,
        ValidationContext<TModel> context
    )
    {
        try
        {
            AddElementError(validator, element, index, Builder.OptionsFor(context), context);
        }
        finally
        {
            context.ResetRuleMessageState();
        }

        if (context.BlockingErrorCount == blockingBefore) return true;

        return Builder.GetRuleCascadeMode() != ValidationCascadeMode.Stop;
    }

    // The awaiting element walk, which a chain with an Include or a source that isn't a list takes.
    private async Task EvaluateElementsAsync(
        IEnumerable<TProperty> elements,
        ValidationContext<TModel> context,
        CancellationToken cancellationToken
    )
    {
        var builderOptions = Builder.OptionsFor(context);
        var cascadeMode = Builder.GetRuleCascadeMode();
        IncludeChildSlot? slot;

        // A List<T> that changes size throws, as in the sync evaluator: before each element and wherever the walk ends.
        if (elements is IList<TProperty> list)
        {
            var size = ElementSourceGuard.SizeOf(list);
            slot = CreateIncludeSlot(list, size);
            for (var index = 0; index < list.Count; index++)
            {
                ElementSourceGuard.ThrowIfResized(list, size);
                cancellationToken.ThrowIfCancellationRequested();
                var element = list[index];
                for (var validatorIndex = 0; validatorIndex < Validators.Count; validatorIndex++)
                {
                    var validator = Unsafe.As<AsyncPropertyValidator<TModel, TProperty>>(Validators[validatorIndex]);
                    var blockingBefore = context.BlockingErrorCount;
                    if (await RuleEvaluator.EvaluateElementAsync(
                            validator,
                            element,
                            context,
                            Builder.DisplayName,
                            Builder.NamePath,
                            index,
                            null,
                            builderOptions,
                            slot,
                            cancellationToken).ConfigureAwait(false)) continue;

                    if (context.BlockingErrorCount == blockingBefore) continue;

                    if (cascadeMode == ValidationCascadeMode.Stop)
                    {
                        ElementSourceGuard.ThrowIfResized(list, size);
                        return;
                    }
                }
            }

            ElementSourceGuard.ThrowIfResized(list, size);

            return;
        }

        // CreateIncludeSlot reads only the source's type and Count, so the loop below is the one enumeration.
        // ReSharper disable once PossibleMultipleEnumeration
        slot = CreateIncludeSlot(elements);
        var elementIndex = 0;

        // ReSharper disable once PossibleMultipleEnumeration
        foreach (var element in elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var validatorIndex = 0; validatorIndex < Validators.Count; validatorIndex++)
            {
                var validator = Unsafe.As<AsyncPropertyValidator<TModel, TProperty>>(Validators[validatorIndex]);
                var blockingBefore = context.BlockingErrorCount;

                bool isValid;
                if (HasIncludes)
                {
                    isValid = await RuleEvaluator.EvaluateElementAsync(
                        validator,
                        element,
                        context,
                        Builder.DisplayName,
                        Builder.NamePath,
                        elementIndex,
                        null,
                        builderOptions,
                        slot,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        var pending = validator.ValidateAsync(element, context, cancellationToken);
                        isValid = pending.IsCompletedSuccessfully
                            ? pending.Result
                            : await pending.ConfigureAwait(false);

                        if (!isValid) AddElementError(validator, element, elementIndex, builderOptions, context);
                    }
                    finally
                    {
                        context.ResetRuleMessageState();
                    }
                }

                if (isValid) continue;

                if (context.BlockingErrorCount == blockingBefore) continue;

                if (cascadeMode == ValidationCascadeMode.Stop) return;
            }

            elementIndex++;
        }
    }
}

internal class AsyncElementPropertyValidationEvaluator<TModel, TElement, TProperty>
    : AsyncValidationEvaluator<TModel>
{
    private readonly ElementsReader<TModel, TElement> _elementsReader;

    private readonly ElementMemberReader<TModel, TElement, TProperty> _memberReader;

    public AsyncElementPropertyValidationEvaluator(ElementPropertyValidatorBuilder<TModel, TElement, TProperty> builder)
    {
        Builder = builder;
        ElementPropertyBuilder = builder;
        _memberReader = new(builder);
        _elementsReader = new(builder);
    }

    private ElementPropertyValidatorBuilder<TModel, TElement, TProperty> ElementPropertyBuilder { get; }

    internal override void Freeze()
    {
        base.Freeze();
        _memberReader.Freeze();
    }

    // A walk over a list runs here while each element's rules complete synchronously, so an element costs no state
    // machine: in an async method every local lived in it, which made the walk about 20% slower on x64. The first
    // element whose rules are still pending hands the rest of the walk to WalkListAsync.
    internal override Task EvaluateAsync(ValidationContext<TModel> context, CancellationToken cancellationToken)
    {
        var elements = _elementsReader.Read(context);
        if (elements == null) return Task.CompletedTask;
        if (elements is not IList<TElement> list) return EvaluateSequenceAsync(elements, context, cancellationToken);

        // A List<T> that changes size throws, as in the sync evaluator: before each element and wherever the walk ends.
        var size = ElementSourceGuard.SizeOf(list);
        var slot = CreateIncludeSlot(list, size);

        // The read is written out as in the sync walk, and for the same reason: a TryRead call per element, which its
        // catch keeps from being inlined, cost an async 10,000-element walk about 15%.
        var read = _memberReader.IsNestedPath ? null : _memberReader.Accessor;
        for (var index = 0; index < list.Count; index++)
        {
            ElementSourceGuard.ThrowIfResized(list, size);
            cancellationToken.ThrowIfCancellationRequested();
            var element = list[index];
            TProperty property;
            if (read is null)
            {
                if (!_memberReader.TryReadNested(element, out property)) continue;
            }
            else
            {
                if (element is null) continue;

                try
                {
                    property = read(element);
                }
                catch (NullReferenceException exception)
                {
                    throw SelectorErrors.Failed(Builder.PropertyExpression, exception);
                }
            }

            // EvaluateElementAsync's reset, written out so that the rules inline here.
            ValueTask<bool> goesOn;

            try
            {
                goesOn = HasIncludes
                    ? new ValueTask<bool>(EvaluatePropertyAsync(context, property, index, slot, cancellationToken))
                    : EvaluateRulesAsync(context, property, index, cancellationToken);
            }
            catch
            {
                context.ResetRuleMessageState();
                throw;
            }

            if (!goesOn.IsCompletedSuccessfully)
                return WalkListAsync(goesOn, list, size, slot, index, context, cancellationToken);

            if (goesOn.Result) continue;

            ElementSourceGuard.ThrowIfResized(list, size);

            return Task.CompletedTask;
        }

        ElementSourceGuard.ThrowIfResized(list, size);

        return Task.CompletedTask;
    }

    // The rest of a list walk from an element whose rules were still pending, awaiting each element's rules.
    private async Task WalkListAsync(
        ValueTask<bool> pending,
        IList<TElement> list,
        int size,
        IncludeChildSlot? slot,
        int index,
        ValidationContext<TModel> context,
        CancellationToken cancellationToken
    )
    {
        if (await pending.ConfigureAwait(false))
        {
            for (index++; index < list.Count; index++)
            {
                ElementSourceGuard.ThrowIfResized(list, size);
                cancellationToken.ThrowIfCancellationRequested();
                if (!_memberReader.TryRead(list[index], out var property)) continue;

                var goesOn = EvaluateElementAsync(context, property, index, slot, cancellationToken);
                if (!await goesOn.ConfigureAwait(false)) break;
            }
        }

        ElementSourceGuard.ThrowIfResized(list, size);
    }

    // A walk over any other source, awaiting each element's rules.
    private async Task EvaluateSequenceAsync(
        IEnumerable<TElement> elements,
        ValidationContext<TModel> context,
        CancellationToken cancellationToken
    )
    {
        // CreateIncludeSlot reads only the source's type and Count, so the loop below is the one enumeration.
        // ReSharper disable once PossibleMultipleEnumeration
        var slot = CreateIncludeSlot(elements);
        var elementIndex = 0;

        // ReSharper disable once PossibleMultipleEnumeration
        foreach (var element in elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_memberReader.TryRead(element, out var property))
            {
                var goesOn = EvaluateElementAsync(context, property, elementIndex, slot, cancellationToken);
                if (!await goesOn.ConfigureAwait(false)) return;
            }

            elementIndex++;
        }
    }

    // An element's rules from the awaiting walks, the Include's or the chain's own. A rule that throws before returning
    // its task has its message state reset here. True means the walk goes on.
    private ValueTask<bool> EvaluateElementAsync(
        ValidationContext<TModel> context,
        TProperty property,
        int elementIndex,
        IncludeChildSlot? slot,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return HasIncludes
                ? new ValueTask<bool>(EvaluatePropertyAsync(context, property, elementIndex, slot, cancellationToken))
                : EvaluateRulesAsync(context, property, elementIndex, cancellationToken);
        }
        catch
        {
            context.ResetRuleMessageState();
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // An element's rules when the chain has no Include, run here until one is still pending. While every rule completes
    // synchronously the element costs no state machine and no task: the result is a ValueTask over a plain bool. An
    // async method per element made an element-property walk with an async rule several times its synchronous cost.
    // True means the walk goes on. A rule that throws before returning its task is the caller's to reset: without a
    // catch this inlines into the walk, and as a call it made an async 10,000-element walk about 45% slower on x64.
    private ValueTask<bool> EvaluateRulesAsync(
        ValidationContext<TModel> context,
        TProperty property,
        int elementIndex,
        CancellationToken cancellationToken
    )
    {
        for (var validatorIndex = 0; validatorIndex < Validators.Count; validatorIndex++)
        {
            var validator = Unsafe.As<AsyncPropertyValidator<TModel, TProperty>>(Validators[validatorIndex]);
            var blockingBefore = context.BlockingErrorCount;
            var pending = validator.ValidateAsync(property, context, cancellationToken);
            if (!pending.IsCompletedSuccessfully)
            {
                return new(ResumeAsync(
                    pending, validator, validatorIndex, blockingBefore, context, property, elementIndex,
                    cancellationToken));
            }

            if (!Settle(pending.Result, validator, property, elementIndex, blockingBefore, context)) return new(false);
        }

        return new(true);
    }

    // The rest of an element's rules from one that was still pending. True means the walk goes on.
    private async Task<bool> ResumeAsync(
        Task<bool> pending,
        AsyncPropertyValidator<TModel, TProperty> validator,
        int validatorIndex,
        int blockingBefore,
        ValidationContext<TModel> context,
        TProperty property,
        int elementIndex,
        CancellationToken cancellationToken
    )
    {
        bool isValid;

        try
        {
            isValid = await pending.ConfigureAwait(false);
        }
        catch
        {
            context.ResetRuleMessageState();
            throw;
        }

        if (!Settle(isValid, validator, property, elementIndex, blockingBefore, context)) return false;

        for (var index = validatorIndex + 1; index < Validators.Count; index++)
        {
            var next = Unsafe.As<AsyncPropertyValidator<TModel, TProperty>>(Validators[index]);
            var before = context.BlockingErrorCount;
            bool valid;

            try
            {
                valid = await next.ValidateAsync(property, context, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                context.ResetRuleMessageState();
                throw;
            }

            if (!Settle(valid, next, property, elementIndex, before, context)) return false;
        }

        return true;
    }

    // Records a completed rule as RuleEvaluator.EvaluateElementAsync and EvaluatePropertyAsync do. True means the
    // walk goes on.
    private bool Settle(
        bool isValid,
        AsyncPropertyValidator<TModel, TProperty> validator,
        TProperty property,
        int elementIndex,
        int blockingBefore,
        ValidationContext<TModel> context
    )
    {
        if (!isValid)
            return OnRuleFailed(validator, property, elementIndex, blockingBefore, context);

        context.ResetRuleMessageState();

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool OnRuleFailed(
        AsyncPropertyValidator<TModel, TProperty> validator,
        TProperty property,
        int elementIndex,
        int blockingBefore,
        ValidationContext<TModel> context
    )
    {
        try
        {
            var builderOptions = Builder.OptionsFor(context);
            var propertyName = PropertyNameComposer.FormatElement(
                ElementPropertyBuilder.ElementsBuilder.NamePath, elementIndex, Builder.NamePath, builderOptions);

            context.AddValidationError(propertyName, Builder.DisplayName, property, builderOptions, validator);
        }
        finally
        {
            context.ResetRuleMessageState();
        }

        if (context.BlockingErrorCount == blockingBefore) return true;

        return Builder.GetRuleCascadeMode() != ValidationCascadeMode.Stop;
    }

    private async Task<bool> EvaluatePropertyAsync(
        ValidationContext<TModel> context,
        TProperty property,
        int elementIndex,
        IncludeChildSlot? slot,
        CancellationToken cancellationToken
    )
    {
        var builderOptions = Builder.OptionsFor(context);
        var cascadeMode = Builder.GetRuleCascadeMode();
        for (var validatorIndex = 0; validatorIndex < Validators.Count; validatorIndex++)
        {
            var validator = Unsafe.As<AsyncPropertyValidator<TModel, TProperty>>(Validators[validatorIndex]);
            var blockingBefore = context.BlockingErrorCount;
            if (await RuleEvaluator.EvaluateElementAsync(
                    validator,
                    property,
                    context,
                    Builder.DisplayName,
                    ElementPropertyBuilder.ElementsBuilder.NamePath,
                    elementIndex,
                    Builder.NamePath,
                    builderOptions,
                    slot,
                    cancellationToken).ConfigureAwait(false)) continue;

            if (context.BlockingErrorCount == blockingBefore) continue;

            if (cascadeMode == ValidationCascadeMode.Stop) return false;
        }

        return true;
    }
}

/// <summary>Turns a selector's NullReferenceException into one that names the selector, with the original inside, for the readers below.</summary>
file static class SelectorErrors
{
    internal static NullReferenceException Failed(LambdaExpression expression, NullReferenceException exception) =>
        new($"NullReferenceException occurred while executing the expression for '{expression}'. " +
            "If this property can be null, consider adding a null check using a Where condition.", exception);
}

// The value read of a property chain, owned by its evaluator. Its reads are instance methods, as ElementMemberReader's
// are, so a call takes the generic context from the reader: from shared generic code, a static generic method is looked
// up in the caller's generic dictionary at every call, once per chain per validation.
internal sealed class ChainValueReader<TModel, TProperty>(PropertyValidatorBuilder<TModel> builder)
{
    private Func<TModel, TProperty>? _accessor;

    private Func<TModel, (bool HasValue, TProperty Value)>? _safeAccessor;

    internal TProperty Read(TModel model)
    {
        try
        {
            return (_accessor ??= PropertyAccessorCache<TModel, TProperty>.GetOrAdd(builder))(model);
        }
        catch (NullReferenceException exception)
        {
            throw SelectorErrors.Failed(builder.PropertyExpression, exception);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // No exception to rewrap: a null intermediate comes back as HasValue == false, and the caller skips the chain.
    internal (bool HasValue, TProperty Value) ReadNested(TModel model) =>
        (_safeAccessor ??= NullSafeAccessorCache<TModel, TProperty>.GetOrAdd(
            builder.PropertyPath, builder.IsPathFaithful, builder.PropertyExpression))(model);
}

// The element source of an elements or element-property chain, owned by its evaluator and read once per walk. It reads
// through the chain that owns the elements, which reads them once per validation when several chains walk them.
internal sealed class ElementsReader<TModel, TElement>(PropertyValidatorBuilder<TModel> chain)
{
    private Func<TModel, IEnumerable<TElement>?>? _accessor;

    internal IEnumerable<TElement>? Read(ValidationContext<TModel> context)
    {
        var source = chain.ElementsSourceOwner!;

        try
        {
            _accessor ??= ElementsAccessorCache<TModel, TElement>.GetOrAdd(source);
            var slot = source.ElementSourceSlot;
            return slot < 0
                ? _accessor(context.Model)
                : context.GetElements(slot, source.PrimaryBuilder.ElementSourceCount, _accessor);
        }
        catch (NullReferenceException exception)
        {
            throw SelectorErrors.Failed(source.PropertyExpression, exception);
        }
    }
}

// The member read of an element-property chain, owned by its evaluator. It runs once per element, so it is an instance
// method: from shared generic code, a static generic method is looked up in the caller's generic dictionary at every
// call, and that lookup and the wider call made a 10,000-element walk 27% slower.
internal sealed class ElementMemberReader<TModel, TElement, TProperty>(PropertyValidatorBuilder<TModel> builder)
{
    private Func<TElement, (bool HasValue, TProperty Value)>? _safeAccessor;

    internal bool IsNestedPath { get; private set; }

    // The accessor of a selector that isn't nested. A caller that reads through it rewraps its NullReferenceException.
    internal Func<TElement, TProperty> Accessor => field ??= PropertyAccessorCache<TElement, TProperty>.GetOrAdd(
        builder.PropertyPath, builder.IsPathFaithful, builder.PropertyExpression);

    // The chain settles its path flags when it freezes, before its evaluators freeze.
    internal void Freeze() => IsNestedPath = builder.IsNestedPath;

    // An element's member, read null-safely when the selector is nested; false when there is nothing to read. The
    // walks over a list or an array read through Accessor and TryReadNested instead, as this catch keeps the JIT
    // from inlining it.
    internal bool TryRead(TElement element, out TProperty property)
    {
        if (IsNestedPath) return TryReadNested(element, out property);

        property = default!;
        if (element is null) return false;

        try
        {
            property = Accessor(element);
            return true;
        }
        catch (NullReferenceException exception)
        {
            throw SelectorErrors.Failed(builder.PropertyExpression, exception);
        }
    }

    // A nested selector's read: a null element or intermediate comes back as false, so there is nothing to rewrap and
    // no catch to keep the JIT from inlining it.
    internal bool TryReadNested(TElement element, out TProperty property)
    {
        property = default!;
        if (element is null) return false;

        var accessed = (_safeAccessor ??= NullSafeAccessorCache<TElement, TProperty>.GetOrAdd(
            builder.PropertyPath, builder.IsPathFaithful, builder.PropertyExpression))(element);

        if (!accessed.HasValue) return false;

        property = accessed.Value;

        return true;
    }
}

file static class RuleEvaluator
{
    internal static bool Evaluate<TModel, TProperty>(
        PropertyValidator<TModel, TProperty> validator,
        TProperty value,
        ValidationContext<TModel> context,
        string? displayName,
        string corePath,
        string propertyName,
        in ValidatorBuilderOptionsSnapshot builderOptions,
        ref object? childContext
    )
    {
        try
        {
            if (validator is ISyncIncludeValidator<TModel, TProperty> include)
            {
                return include.ValidateIncluded(
                    value, context, PropertyPathValue.Property(corePath), builderOptions, ref childContext);
            }

            var isValid = validator.Validate(value, context);
            if (!isValid)
                context.AddValidationError(propertyName, displayName, value, builderOptions, validator);

            return isValid;
        }
        finally
        {
            context.ResetRuleMessageState();
        }
    }

    internal static bool EvaluateElement<TModel, TProperty>(
        PropertyValidator<TModel, TProperty> validator,
        TProperty value,
        ValidationContext<TModel> context,
        string? displayName,
        string collectionPath,
        int elementIndex,
        string? elementPath,
        in ValidatorBuilderOptionsSnapshot builderOptions,
        ref object? childContext
    )
    {
        try
        {
            if (validator is ISyncIncludeValidator<TModel, TProperty> include)
            {
                return include.ValidateIncluded(
                    value,
                    context,
                    PropertyPathValue.Element(collectionPath, elementIndex, elementPath),
                    builderOptions,
                    ref childContext);
            }

            var isValid = validator.Validate(value, context);
            if (isValid) return isValid;

            var propertyName = PropertyNameComposer.FormatElement(
                collectionPath, elementIndex, elementPath, builderOptions);

            context.AddValidationError(propertyName, displayName, value, builderOptions, validator);

            return isValid;
        }
        finally
        {
            context.ResetRuleMessageState();
        }
    }

    internal static async Task<bool> EvaluateAsync<TModel, TProperty>(
        AsyncPropertyValidator<TModel, TProperty> validator,
        TProperty value,
        ValidationContext<TModel> context,
        string? displayName,
        string corePath,
        string propertyName,
        ValidatorBuilderOptionsSnapshot builderOptions,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (validator is IAsyncIncludeValidator<TModel, TProperty> include)
            {
                return await include.ValidateIncludedAsync(
                    value,
                    context,
                    PropertyPathValue.Property(corePath),
                    builderOptions,
                    null,
                    cancellationToken).ConfigureAwait(false);
            }

            var isValid = await validator.ValidateAsync(value, context, cancellationToken).ConfigureAwait(false);
            if (!isValid)
                context.AddValidationError(propertyName, displayName, value, builderOptions, validator);

            return isValid;
        }
        finally
        {
            context.ResetRuleMessageState();
        }
    }

    internal static async Task<bool> EvaluateElementAsync<TModel, TProperty>(
        AsyncPropertyValidator<TModel, TProperty> validator,
        TProperty value,
        ValidationContext<TModel> context,
        string? displayName,
        string collectionPath,
        int elementIndex,
        string? elementPath,
        ValidatorBuilderOptionsSnapshot builderOptions,
        IncludeChildSlot? slot,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (validator is IAsyncIncludeValidator<TModel, TProperty> include)
            {
                return await include.ValidateIncludedAsync(
                    value,
                    context,
                    PropertyPathValue.Element(collectionPath, elementIndex, elementPath),
                    builderOptions,
                    slot,
                    cancellationToken).ConfigureAwait(false);
            }

            var isValid = await validator.ValidateAsync(value, context, cancellationToken).ConfigureAwait(false);
            if (!isValid)
            {
                var propertyName = PropertyNameComposer.FormatElement(
                    collectionPath, elementIndex, elementPath, builderOptions);

                context.AddValidationError(propertyName, displayName, value, builderOptions, validator);
            }

            return isValid;
        }
        finally
        {
            context.ResetRuleMessageState();
        }
    }
}

file static class ErrorMessageExtensions
{
    /// <summary>Sentinel: the rule's message depends on per-call values, so it can't be precomputed.</summary>
    internal static readonly ValidationError NoStaticError = new(string.Empty);

    /// <summary>The complete error of a rule whose template has nothing to substitute but <c>{PropertyName}</c>.</summary>
    internal static ValidationError ResolveStaticError(
        string propertyName,
        string? displayName,
        in ValidatorBuilderOptionsSnapshot builderOptions,
        PropertyValidatorOptions validatorOptions
    )
    {
        var message = validatorOptions.ErrorMessage;

        // Both cases share one return: an early return for a null message would copy `message ?? string.Empty`, and
        // neither copy could take both of its sides.
        if (message != null)
        {
            message = PropertyNameComposer.WrapMessage(message, builderOptions);
            if (PropertyValidator.HasParameterPlaceholder(message)) return NoStaticError;

            message = message.Replace("{PropertyName}", displayName ?? propertyName, StringComparison.Ordinal);
        }

        return new ValidationError(
            propertyName, message ?? string.Empty, validatorOptions.ErrorCode,
            validatorOptions.ErrorSeverity ?? ValidationErrorSeverity.Error);
    }

    // The message names the property by its display name when the chain has one; the error keeps the reported name.
    internal static void AddValidationError<TModel, TProperty>(
        this ValidationContext<TModel> context,
        string propertyName,
        string? displayName,
        TProperty value,
        in ValidatorBuilderOptionsSnapshot builderOptions,
        PropertyValidator rule
    )
    {
        // Every rule that reports its own failures has options once it froze: the library's set them, and an
        // app's get defaults.
        var validatorOptions = rule.Options!;
        string? errorMessage;

        // A frozen template whose only placeholder is {PropertyName}, under a validator that wraps no message, has only
        // the name to fill in: joining it between the template's parts gives what Format would. The parts come last:
        // reading them splits the template, which a wrapped message would never use.
        if (context.ErrorMessage == null &&
            string.IsNullOrEmpty(builderOptions.ErrorMessagePrefix) &&
            string.IsNullOrEmpty(builderOptions.ErrorMessageSuffix) &&
            rule.MessageParts is { Length: > 0 } parts)
            errorMessage = string.Join(displayName ?? propertyName, parts);
        else
        {
            errorMessage = context.ErrorMessage ?? validatorOptions.ErrorMessage;
            if (errorMessage != null)
            {
                errorMessage = Format(PropertyNameComposer.WrapMessage(errorMessage, builderOptions),
                    displayName ?? propertyName, value, context);
            }
        }

        context.AddError(new(propertyName, errorMessage ?? string.Empty,
            validatorOptions.ErrorCode, validatorOptions.ErrorSeverity ?? ValidationErrorSeverity.Error));
    }

    // One pass over the template: substituted values are copied verbatim and never rescanned. Replacing placeholders in
    // turn let user input such as "{TargetValue}" pull a comparison target into the message. The value is only
    // stringified when the template references it.
    private static string Format<TModel, TProperty>(
        string template,
        string propertyName,
        TProperty value,
        ValidationContext<TModel> context
    )
    {
        var open = template.IndexOf('{');
        if (open < 0) return template;

        var message = new DefaultInterpolatedStringHandler(0, 0);
        var position = 0;
        while (open >= 0)
        {
            var close = template.IndexOf('}', open + 1);
            if (close < 0) break;

            message.AppendFormatted(template.AsSpan(position, open - position));
            var placeholder = template.AsSpan(open, close - open + 1);

            string? replacement = null;
            switch (placeholder)
            {
                case "{PropertyName}":
                    replacement = propertyName;
                    break;

                case "{PropertyValue}":
                    replacement = value.ToInvariantString();
                    break;

                default:
                {
                    if (context.ExistsMessageParameters && context.MessageParameters.TryGet(placeholder, out var parameter))
                        replacement = parameter;

                    break;
                }
            }

            if (replacement == null)
            {
                // Not a known placeholder: keep the brace and rescan from the next character.
                message.AppendFormatted('{');
                position = open + 1;
            }
            else
            {
                message.AppendFormatted(replacement);
                position = close + 1;
            }

            open = template.IndexOf('{', position);
        }

        message.AppendFormatted(template.AsSpan(position));
        return message.ToStringAndClear();
    }
}

file static class ElementSourceGuard
{
    // The size a List<T> had when its walk began, or -1 for any other list, whose size isn't checked.
    internal static int SizeOf<T>(IList<T> list) => list is List<T> typed ? typed.Count : -1;

    // A size is recorded only for a List<T>, so the cast holds, and List<T>.Count is a field read where IList<T>.Count
    // is an interface call on every element.
    internal static void ThrowIfResized<T>(IList<T> list, int size)
    {
        if (size >= 0 && Unsafe.As<List<T>>(list).Count != size) ThrowListChanged();
    }

    [DoesNotReturn]
    internal static void ThrowListChanged() =>
        throw new InvalidOperationException(
            "The list changed size while its elements were validated. A rule must not add or remove elements of the list it validates.");
}