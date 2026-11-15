namespace Sharp.Validator.Tests.Validators;

// ExactLengthValidator(Func) passes the same delegate as both bounds; the base validator must invoke it once per
// validation, not twice — both for cost and so a side-effecting or non-deterministic bound can't disagree with itself
// between the min and max reads.
public sealed class ExactLengthSingleInvocationTests
{
    [Fact]
    public void ExactLengthFunc_IsInvokedOncePerValidation()
    {
        var calls = 0;
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Length(_ =>
        {
            calls++;
            return 4;
        }));

        Assert.True(validator.Validate(new Person { Name = "abcd" }).IsValid);
        Assert.Equal(1, calls);
        Assert.False(validator.Validate(new Person { Name = "abc" }).IsValid);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void DistinctMinMaxFuncs_AreStillInvokedSeparately()
    {
        var minCalls = 0;
        var maxCalls = 0;
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Length(
            _ =>
            {
                minCalls++;
                return 2;
            },
            _ =>
            {
                maxCalls++;
                return 6;
            }));

        Assert.True(validator.Validate(new Person { Name = "abcd" }).IsValid);
        Assert.Equal(1, minCalls);
        Assert.Equal(1, maxCalls);
    }
}