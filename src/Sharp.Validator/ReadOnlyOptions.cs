namespace Sharp.Validator;

// An options object becomes read-only once the library has read it, so a change made afterward throws rather than
// go unseen. Options a configure callback receives are read when it returns; ValidatorBuilderOptions, which a
// validator can be configured with more than once and which validators share through a scan or Global, are read
// when a validator freezes.
internal static class ReadOnlyOptions
{
    internal const string ReadWhenTheCallbackReturned =
        "These options were read when the callback that received them returned, so a change made now would go unseen. " +
        "Set them inside the callback.";

    internal const string ReadWhenAChainTookTheRule =
        "These options were read when a chain took the rule, so a change made now would go unseen. Configure the rule " +
        "in its constructor, before it is added to a chain.";

    internal const string ReadWhenAValidatorFroze =
        "These options were read when a validator froze, so a change made now would go unseen. Set them before the " +
        "first validation, or assign new options to ValidatorBuilderOptions.Global to change the app-wide defaults.";
}