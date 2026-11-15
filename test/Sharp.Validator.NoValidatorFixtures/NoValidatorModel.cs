namespace Sharp.Validator.NoValidatorFixtures;

/// <summary>
/// The only type of an assembly built with the source generator that declares no validator. The AOT canary scans this assembly and asserts that it
/// registers nothing instead of throwing the no-registrar guard.
/// </summary>
public sealed class NoValidatorModel
{
    /// <summary>Any value.</summary>
    // ReSharper disable once UnusedMember.Global — the model only needs a shape; no validator reads it.
    public string? Name { get; init; }
}