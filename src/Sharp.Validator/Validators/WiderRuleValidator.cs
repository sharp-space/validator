namespace Sharp.Validator.Validators;

// A chain stores its rules as PropertyValidator<TModel, TProperty> and calls them through that type, never the variant
// interface, whose casts go through the runtime's cast cache. A rule for a wider type, such as an object? rule on a
// string chain, is not one, so it runs behind this adapter. The rule itself took the chain's options and the flag that
// keeps it to one chain, and freezes them under its own type's name; the adapter reports what it froze. An include is
// never wider: Include makes it for the chain's own type.
internal sealed class WiderRuleValidator<TModel, TProperty>(IPropertyValidator<TModel, TProperty> rule)
    : PropertyValidator<TModel, TProperty>
{
    internal override void FreezeOptions(in ValidatorBuilderOptionsSnapshot builderOptions, in ValidatorMessageOptions messages)
    {
        rule.Rule.FreezeOptions(builderOptions, messages);
        Options = rule.Rule.Options;
    }

    public override bool Validate(TProperty value, ValidationContext<TModel> context) => rule.ValidateValue(value, context);
}

// The async form of WiderRuleValidator.
internal sealed class AsyncWiderRuleValidator<TModel, TProperty>(IAsyncPropertyValidator<TModel, TProperty> rule)
    : AsyncPropertyValidator<TModel, TProperty>
{
    internal override void FreezeOptions(in ValidatorBuilderOptionsSnapshot builderOptions, in ValidatorMessageOptions messages)
    {
        rule.Rule.FreezeOptions(builderOptions, messages);
        Options = rule.Rule.Options;
    }

    public override Task<bool> ValidateAsync(TProperty value, ValidationContext<TModel> context, CancellationToken cancellationToken) =>
        rule.ValidateValueAsync(value, context, cancellationToken);
}