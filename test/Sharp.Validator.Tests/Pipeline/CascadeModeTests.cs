namespace Sharp.Validator.Tests.Pipeline;

public sealed class CascadeModeTests
{
    [Fact]
    public void DefaultCascade_ContinuesOnFailure()
    {
        var validator = new Validator<Person>(builder =>
        {
            builder.Property(x => x.Name).NotEmpty();
            builder.Property(x => x.Age).GreaterThan(0);
        });

        var result = validator.Validate(new Person { Name = null, Age = 0 });
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void CascadeModeStop_AtBuilderLevel_StopsAtFirstFailedProperty()
    {
        var validator = new Validator<Person>(builder =>
        {
            builder.Property(x => x.Name).NotEmpty();
            builder.Property(x => x.Age).GreaterThan(0);
        });

        validator.Configure(x => x.CascadeMode = ValidationCascadeMode.Stop);

        var result = validator.Validate(new Person { Name = null, Age = 0 });
        var item = Assert.Single(result.Errors);
        Assert.Equal("Name", item.PropertyName);
    }

    [Fact]
    public void CascadeModeStop_AtPropertyLevel_StopsAtFirstFailedRule()
    {
        var validator = new Validator<Person>(builder => builder.Property(
                x => x.Name,
                x => x.CascadeMode = ValidationCascadeMode.Stop)
            .NotEmpty()
            .MinimumLength(5));

        var result = validator.Validate(new Person { Name = null });
        Assert.Single(result.Errors); // only NotEmpty fires, MinimumLength is skipped
    }

    [Fact]
    public void ElementsCascadeStop_HaltsAfterFirstFailingElement()
    {
        var validator = new Validator<Person>(x => x.Elements(person => person.Tags!).NotEmpty().MinimumLength(3));
        validator.Configure(x => x.CascadeMode = ValidationCascadeMode.Stop);

        // Without Stop: "" fails NotEmpty + MinimumLength and "x" fails MinimumLength = 3 errors.
        var result = validator.Validate(new Person { Tags = ["", "x", "abc"] });
        var item = Assert.Single(result.Errors);
        Assert.Equal("Tags[0]", item.PropertyName);
    }

    [Fact]
    public async Task AsyncElementsCascadeStop_HaltsAfterFirstFailingElement()
    {
        var validator = new Validator<Person>(builder => builder.Elements(x => x.Tags!)
            .MustAsync(x => Task.FromResult(!string.IsNullOrEmpty(x)))
            .MustAsync(x => Task.FromResult(x is { Length: >= 3 })));

        validator.Configure(x => x.CascadeMode = ValidationCascadeMode.Stop);

        var result = await validator.ValidateAsync(new Person { Tags = ["", "x", "abc"] });
        var item = Assert.Single(result.Errors);
        Assert.Equal("Tags[0]", item.PropertyName);
    }
}