using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Sharp.Validator.Result;

namespace Sharp.Validator;

/// <summary>
/// Builder-wide options applied to every property rule. A validator reads them when it freezes, and from then on they are read-only: a change throws
/// <see cref="InvalidOperationException" /> rather than go unseen.
/// </summary>
public sealed class ValidatorBuilderOptions
{
    internal static ValidatorBuilderOptions? InternalGlobal;

    private ErrorMessageTable? _errorMessages;

    /// <summary>
    /// How to behave after the first <see cref="ValidationErrorSeverity.Error" />-level failure. Warning and Information entries never stop validation.
    /// </summary>
    public ValidationCascadeMode? CascadeMode
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>Naming convention applied to property names in error output.</summary>
    public PropertyNamePolicy? PropertyNamePolicy
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>Literal prefix prepended after the property-name policy is applied to path segments.</summary>
    public string? PropertyNamePrefix
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>Literal suffix appended after the property-name policy is applied to path segments.</summary>
    public string? PropertyNameSuffix
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>Prefix prepended to every error message.</summary>
    public string? ErrorMessagePrefix
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>Suffix appended to every error message.</summary>
    public string? ErrorMessageSuffix
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>
    /// Default messages by error code. A rule whose message is set neither by its <c>configure</c> nor by its chain's options takes the entry for its error
    /// code, placeholders included, when its validator freezes. Entries set on a validator or an <c>AddValidators</c> scan win over Global's, code by code.
    /// Read-only, as the options are, once a validator has read them.
    /// </summary>
    public IDictionary<string, string> ErrorMessages => _errorMessages ??= new ErrorMessageTable(this);

    internal IReadOnlyDictionary<string, string>? ErrorMessageTable => _errorMessages?.Entries;

    /// <summary>
    /// Names a property in messages from the member its selector ends at, for example from a <c>[Display]</c> attribute; <c>null</c> keeps the reported
    /// name. Called when a validator freezes, once for each chain and for each cross-property target that is another property. The name is used as written,
    /// and the error's <c>PropertyName</c> keeps the path.
    /// </summary>
    public Func<MemberInfo, string?>? DisplayNameResolver
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>
    /// The clock <c>InPast</c>, <c>InFuture</c>, and <c>IsToday</c> read "now" from; <see cref="System.TimeProvider.System" /> when no level sets one. A
    /// validator reads it when it freezes, and an included validator reads its own.
    /// </summary>
    public TimeProvider? TimeProvider
    {
        get;
        set => field = Writable(value);
    }

    internal bool IsReadOnly { get; private set; }

    /// <summary>
    /// App-wide defaults consulted when an instance option is unset. Set them at startup: a validator reads them when it freezes, which makes the object
    /// read-only, and assigning a new object changes the defaults for validators that freeze afterward.
    /// </summary>
    public static ValidatorBuilderOptions Global
    {
        get
        {
            var current = Volatile.Read(ref InternalGlobal);
            if (current is not null) return current;

            var fresh = new ValidatorBuilderOptions();
            return InitializeGlobal(fresh);
        }
        set => Volatile.Write(ref InternalGlobal, value);
    }

    // A validator froze reading these options, so a change made now would go unseen by it.
    internal void MakeReadOnly() => IsReadOnly = true;

    internal void ThrowIfReadOnly()
    {
        if (IsReadOnly) throw new InvalidOperationException(ReadOnlyOptions.ReadWhenAValidatorFroze);
    }

    private T Writable<T>(T value)
    {
        ThrowIfReadOnly();
        return value;
    }

    internal static ValidatorBuilderOptions InitializeGlobal(ValidatorBuilderOptions fresh) =>
        Interlocked.CompareExchange(ref InternalGlobal, fresh, null) ?? fresh;
}

// The error-message table as callers see it: writable until a validator reads its options, then read-only, so an entry
// set afterward throws rather than go unseen. Reads go through to the entries.
internal sealed class ErrorMessageTable(ValidatorBuilderOptions owner) : IDictionary<string, string>
{
    internal Dictionary<string, string> Entries { get; } = new(StringComparer.Ordinal);

    private ICollection<KeyValuePair<string, string>> Pairs => Entries;

    public string this[string key]
    {
        get => Entries[key];
        set
        {
            owner.ThrowIfReadOnly();
            Entries[key] = value;
        }
    }

    public ICollection<string> Keys => Entries.Keys;

    public ICollection<string> Values => Entries.Values;

    public int Count => Entries.Count;

    public bool IsReadOnly => owner.IsReadOnly;

    public void Add(string key, string value)
    {
        owner.ThrowIfReadOnly();
        Entries.Add(key, value);
    }

    public void Add(KeyValuePair<string, string> item)
    {
        owner.ThrowIfReadOnly();
        Pairs.Add(item);
    }

    public void Clear()
    {
        owner.ThrowIfReadOnly();
        Entries.Clear();
    }

    public bool Contains(KeyValuePair<string, string> item) => Pairs.Contains(item);

    public bool ContainsKey(string key) => Entries.ContainsKey(key);

    public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex) => Pairs.CopyTo(array, arrayIndex);

    public bool Remove(string key)
    {
        owner.ThrowIfReadOnly();
        return Entries.Remove(key);
    }

    public bool Remove(KeyValuePair<string, string> item)
    {
        owner.ThrowIfReadOnly();
        return Pairs.Remove(item);
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) => Entries.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal interface IValidatorConfigurationTarget
{
    void ApplyDefaults(ValidatorBuilderOptions defaults);
}

internal readonly record struct ValidatorBuilderOptionsSnapshot(
    ValidationCascadeMode? CascadeMode,
    PropertyNamePolicy PropertyNamePolicy,
    string? PropertyNamePrefix,
    string? PropertyNameSuffix,
    string? ErrorMessagePrefix,
    string? ErrorMessageSuffix
)
{
    // Scan and Global wrappers are ambient: every validator inherits them, so a child running under Include must not
    // apply them again. Wrappers set through Configure belong to the validator and stay.
    internal ValidatorBuilderOptionsSnapshot WithLocalWrappersOnly(ValidatorBuilderOptions? local) => this with
    {
        PropertyNamePrefix = local?.PropertyNamePrefix,
        PropertyNameSuffix = local?.PropertyNameSuffix,
        ErrorMessagePrefix = local?.ErrorMessagePrefix,
        ErrorMessageSuffix = local?.ErrorMessageSuffix
    };

    /// <summary>
    /// How a message names another property, such as a comparison target: its path under the naming policy, without the name wrappers, which belong to the
    /// property the error is about.
    /// </summary>
    internal string? NameOf(string? path) => path is null ? null : PropertyNameComposer.ApplyPolicy(path, PropertyNamePolicy);

    // The most specific setting wins: this validator's Configure, then the AddValidators scan, then Global, which a
    // freeze creates when no one has yet.
    internal static ValidatorBuilderOptionsSnapshot Create(
        ValidatorBuilderOptions? local,
        ValidatorBuilderOptions? scan,
        ValidatorBuilderOptions global
    ) => new(
        local?.CascadeMode ?? scan?.CascadeMode ?? global.CascadeMode,
        local?.PropertyNamePolicy ?? scan?.PropertyNamePolicy ?? global.PropertyNamePolicy ?? PropertyNamePolicy.None,
        local?.PropertyNamePrefix ?? scan?.PropertyNamePrefix ?? global.PropertyNamePrefix,
        local?.PropertyNameSuffix ?? scan?.PropertyNameSuffix ?? global.PropertyNameSuffix,
        local?.ErrorMessagePrefix ?? scan?.ErrorMessagePrefix ?? global.ErrorMessagePrefix,
        local?.ErrorMessageSuffix ?? scan?.ErrorMessageSuffix ?? global.ErrorMessageSuffix);
}

/// <summary>
/// The settings a validator reads only when it freezes: the default messages by error code, the display-name resolver, and the clock, each from the most
/// specific level that sets them. Kept out of <see cref="ValidatorBuilderOptionsSnapshot" />, which async validation copies into its state machines.
/// </summary>
internal readonly struct ValidatorMessageOptions
{
    private readonly ValidatorBuilderOptions? _local;

    private readonly ValidatorBuilderOptions? _scan;

    private readonly ValidatorBuilderOptions? _global;

    private readonly Func<MemberInfo, string?>? _displayNameResolver;

    internal ValidatorMessageOptions(ValidatorBuilderOptions? local, ValidatorBuilderOptions? scan, ValidatorBuilderOptions? global)
    {
        _local = local;
        _scan = scan;
        _global = global;
        _displayNameResolver = local?.DisplayNameResolver ?? scan?.DisplayNameResolver ?? global?.DisplayNameResolver;
        TimeProvider = local?.TimeProvider ?? scan?.TimeProvider ?? global?.TimeProvider ?? TimeProvider.System;
    }

    /// <summary>The clock the date rules read, from the most specific level that sets one.</summary>
    internal TimeProvider TimeProvider { get; }

    internal bool TryGetMessage(string code, [NotNullWhen(true)] out string? message) =>
        TryGet(_local, code, out message) || TryGet(_scan, code, out message) || TryGet(_global, code, out message);

    /// <summary>The display name the resolver gives <paramref name="member" />, or <c>null</c> for none.</summary>
    internal string? DisplayNameOf(MemberInfo? member) => member is null ? null : _displayNameResolver?.Invoke(member);

    private static bool TryGet(ValidatorBuilderOptions? options, string code, [NotNullWhen(true)] out string? message)
    {
        message = null;
        return options?.ErrorMessageTable is { } table && table.TryGetValue(code, out message);
    }
}

internal readonly record struct PropertyValidatorBuilderOptionsSnapshot(
    bool IsCascadeModeSet,
    ValidationCascadeMode CascadeMode,
    string? PropertyName,
    string? DisplayName,
    ValidationErrorSeverity? ErrorSeverity,
    string? ErrorCode,
    string? ErrorMessage
)
{
    internal static PropertyValidatorBuilderOptionsSnapshot Create(PropertyValidatorBuilderOptions? options) => new(
        options?.IsCascadeModeSet == true,
        options?.CascadeMode ?? default,
        options?.PropertyName,
        options?.DisplayName,
        options?.ErrorSeverity,
        options?.ErrorCode,
        options?.ErrorMessage);
}

/// <summary>
/// Per-property options applied at the property declaration site. They are read when the callback that receives them returns, and are read-only
/// from then on.
/// </summary>
public sealed class PropertyValidatorBuilderOptions
{
    private bool _readOnly;

    internal bool IsCascadeModeSet { get; private set; }

    /// <summary>Cascade mode for this single property's rule chain.</summary>
    public ValidationCascadeMode CascadeMode
    {
        get;
        set
        {
            field = Writable(value);
            IsCascadeModeSet = true;
        }
    }

    /// <summary>Override for the reported property name, used as written: the naming policy does not apply to it. Defaults to the reflected name.</summary>
    public string? PropertyName
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>
    /// Name for this property in messages, used as written in place of the reported name. The error's <c>PropertyName</c> is unchanged. Wins over
    /// <see cref="ValidatorBuilderOptions.DisplayNameResolver" />.
    /// </summary>
    public string? DisplayName
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>Default severity for errors produced by this property's rules.</summary>
    public ValidationErrorSeverity? ErrorSeverity
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>Default error code for errors produced by this property's rules.</summary>
    public string? ErrorCode
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>Default error message for errors produced by this property's rules.</summary>
    public string? ErrorMessage
    {
        get;
        set => field = Writable(value);
    }

    internal void MakeReadOnly() => _readOnly = true;

    private T Writable<T>(T value) =>
        _readOnly ? throw new InvalidOperationException(ReadOnlyOptions.ReadWhenTheCallbackReturned) : value;
}

/// <summary>How a validator should behave after the first failure.</summary>
public enum ValidationCascadeMode
{
    /// <summary>Run every remaining rule even after a failure.</summary>
    Continue,
    /// <summary>Stop on the first <see cref="ValidationErrorSeverity.Error" />-level failure.</summary>
    Stop
}

/// <summary>Naming convention applied to reported property names.</summary>
public enum PropertyNamePolicy
{
    /// <summary>Use the property name as declared.</summary>
    None,
    /// <summary>e.g. <c>userName</c>.</summary>
    CamelCase,
    /// <summary>e.g. <c>UserName</c>.</summary>
    PascalCase,
    /// <summary>e.g. <c>user_name</c>.</summary>
    SnakeCase,
    /// <summary>e.g. <c>user-name</c>.</summary>
    KebabCase
}