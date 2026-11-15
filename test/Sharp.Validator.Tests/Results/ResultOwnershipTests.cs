namespace Sharp.Validator.Tests.Results;

// ToResult hands the error list's ownership to the returned result: re-validating with the same context must neither
// mutate a previously returned result nor accumulate errors across runs, and run-scoped rule-chain failure bookkeeping
// resets between runs.
public sealed class ResultOwnershipTests
{
    [Fact]
    public void RevalidatingTheSameContext_NeitherMutatesNorAccumulates()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        var context = new ValidationContext<Person>(new Person { Name = "" });

        var first = validator.Validate(context);
        Assert.Single(first.Errors);

        var second = validator.Validate(context);

        Assert.Single(first.Errors); // prior result untouched
        Assert.Single(second.Errors); // fresh run, not 2 accumulated errors
        Assert.NotSame(first.Errors, second.Errors);
    }

    [Fact]
    // The validator hands its own list to the result without a copy, and the result never lets it out: a cast of Errors
    // used to reach that list, and clearing it turned the result valid.
    public void ValidatorResult_CannotBeChangedThroughErrors()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        var result = validator.Validate(new Person { Name = "" });
        Assert.IsNotType<List<ValidationError>>(result.Errors, false);
        Assert.Throws<NotSupportedException>(() => ((IList<ValidationError>)result.Errors).Clear());
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ReusedContext_RerunsAsyncChains()
    {
        var attempts = 0;
        var validator = new Validator<Person>(x => x
            .Property(person => person.Name, options => options.CascadeMode = ValidationCascadeMode.Stop)
            .MustAsync(async _ =>
            {
                attempts++;
                await Task.Yield();

                return false;
            }));

        var person = new Person { Name = "x" };
        var context = new ValidationContext<Person>(person);
        Assert.False((await validator.ValidateAsync(context)).IsValid);

        // Without the run-scoped reset, the first failure's bookkeeping would make the second run skip the chain
        // entirely and report the model as valid.
        Assert.False((await validator.ValidateAsync(context)).IsValid);
        Assert.Equal(2, attempts);
    }
}