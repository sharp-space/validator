using System.ComponentModel;

namespace Sharp.Validator;

/// <summary>Base of all property validators. Holds the per-validator options such as error message.</summary>
public abstract class PropertyValidator
{
    private const string PropertyNamePlaceholder = "{PropertyName}";
    private const string DefaultMessage = "'{PropertyName}' is not valid.";

    // Read by JoinedMessageTests to check that the split waits for the first failure.
    internal string[]? SplitMessageParts;

    // Set once Configure has handed the options to a subclass's callback, which may keep them: they become read-only
    // when a chain takes the rule, and the freeze works on a copy, so the subclass's reference never shows the frozen
    // template. The library's rules set their defaults through SetDefaults, which hands them to the library's own
    // callback only, so theirs freeze in place.
    private bool _optionsHandedOut;

    // Set by SetDefaults, which only the library's rules call.
    private bool _setsOwnDefaults;
    private bool _skipMessageParameters;
    private bool _templateTakesParameters;

    // A rule derives from PropertyValidator<TModel, TProperty> or AsyncPropertyValidator<TModel, TProperty>: a chain
    // takes only those, so a rule that derived from this base directly could be added to none.
    private protected PropertyValidator()
    {
    }

    internal PropertyValidatorOptions? Options { get; set; }

    // Set when a chain takes the rule: its options and settled names belong to that chain, so no second chain may.
    internal bool IsAttached { get; set; }

    /// <summary>
    /// The frozen message split at <c>{PropertyName}</c> when that is its only placeholder, otherwise empty. A failure joins the parts with the name, which
    /// costs a third of scanning the template for placeholders. The first failure splits it rather than the freeze: a Scoped validator freezes in every
    /// scope, where most of its rules pass.
    /// </summary>
    internal string[] MessageParts => SplitMessageParts ??=
        Options!.ErrorMessage is { } frozen && !HasParameterPlaceholder(frozen)
            ? frozen.Split(PropertyNamePlaceholder)
            : [];

    /// <summary>
    /// Whether the library wrote this rule: its rules set their own code and message, and an Include reports its child's errors, so only a rule written for
    /// an app gets the defaults that name it.
    /// </summary>
    private protected virtual bool IsLibraryRule => _setsOwnDefaults;

    // A frozen template with no placeholder besides {PropertyName} can't read a parameter, so a failing rule skips
    // building them. A message set on the context at runtime can read anything, and outside the pipeline nothing is
    // frozen, so both still get every parameter.
    private protected bool NeedsMessageParameters<TModel>(ValidationContext<TModel> context) =>
        context.ErrorMessage != null || !_skipMessageParameters;

    /// <summary>Lets a validator bake placeholders whose value is fixed for its lifetime into the frozen template.</summary>
    private protected virtual string? BakeMessage(string? message) => message;

    /// <summary>
    /// Lets a validator settle, before its template is baked, what it reads from the options of the validator that freezes it, which is the only one, since
    /// a rule belongs to one chain: the names it reports for other properties, under that validator's naming policy, and the clock a date rule reads.
    /// </summary>
    private protected virtual void FreezeSettings(in ValidatorBuilderOptionsSnapshot builderOptions, in ValidatorMessageOptions messages)
    {
    }

    /// <summary>A reported name with no brace can be baked into a template; one with a brace stays a runtime value.</summary>
    private protected static string BakeName(string message, string placeholder, string? name) =>
        name is null || name.Contains('{') ? message : message.Replace(placeholder, name, StringComparison.Ordinal);

    /// <summary>
    /// A target or bound that isn't another property has no name, so its name placeholder reads empty: every form of a rule fills the placeholders
    /// of its code.
    /// </summary>
    private protected static string BakeNameless(string message, string placeholder, string? path) =>
        path is null ? message.Replace(placeholder, string.Empty, StringComparison.Ordinal) : message;

    internal virtual void FreezeOptions(in ValidatorBuilderOptionsSnapshot builderOptions, in ValidatorMessageOptions messages)
    {
        FreezeSettings(builderOptions, messages);
        if (_optionsHandedOut) Options = Options!.Clone();
        if (!IsLibraryRule) FillMissingDefaults();

        if (Options != null)
        {
            // A default message gives way to the app's message for its code; one set on the rule or its chain stays.
            if (!Options.IsMessageOverridden && Options.ErrorCode is { } code && messages.TryGetMessage(code, out var message))
                Options.ErrorMessage = message;

            Options.ErrorMessage = BakeMessage(Options.ErrorMessage);
        }

        // The root wrappers cover the Include run's, which only ever drop the inherited ones, so judging the template
        // with them holds for both kinds of run.
        _templateTakesParameters |=
            HasParameterPlaceholder(Options?.ErrorMessage) ||
            HasParameterPlaceholder(builderOptions.ErrorMessagePrefix) ||
            HasParameterPlaceholder(builderOptions.ErrorMessageSuffix);

        // Only the library's own rules call NeedsMessageParameters, and they are sealed, so no subclass can set a
        // runtime message after one of them has decided.
        _skipMessageParameters = !_templateTakesParameters;
    }

    // A rule written for an app that names no code or message still reports an error a client can branch on and
    // read: its code is its type's name, which ErrorMessages can key, and its message names the property. It runs
    // on the frozen copy, after the chain merged its options, so a code or message from the chain or the rule's
    // own Configure stays.
    private void FillMissingDefaults()
    {
        var options = Options ??= new();
        options.ErrorCode ??= CodeOf(GetType());
        options.ErrorMessage ??= DefaultMessage;
    }

    // The name the type is declared with. A file-local type's runtime name is `<File>F<hash>__Name`, the hash taken
    // over the source file's path, so a code taken from it would differ between machines that build from different
    // folders. The file part has no '>' and the hash no '_', so the name starts after the first "__" past the '>'; a
    // name with no '>', which only a renaming tool makes, is searched from its start rather than failing the freeze. A
    // generic rule's runtime name also carries an arity suffix.
    private static string CodeOf(Type type)
    {
        var name = type.Name;
        if (name.StartsWith('<'))
            name = name[(name.IndexOf("__", name.IndexOf('>') + 1, StringComparison.Ordinal) + 2)..];

        var arity = name.IndexOf('`', StringComparison.Ordinal);
        return arity < 0 ? name : name[..arity];
    }

    internal static bool HasParameterPlaceholder(ReadOnlySpan<char> template)
    {
        while (true)
        {
            var open = template.IndexOf('{');
            if (open < 0) return false;

            template = template[open..];
            if (!template.StartsWith(PropertyNamePlaceholder, StringComparison.Ordinal)) return true;

            template = template[PropertyNamePlaceholder.Length..];
        }
    }

    /// <summary>
    /// Sets or merges this validator's per-instance options. A chain reads them when it takes the rule, and they are read-only from then on, so a change
    /// through a reference kept from the callback throws.
    /// </summary>
    protected void Configure(Action<PropertyValidatorOptions> configure)
    {
        // The chain read the options when it took the rule, and froze a copy of them later, so nothing set now is seen.
        if (IsAttached) throw new InvalidOperationException(ReadOnlyOptions.ReadWhenAChainTookTheRule);

        _optionsHandedOut = true;
        configure(Options ??= new());
    }

    internal void LockHandedOutOptions()
    {
        if (_optionsHandedOut) Options!.MakeReadOnly(ReadOnlyOptions.ReadWhenAChainTookTheRule);
    }

    // The library's rules set their default code and message through this rather than Configure, so a rule that no
    // caller configured freezes its options in place instead of copying them.
    private protected void SetDefaults(Action<PropertyValidatorOptions> configure)
    {
        _setsOwnDefaults = true;
        configure(Options ??= new());
    }
}

/// <summary>
/// The type <c>Add</c> takes a rule as, so that a chain also takes a rule for a wider type, such as a <c>string?</c> rule on a <c>string</c> chain.
/// Derive a rule from <see cref="PropertyValidator{TModel, TProperty}" />; this interface has nothing to implement or call.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
// Public only because Add's signature names it, and an interface because only an interface can be contravariant. Its
// members are internal, so only the library's base classes implement it, and every rule a chain takes carries options
// and the flag that keeps it to one chain.
public interface IPropertyValidator<TModel, in TProperty>
{
    internal PropertyValidator Rule { get; }

    // What a wider rule's adapter calls; a chain runs its rules through the base class instead.
    internal bool ValidateValue(TProperty value, ValidationContext<TModel> context);
}

/// <summary>
/// The type <c>Add</c> takes an async rule as, so that a chain also takes a rule for a wider type. Derive a rule from
/// <see cref="AsyncPropertyValidator{TModel, TProperty}" />; this interface has nothing to implement or call.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
// Public and internal-membered for the reasons IPropertyValidator gives.
public interface IAsyncPropertyValidator<TModel, in TProperty>
{
    internal PropertyValidator Rule { get; }

    // What a wider rule's adapter calls; a chain runs its rules through the base class instead.
    internal Task<bool> ValidateValueAsync(TProperty value, ValidationContext<TModel> context, CancellationToken cancellationToken);
}

/// <summary>Base for synchronous validators that check a single <typeparamref name="TProperty" /> value.</summary>
public abstract class PropertyValidator<TModel, TProperty> : PropertyValidator, IPropertyValidator<TModel, TProperty>
{
    PropertyValidator IPropertyValidator<TModel, TProperty>.Rule => this;

    bool IPropertyValidator<TModel, TProperty>.ValidateValue(TProperty value, ValidationContext<TModel> context) =>
        Validate(value, context);

    /// <summary>Returns <c>true</c> if the value is valid, otherwise <c>false</c>.</summary>
    public abstract bool Validate(TProperty value, ValidationContext<TModel> context);
}

/// <summary>Base for asynchronous validators that check a single <typeparamref name="TProperty" /> value.</summary>
public abstract class AsyncPropertyValidator<TModel, TProperty> : PropertyValidator, IAsyncPropertyValidator<TModel, TProperty>
{
    PropertyValidator IAsyncPropertyValidator<TModel, TProperty>.Rule => this;

    Task<bool> IAsyncPropertyValidator<TModel, TProperty>.ValidateValueAsync(
        TProperty value,
        ValidationContext<TModel> context,
        CancellationToken cancellationToken
    ) => ValidateAsync(value, context, cancellationToken);

    /// <summary>Returns <c>true</c> if the value is valid, otherwise <c>false</c>.</summary>
    // Task<bool> (not ValueTask<bool>) by design: the runtime returns cached completed Task<bool> singletons for
    // true/false, so a synchronously-completing async validator already allocates nothing here. ValueTask<bool> would
    // recover no allocation, is marginally slower, and — being a public extension point — would be a breaking change.
    // ValueTask earns its keep only for a non-cacheable result (e.g. ValidationResult), which is why
    // Validator.ValidateAsync returns ValueTask<ValidationResult> with a synchronous fast-path and nothing else does.
    public abstract Task<bool> ValidateAsync(
        TProperty value,
        ValidationContext<TModel> context,
        CancellationToken cancellationToken
    );
}