namespace Sharp.Validator.Tests.Pipeline;

// Regression: Stop reacted to entries of any severity, while Warning and Information don't fail IsValid. A warning
// therefore skipped a later Error rule and an invalid model came back valid. Non-blocking entries must never stop.
public sealed class CascadeSeverityTests
{
    [Theory]
    [InlineData(ValidationErrorSeverity.Warning)]
    [InlineData(ValidationErrorSeverity.Information)]
    public void NonErrorEntry_DoesNotStopThePropertyChain(ValidationErrorSeverity severity)
    {
        var validator = new Validator<Model>(x => x
            .Property(m => m.Name, o => o.CascadeMode = ValidationCascadeMode.Stop)
            .NotEmpty(o => o.ErrorSeverity = severity)
            .MinimumLength(3));

        var result = validator.Validate(new Model { Name = "" });
        Assert.Equal(2, result.Errors.Count);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(ValidationErrorSeverity.Warning)]
    [InlineData(ValidationErrorSeverity.Information)]
    public void NonErrorEntry_DoesNotStopLaterChains(ValidationErrorSeverity severity)
    {
        var validator = new Validator<Model>(x =>
        {
            x.Property(m => m.Name).NotEmpty(o => o.ErrorSeverity = severity);
            x.Property(m => m.Code).NotEmpty();
        });

        validator.Configure(o => o.CascadeMode = ValidationCascadeMode.Stop);
        var result = validator.Validate(new Model());
        Assert.Equal(["Name", "Code"], result.Errors.Select(x => x.PropertyName));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ErrorEntry_StillStopsThePropertyChain()
    {
        var validator = new Validator<Model>(x => x
            .Property(m => m.Name, o => o.CascadeMode = ValidationCascadeMode.Stop)
            .NotEmpty()
            .MinimumLength(3));

        Assert.Single(validator.Validate(new Model { Name = "" }).Errors);
    }

    [Fact]
    public void ErrorEntry_StillStopsLaterChains()
    {
        var validator = new Validator<Model>(x =>
        {
            x.Property(m => m.Name).NotEmpty();
            x.Property(m => m.Code).NotEmpty();
        });

        validator.Configure(o => o.CascadeMode = ValidationCascadeMode.Stop);
        Assert.Equal("Name", Assert.Single(validator.Validate(new Model()).Errors).PropertyName);
    }

    [Fact]
    public void Warning_OnOneElement_DoesNotStopTheLoop()
    {
        var validator = new Validator<Model>(x => x
            .Elements(m => m.Tags!, o => o.CascadeMode = ValidationCascadeMode.Stop)
            .NotEmpty(o => o.ErrorSeverity = ValidationErrorSeverity.Warning));

        Assert.Equal(2, validator.Validate(new Model { Tags = ["", ""] }).Errors.Count);
    }

    [Fact]
    public void Warning_OnAnElementProperty_DoesNotStopTheLoop()
    {
        var validator = new Validator<Model>(x => x
            .Elements(m => m.Items!)
            .Property(i => i.Label, o => o.CascadeMode = ValidationCascadeMode.Stop)
            .NotEmpty(o => o.ErrorSeverity = ValidationErrorSeverity.Warning));

        var result = validator.Validate(new Model { Items = [new Item(), new Item()] });
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public async Task Warning_InTheSyncPhase_DoesNotSkipTheChainAsyncRules()
    {
        var validator = new Validator<Model>(x => x
            .Property(m => m.Name, o => o.CascadeMode = ValidationCascadeMode.Stop)
            .NotEmpty(o => o.ErrorSeverity = ValidationErrorSeverity.Warning)
            .MustAsync(_ => Task.FromResult(false)));

        var result = await validator.ValidateAsync(new Model { Name = "" });
        Assert.Equal(2, result.Errors.Count);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Error_InTheSyncPhase_SkipsTheChainAsyncRules()
    {
        var validator = new Validator<Model>(x => x
            .Property(m => m.Name, o => o.CascadeMode = ValidationCascadeMode.Stop)
            .NotEmpty()
            .MustAsync(_ => Task.FromResult(false)));

        Assert.Single((await validator.ValidateAsync(new Model { Name = "" })).Errors);
    }

    [Fact]
    // Without Stop a chain goes on past a failure, into its async rules as well, so the sync phase records nothing.
    public async Task Error_InTheSyncPhase_WithoutStop_StillRunsTheChainAsyncRules()
    {
        var validator = new Validator<Model>(x => x
            .Property(m => m.Name)
            .NotEmpty()
            .MustAsync(_ => Task.FromResult(false)));

        Assert.Equal(2, (await validator.ValidateAsync(new Model { Name = "" })).Errors.Count);
    }

    [Fact]
    // The sync phase records each failed chain on its own, so when two fail, neither runs its async rules.
    public async Task Errors_InTheSyncPhaseOfTwoChains_SkipEachChainAsyncRules()
    {
        var validator = new Validator<Model>(x =>
        {
            x.Property(m => m.Name, o => o.CascadeMode = ValidationCascadeMode.Stop)
                .NotEmpty()
                .MustAsync(_ => Task.FromResult(false));

            x.Property(m => m.Code, o => o.CascadeMode = ValidationCascadeMode.Stop)
                .NotEmpty()
                .MustAsync(_ => Task.FromResult(false));
        });

        var result = await validator.ValidateAsync(new Model { Name = "", Code = "" });
        Assert.Equal(["Name", "Code"], result.Errors.Select(error => error.PropertyName));
    }

    private sealed class Item
    {
        public string? Label { get; init; }
    }

    private sealed class Model
    {
        public string? Name { get; init; }

        public string? Code { get; init; }

        public List<string>? Tags { get; init; }

        public List<Item>? Items { get; init; }
    }
}