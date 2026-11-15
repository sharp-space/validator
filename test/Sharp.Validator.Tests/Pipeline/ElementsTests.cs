namespace Sharp.Validator.Tests.Pipeline;

public sealed class ElementsTests
{
    [Fact]
    public void Elements_ValidatesEachWithIndexedPropertyName()
    {
        var validator = new Validator<Person>(x => x.Elements(person => person.Tags!).NotEmpty());
        var result = validator.Validate(new Person { Tags = ["ok", "", "ok"] });
        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);
        Assert.Equal("Tags[1]", item.PropertyName);
    }

    [Fact]
    public void ElementsNullCollection_Passes()
    {
        var validator = new Validator<Person>(x => x.Elements(person => person.Tags!).NotEmpty());
        Assert.True(validator.Validate(new Person { Tags = null }).IsValid);
    }

    [Fact]
    public void ElementsEmptyCollection_Passes()
    {
        var validator = new Validator<Person>(x => x.Elements(person => person.Tags!).NotEmpty());
        Assert.True(validator.Validate(new Person { Tags = [] }).IsValid);
    }

    [Fact]
    public void ElementsMultipleValidators_EachRunsAgainstAllItems()
    {
        var validator = new Validator<Person>(builder => builder.Elements(x => x.Tags!)
            .NotEmpty()
            .MinimumLength(3));

        var result = validator.Validate(new Person { Tags = ["", "xx", "abc"] });
        Assert.False(result.IsValid);

        // "" fails NotEmpty + MinimumLength
        // "xx" fails MinimumLength
        // "abc" passes both
        Assert.Equal(3, result.Errors.Count);
        Assert.Equal(2, result.Errors.Count(x => x.PropertyName == "Tags[0]"));
        Assert.Single(result.Errors, x => x.PropertyName == "Tags[1]");
    }

    [Fact]
    public void Include_OnElements_MergesChildErrorsWithoutOwnError()
    {
        var validator = new Validator<Team>(x => x.Elements(team => team.Members!).Include(new MemberValidator()));
        var result = validator.Validate(new Team { Members = [new Member { Name = "" }, new Member { Name = "ok" }] });
        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);
        Assert.Equal("Members[0].Name", item.PropertyName);
    }

    [Fact]
    public async Task AsyncInclude_OnElements_MergesChildErrors()
    {
        var validator = new Validator<Team>(x => x.Elements(team => team.Members!).Include(new AsyncMemberValidator()));
        var result = await validator.ValidateAsync(new Team { Members = [new Member { Name = "" }, new Member { Name = "ok" }] });
        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);
        Assert.Equal("Members[0].Name", item.PropertyName);
    }

    [Fact]
    public void Configure_PropagatesBuilderOptionsToElementsIncludeValidators()
    {
        var validator = new Validator<Team>(x => x.Elements(team => team.Members!).Include(new MemberValidator()));

        // Forces Configure's ElementsEvaluator branch to back-fill the include validator's options.
        validator.Configure(x => x.PropertyNamePrefix = "Team_");

        var result = validator.Validate(new Team { Members = [new Member { Name = "" }] });
        Assert.False(result.IsValid);
        Assert.StartsWith("Team_", result.Errors[0].PropertyName);
    }

    private sealed class Member
    {
        public string? Name { get; init; }
    }

    private sealed class Team
    {
        public List<Member>? Members { get; init; }
    }

    private sealed class MemberValidator() : Validator<Member>(x => x.Property(member => member.Name).NotEmpty());

    private sealed class AsyncMemberValidator() : Validator<Member>(builder => builder
        .Property(x => x.Name)
        .MustAsync(x => Task.FromResult(!string.IsNullOrEmpty(x))));
}