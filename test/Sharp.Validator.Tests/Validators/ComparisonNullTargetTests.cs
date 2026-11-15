namespace Sharp.Validator.Tests.Validators;

public sealed class ComparisonNullTargetTests
{
    [Fact]
    public void Comparison_WithFuncReturningNull_IsSkipped()
    {
        // direct Add so we can pass a target func that returns null
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .Add(new LessThanValidator<Person, string?>(_ => (true, null), nameof(Person.Email))));

        var result = validator.Validate(new Person { Name = "test" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Comparison_WithFuncReturningValue_StillReportsErrorMessage()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).LessThan(person => person.Email));
        var result = validator.Validate(new Person { Name = "z", Email = "a" });
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }
}