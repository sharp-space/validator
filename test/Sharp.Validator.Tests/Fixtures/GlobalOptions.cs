namespace Sharp.Validator.Tests.Fixtures;

[CollectionDefinition(Name, DisableParallelization = true)]
// Tests that mutate ValidatorBuilderOptions.Global must not run in parallel with each other (and ideally not with
// anything else that depends on Global). xUnit collections are sequential.
public sealed class GlobalOptions
{
    public const string Name = nameof(GlobalOptions);
}