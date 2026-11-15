namespace Sharp.Validator.Tests.Pipeline;

public sealed class IncludePropertyTests
{
    [Fact]
    public void IncludeProperty_RunsOnlyTheSpecifiedProperty()
    {
        var validator = new Validator<Person>(builder =>
        {
            builder.Property(x => x.Name).NotEmpty();
            builder.Property(x => x.Email).NotEmpty();
        });

        // Only validate Email
        var result = validator.Validate(new Person { Name = null, Email = null },
            x => x.IncludeProperty(person => person.Email));

        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);
        Assert.Equal("Email", item.PropertyName);
    }

    [Fact]
    public void IncludeProperty_MultipleSelectors_RunOnlyThoseProperties()
    {
        var validator = new Validator<Person>(builder =>
        {
            builder.Property(x => x.Name).NotEmpty();
            builder.Property(x => x.Email).NotEmpty();
            builder.Property(x => x.Age).GreaterThan(0);
        });

        var result = validator.Validate(new Person { Name = null, Email = null, Age = 0 },
            context =>
            {
                context.IncludeProperty(x => x.Email);
                context.IncludeProperty(x => x.Age);
            });

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.DoesNotContain(result.Errors, x => x.PropertyName == "Name");
    }

    [Fact]
    // A selection matches chains by path, and a selector through an indexer ended its path at the last member,
    // so IncludeProperty(x => x.Squads[0].Name) selected the top-level Name chain. It is rejected as a chain's
    // own selector is.
    public void IncludeProperty_ThroughAnIndexer_Throws()
    {
        var validator = new Validator<Team>(builder => builder.Property(x => x.Name).NotEmpty());
        var exception = Assert.Throws<InvalidOperationException>(() =>
            validator.Validate(new Team(), context => context.IncludeProperty(x => x.Squads![0].Name)));

        Assert.Contains("reads through an indexer", exception.Message);
    }

    private sealed class Team
    {
        public string? Name { get; init; }

        public List<Team>? Squads { get; init; }
    }
}