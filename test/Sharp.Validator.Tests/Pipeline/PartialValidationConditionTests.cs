namespace Sharp.Validator.Tests.Pipeline;

// Regression: conditions ran before the partial-validation filter, so a step of a multi-step form threw on a section
// the caller had excluded. An excluded chain must behave as if it were not registered.
public sealed class PartialValidationConditionTests
{
    [Fact]
    public void ExcludedChain_DoesNotRunItsSyncCondition()
    {
        var validator = new Validator<Model>(x =>
        {
            x.Property(m => m.Email).NotEmpty();
            x.Property(m => m.Name).Where(m => m.Section!.Country == "US").NotEmpty();
        });

        Assert.True(validator.Validate(new Model { Email = "a" }, c => c.IncludeProperty(m => m.Email)).IsValid);
    }

    [Fact]
    public async Task ExcludedAsyncChain_DoesNotRunItsSyncCondition()
    {
        var validator = new Validator<Model>(x =>
        {
            x.Property(m => m.Email).NotEmpty();
            x.Property(m => m.Name).Where(m => m.Section!.Country == "US").MustAsync(_ => Task.FromResult(false));
        });

        var result = await validator.ValidateAsync(new Model { Email = "a" }, c => c.IncludeProperty(m => m.Email));
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ExcludedChain_DoesNotRunItsAsyncCondition()
    {
        var validator = new Validator<Model>(x =>
        {
            x.Property(m => m.Email).NotEmpty();
            x.Property(m => m.Name).Where(m => Task.FromResult(m.Section!.Country == "US")).NotEmpty();
        });

        var result = await validator.ValidateAsync(new Model { Email = "a" }, c => c.IncludeProperty(m => m.Email));
        Assert.True(result.IsValid);
    }

    private sealed class Section
    {
        public string? Country { get; init; }
    }

    private sealed class Model
    {
        public string? Email { get; init; }

        public string? Name { get; init; }

        public Section? Section { get; init; }
    }
}