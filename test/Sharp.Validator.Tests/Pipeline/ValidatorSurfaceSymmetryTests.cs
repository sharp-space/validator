namespace Sharp.Validator.Tests.Pipeline;

// The block Where lived on Validator<T>, so only a subclass could gate a group of rules, and IValidator<T> took an
// existing context on the async entry point alone. Both ways of writing a validator and both entry points now match.
public sealed class ValidatorSurfaceSymmetryTests
{
    [Fact]
    public void InlineValidator_GatesAGroupOfRules()
    {
        var validator = new Validator<Person>(x => x.Where(person => person.Age >= 18, () =>
        {
            x.Property(person => person.Name).NotEmpty();
            x.Property(person => person.Email).NotEmpty();
        }));

        Assert.True(validator.Validate(new Person { Age = 10 }).IsValid);
        Assert.Equal(2, validator.Validate(new Person { Age = 20 }).Errors.Count);
    }

    [Fact]
    public void Interface_ValidatesAnExistingContextSynchronously()
    {
        IValidator<Person> validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        var result = validator.Validate(new ValidationContext<Person>(new Person()));
        Assert.Equal("Name", Assert.Single(result.Errors).PropertyName);
    }
}