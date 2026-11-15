namespace Sharp.Validator.NoGeneratorFixtures;

/// <summary>Model for the no-registrar negative canary.</summary>
public sealed class NoRegistrarModel
{
    /// <summary>Any value.</summary>
    public string? Name { get; init; }
}

/// <summary>
/// A perfectly ordinary validator in an assembly built without the source generator. The AOT canary asserts that scanning this assembly throws (the
/// guard path) instead of silently registering nothing.
/// </summary>
public sealed class NoRegistrarValidator : Validator<NoRegistrarModel>
{
    /// <summary>Creates the validator.</summary>
    public NoRegistrarValidator() : base(x => x.Property(model => model.Name).NotEmpty())
    {
    }
}