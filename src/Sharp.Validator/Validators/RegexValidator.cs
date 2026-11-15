using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using PatternKey = (string Pattern, System.Text.RegularExpressions.RegexOptions Options, System.TimeSpan Timeout);

namespace Sharp.Validator.Validators;

/// <summary>Settings that every <see cref="RegexValidator{TModel}" /> shares.</summary>
public static class RegexValidator
{
    /// <summary>Default match timeout applied when callers don't supply their own. Mitigates ReDoS.</summary>
    public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(2.0);
}

/// <summary>
/// String must match a regular expression; the pattern can be constant or resolved from the model at validation time. <c>null</c>/empty is skipped. A
/// pattern the validator builds ignores case in the invariant culture; pass a <see cref="Regex" /> built in another culture to match in that one.
/// </summary>
public sealed class RegexValidator<TModel> : PropertyValidator<TModel, string?>
{
    private const int MaximumCachedDynamicPatterns = 256;

    // A constant pattern's instances, which every rule built over the same pattern, options and timeout shares.
    private readonly PatternInstances? _instances;

    // The Regex the caller passed in, which stays one shared instance.
    private readonly Regex? _regex;

    private readonly Func<TModel, Regex?>? _regexGetter;

    /// <summary>Creates the validator with a prebuilt <see cref="Regex" />; the caller controls its options and match timeout.</summary>
    public RegexValidator(Regex regex)
    {
        ArgumentNullException.ThrowIfNull(regex);
        _regex = regex;
        Configure();
    }

    /// <summary>Creates the validator from a constant pattern with the <see cref="RegexValidator.DefaultMatchTimeout" /> applied.</summary>
    public RegexValidator(string regexString, RegexOptions options)
        : this(regexString, options, RegexValidator.DefaultMatchTimeout)
    {
    }

    /// <summary>Creates the validator from a constant pattern with an explicit match timeout.</summary>
    public RegexValidator(string regexString, RegexOptions options, TimeSpan matchTimeout)
    {
        _instances = ConstantPatterns.Shared.GetOrAdd(regexString, options, matchTimeout);
        Configure();
    }

    /// <summary>Creates the validator with the <see cref="Regex" /> resolved from the model at validation time.</summary>
    public RegexValidator(Func<TModel, Regex?> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        _regexGetter = func;
        Configure();
    }

    /// <summary>
    /// Creates the validator with the pattern resolved from the model at validation time and the <see cref="RegexValidator.DefaultMatchTimeout" /> applied.
    /// </summary>
    public RegexValidator(Func<TModel, string?> func, RegexOptions options)
        : this(func, options, RegexValidator.DefaultMatchTimeout) =>
        ArgumentNullException.ThrowIfNull(func);

    /// <summary>
    /// Creates the validator with the pattern resolved from the model at validation time and an explicit match timeout; compiled patterns
    /// are cached (bounded).
    /// </summary>
    public RegexValidator(Func<TModel, string?> func, RegexOptions options, TimeSpan matchTimeout)
    {
        ArgumentNullException.ThrowIfNull(func);

        var cache = new ConcurrentDictionary<string, Regex>();
        _regexGetter = model =>
        {
            var pattern = func(model);

            // A null pattern fails as an invalid pattern does.
            if (pattern is null) return null;

            // Hit path first: ConcurrentDictionary.Count locks every stripe, so consulting it before the lookup taxed
            // the common (cached) case to guard against the rare saturated one.
            if (cache.TryGetValue(pattern, out var cached)) return cached;

            if (cache.Count >= MaximumCachedDynamicPatterns)
                return CreateDynamicRegex(pattern, options, matchTimeout);

            return cache.GetOrAdd(pattern,
                static (pattern, state) => CreateDynamicRegex(pattern, state.options, state.timeout),
                (options, timeout: matchTimeout));
        };

        Configure();
    }

    private void Configure() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.Regex;
        options.ErrorMessage = "'{PropertyName}' is not in the correct format.";
    });

    private static Regex CreateDynamicRegex(string expression, RegexOptions options, TimeSpan matchTimeout)
    {
        try
        {
            return PatternFactory.Create(expression, options, matchTimeout);
        }
        catch (RegexParseException exception)
        {
            throw new DynamicRegexFormatException(expression, exception);
        }
    }

    /// <summary>A match timeout (the ReDoS guard) counts as a validation failure rather than escaping as an exception.</summary>
    public override bool Validate(string? value, ValidationContext<TModel> context)
    {
        if (string.IsNullOrEmpty(value)) return true;

        var regex = _instances?.ForCurrentThread() ?? _regex;
        if (regex == null)
        {
            try
            {
                regex = _regexGetter!(context.Model);
            }
            catch (DynamicRegexFormatException exception)
            {
                context.MessageParameters.Add("RegularExpression", exception.Pattern);
                return false;
            }

            // A model that supplies no pattern, or no Regex, has nothing to match against, so it fails as an invalid
            // pattern does instead of escaping as an exception from the cache or the match.
            if (regex == null)
            {
                context.MessageParameters.Add("RegularExpression", "null");
                return false;
            }
        }

        try
        {
            if (regex.IsMatch(value)) return true;
        }
        catch (RegexMatchTimeoutException)
        {
            // The timeout exists to stop catastrophic backtracking on hostile input. Letting it escape would turn that
            // guard into an exception-based DoS (an unhandled 500 per request); a failed match is the safe verdict for
            // input the engine refused to finish.
        }

        if (NeedsMessageParameters(context))
            context.MessageParameters.Add("{RegularExpression}", regex.ToString());

        return false;
    }

    private sealed class DynamicRegexFormatException(string pattern, RegexParseException innerException)
        : Exception("The model supplied an invalid regular expression.", innerException)
    {
        internal string Pattern { get; } = pattern;
    }
}

// Regex keeps exactly one idle runner per instance: concurrent callers bounce that slot between cores and, on a miss,
// allocate a whole runner. Throughput of a shared instance falls as threads are added, so a constant pattern gets
// further instances, picked by core. The first thread to match it keeps the original, so a pattern only ever matched on
// one thread (a single-threaded batch) pays for one compare. The instances belong to the pattern, not to a rule: a
// Scoped validator is built in every scope, and building its own parsed each pattern again and wrote the code that runs
// it, most of what a Regex rule added to each scope. Rules sharing one original instead would contend for its runner.
internal sealed class PatternInstances
{
    private readonly Func<PatternKey, Regex> _create;
    private readonly PatternKey _key;
    private readonly Regex _original;
    private Regex?[]? _copies;
    private int _owner;

    internal PatternInstances(PatternKey key, Func<PatternKey, Regex> create, Regex original)
    {
        _key = key;
        _create = create;
        _original = original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Regex ForCurrentThread()
    {
        var thread = Environment.CurrentManagedThreadId;
        return thread == _owner ? _original : ForOtherThread(thread);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Regex ForOtherThread(int thread)
    {
        // Plain read first: a CAS takes the cache line exclusively even when it fails, and every other thread comes
        // through here on every call.
        if (ClaimOriginal(thread, _owner)) return _original;

        var copies = Copies(_copies);
        var index = Thread.GetCurrentProcessorId() & (copies.Length - 1);
        return copies[index] ?? (copies[index] = _create(_key));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // The caller made the plain read; a thread that saw no owner then tries to claim the original, and another
    // thread can win in between. Taking the observed owner keeps that lost race, which a test cannot time,
    // reachable without one.
    internal bool ClaimOriginal(int thread, int observedOwner) =>
        observedOwner == 0 && Interlocked.CompareExchange(ref _owner, thread, 0) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // The same for the copies' array: a thread that saw none publishes one, and another thread can publish first.
    // Taking the observed array keeps that lost race reachable without threads, so the coverage gate doesn't
    // depend on timing.
    internal Regex?[] Copies(Regex?[]? observed) =>
        observed ?? Interlocked.CompareExchange(ref _copies, new Regex?[RegexStripes.Count], null) ?? _copies!;
}

// Every rule over one constant pattern, options and timeout shares its instances, as the static Regex methods' cache
// shares theirs, and the process keeps them. Bounded, as each rule's dynamic patterns are, for an app that builds its
// constant patterns at run time: past the limit, a rule keeps instances of its own.
internal sealed class ConstantPatterns(int limit)
{
    internal static readonly ConstantPatterns Shared = new(256);

    private readonly ConcurrentDictionary<PatternKey, PatternInstances> _patterns = new();

    // Only the constructor that takes a pattern comes here. The instances build their copies through the delegate made
    // here rather than a call of their own: Validate is always reachable, and a call from there would keep the Regex
    // parser in every Native AOT app, even one that only passes in a [GeneratedRegex]. Made outside the generic rule,
    // the delegate keeps no rule type alive.
    internal PatternInstances GetOrAdd(string pattern, RegexOptions options, TimeSpan matchTimeout)
    {
        // Every pattern ignores case in the invariant culture, so with CultureInvariant or without is one key.
        PatternKey key = (pattern, options | RegexOptions.CultureInvariant, matchTimeout);
        if (_patterns.TryGetValue(key, out var shared)) return shared;

        // Built at once, so a pattern that doesn't parse throws where its rule is declared, and before the object that
        // holds it, so the two lie apart: the owner thread writes the original's runner slot on every match, while
        // every other thread reads the holder's fields on every match.
        var original = PatternFactory.Create(key.Pattern, key.Options, key.Timeout);
        var instances = new PatternInstances(
            key, static key => PatternFactory.Create(key.Pattern, key.Options, key.Timeout), original);

        return _patterns.Count < limit ? _patterns.GetOrAdd(key, instances) : instances;
    }
}

// IgnoreCase, as an option or as (?i) in the pattern, takes its casing table from the culture current when a Regex is
// built, so a rule would be judged by the server's locale, and a copy or a cached dynamic pattern by whichever request
// built it. Every pattern a rule builds ignores case in the invariant culture, as a [GeneratedRegex] does.
file static class PatternFactory
{
    internal static Regex Create(string pattern, RegexOptions options, TimeSpan matchTimeout) =>
        new(pattern, options | RegexOptions.CultureInvariant, matchTimeout);
}

file static class RegexStripes
{
    // One per core, the way ArrayPool partitions: threads running at the same moment are on different cores, which a
    // mask over managed thread ids can't promise. A power of two, so picking one is a mask. At most 64, which keeps
    // common server sizes at one copy per core: past that, cores share copies rather than every pattern growing with
    // the machine, each copy costing about 14 KiB, and a Compiled one a compile of its own.
    private const int Limit = 64;

    internal static readonly int Count = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Min(Environment.ProcessorCount, Limit));
}