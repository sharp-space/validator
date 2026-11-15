namespace Sharp.Validator.Tests.Fixtures;

// The date rules read "now" from the validator's TimeProvider, so a test sets its clock there.
internal static class ClockedValidators
{
    internal static Validator<TModel> Clocked<TModel>(TimeProvider clock, Action<ValidatorBuilder<TModel>> rules)
    {
        var validator = new Validator<TModel>(rules);
        validator.Configure(options => options.TimeProvider = clock);
        return validator;
    }
}