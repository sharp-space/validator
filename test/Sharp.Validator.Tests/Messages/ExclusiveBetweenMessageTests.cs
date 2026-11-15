namespace Sharp.Validator.Tests.Messages;

// Regression: ExclusiveBetween validator passes when from < value < to. The error message historically read
// "must not be between ...", which contradicted the validator's intent.
public sealed class ExclusiveBetweenMessageTests
{
    [Fact]
    public void Message_DescribesRequiredExclusiveRange()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).ExclusiveBetween(1, 10));
        var result = validator.Validate(new Person { Age = 1 });
        Assert.False(result.IsValid);

        var message = result.Errors[0].Message!;
        Assert.Contains("between 1 and 10", message);
        Assert.Contains("exclusive", message);
        Assert.DoesNotContain("must not be between", message);
    }

    [Fact]
    public void Message_ForUpperBoundViolation_AlsoUsesExclusiveWording()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).ExclusiveBetween(1, 10));
        var result = validator.Validate(new Person { Age = 10 });
        Assert.False(result.IsValid);
        Assert.Contains("exclusive", result.Errors[0].Message!);
    }
}