using System.Runtime.CompilerServices;

namespace Sharp.Validator;

/// <summary>
/// One <c>Where</c> condition. A block's condition is a single gate shared by every chain the block wraps, so a validation runs it once however many
/// chains it gates: <see cref="ConditionResults" /> keeps its verdict for the rest of the run. Exactly one of the delegates is set.
/// </summary>
internal sealed class ConditionGate<TModel>
{
    private readonly Func<TModel, ValidationContext<TModel>, CancellationToken, Task<bool>>? _asyncCondition;

    private readonly Func<TModel, Task<bool>>? _asyncModelCondition;

    private readonly Func<TModel, CancellationToken, Task<bool>>? _asyncTokenCondition;

    private readonly Func<TModel, ValidationContext<TModel>, bool>? _condition;
    // Where(model => ...) and its async forms keep their condition as written: a lambda that dropped the context, or
    // the token as well, to call it would cost every check a second delegate call.
    private readonly Func<TModel, bool>? _modelCondition;

    internal ConditionGate(Func<TModel, bool> condition) => _modelCondition = condition;

    internal ConditionGate(Func<TModel, ValidationContext<TModel>, bool> condition) => _condition = condition;

    internal ConditionGate(Func<TModel, Task<bool>> condition)
    {
        _asyncModelCondition = condition;
        IsAsync = true;
    }

    internal ConditionGate(Func<TModel, CancellationToken, Task<bool>> condition)
    {
        _asyncTokenCondition = condition;
        IsAsync = true;
    }

    internal ConditionGate(Func<TModel, ValidationContext<TModel>, CancellationToken, Task<bool>> condition)
    {
        _asyncCondition = condition;
        IsAsync = true;
    }

    /// <summary>Whether the condition returns a task, which only the async path can await.</summary>
    internal bool IsAsync { get; }

    /// <summary>
    /// The gate's slot in <see cref="ConditionResults" />, unique within its validator, or -1 when a run reaches the gate only once, so there is no verdict
    /// to keep. Set when the validator freezes.
    /// </summary>
    internal int Slot { get; set; } = -1;

    /// <summary>How many times one run can reach the gate: once per phase of each chain it gates. Counted at freeze.</summary>
    internal int Reach { get; set; }

    /// <summary>
    /// Runs sync conditions in order until one fails and says whether all held, reusing the verdict the run already has for a slotted one. Inlined into the
    /// rule loops: most chains hold one condition, which needs no slot.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool AllMet(ConditionGate<TModel>[] conditions, ValidationContext<TModel> context, ref ConditionResults results)
    {
        foreach (var gate in conditions)
            if (!gate.IsMet(context, ref results))
                return false;

        return true;
    }

    /// <summary>
    /// Runs conditions from <paramref name="index" /> on and says whether all held (<c>true</c>) or one failed (<c>false</c>), or returns <c>null</c> with
    /// <paramref name="index" /> at an async condition the run has to await. <paramref name="awaited" /> is that condition's verdict once it has been
    /// awaited, recorded before going on. The async loop awaits only the condition itself, so the loop's state stays out of its state machine.
    /// </summary>
    internal static bool? MeetUntilAwait(
        ConditionGate<TModel>[] conditions,
        ref int index,
        bool? awaited,
        ValidationContext<TModel> context,
        ref ConditionResults results
    )
    {
        if (awaited is { } verdict)
        {
            var slot = conditions[index].Slot;
            if (slot >= 0) results.Set(slot, verdict);
            if (!verdict) return false;

            index++;
        }

        for (; index < conditions.Length; index++)
        {
            var gate = conditions[index];
            bool met;
            if (!gate.IsAsync) met = gate.IsMet(context, ref results);
            else if (gate.Slot < 0 || !results.TryGet(gate.Slot, out met)) return null;

            if (!met) return false;
        }

        return true;
    }

    /// <summary>Runs the sync condition, or reuses the verdict the run already has for it when the gate has a slot.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsMet(ValidationContext<TModel> context, ref ConditionResults results)
    {
        if (Slot < 0) return Run(context);

        return results.TryGet(Slot, out var met) ? met : RecordSync(context, ref results);
    }

    /// <summary>Starts the async condition, for a gate whose verdict the run has to await.</summary>
    internal Task<bool> RunAsync(ValidationContext<TModel> context, CancellationToken cancellationToken) =>
        _asyncModelCondition is { } modelCondition ? modelCondition(context.Model)
        : _asyncTokenCondition is { } tokenCondition ? tokenCondition(context.Model, cancellationToken)
        : _asyncCondition!(context.Model, context, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Run(ValidationContext<TModel> context) =>
        _modelCondition is { } modelCondition ? modelCondition(context.Model) : _condition!(context.Model, context);

    /// <summary>Runs the sync condition of a slotted gate the run has not reached yet and records its verdict.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool RecordSync(ValidationContext<TModel> context, ref ConditionResults results)
    {
        var met = Run(context);
        results.Set(Slot, met);
        return met;
    }
}

/// <summary>
/// The verdicts of the conditions one validation run has evaluated, so that each runs at most once per run. The rule loops keep it in a local rather
/// than in the context, so a validator without conditions pays nothing for it.
/// </summary>
internal struct ConditionResults
{
    // Condition i records "evaluated" in bit i % 32 of its word and "met" in bit 32 + i % 32. The first 32 conditions
    // share _first; only a validator with more allocates _more.
    private ulong _first;

    private ulong[]? _more;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly bool TryGet(int index, out bool met)
    {
        var word = index < 32 ? _first : MoreWord(index);
        var bit = index & 31;
        met = ((word >> (32 + bit)) & 1) != 0;
        return ((word >> bit) & 1) != 0;
    }

    internal void Set(int index, bool met)
    {
        var bit = index & 31;
        var flags = (1UL << bit) | (met ? 1UL << (32 + bit) : 0);
        if (index < 32)
        {
            _first |= flags;
            return;
        }

        var slot = (index >> 5) - 1;
        if (_more is null || slot >= _more.Length) Array.Resize(ref _more, slot + 1);

        _more[slot] |= flags;
    }

    private readonly ulong MoreWord(int index)
    {
        var slot = (index >> 5) - 1;
        return _more is { } more && slot < more.Length ? more[slot] : 0;
    }
}