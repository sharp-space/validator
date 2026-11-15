namespace Sharp.Validator.Result.Tests.Errors;

// A rule with a fixed message reports one shared ValidationError, while a rule whose message reads the value builds a
// new one each time. Compared by reference, the same failure counted once or three times under Distinct depending on
// the message, so errors compare by what they say rather than by which object says it.
public sealed class ValidationErrorEqualityTests
{
    [Fact]
    public void ErrorsThatSayTheSameThing_AreEqual()
    {
        var first = new ValidationError("Name", "required", "NotEmpty", ValidationErrorSeverity.Warning);
        var second = new ValidationError("Name", "required", "NotEmpty", ValidationErrorSeverity.Warning);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Theory]
    [InlineData("name", "required", "NotEmpty", ValidationErrorSeverity.Warning)]
    [InlineData("Name", "Required", "NotEmpty", ValidationErrorSeverity.Warning)]
    [InlineData("Name", null, "NotEmpty", ValidationErrorSeverity.Warning)]
    [InlineData("Name", "required", "NotNull", ValidationErrorSeverity.Warning)]
    [InlineData("Name", "required", "NotEmpty", ValidationErrorSeverity.Error)]
    public void ErrorsThatDiffer_InAnyPart_AreNotEqual(
        string propertyName,
        string? message,
        string? code,
        ValidationErrorSeverity severity
    )
    {
        var error = new ValidationError("Name", "required", "NotEmpty", ValidationErrorSeverity.Warning);
        var other = new ValidationError(propertyName, message, code, severity);
        Assert.NotEqual(error, other);
        Assert.True(error != other);
    }

    [Fact]
    public void Error_IsNotEqualToNull()
    {
        var error = new ValidationError("Name");
        Assert.False(error.Equals(null));
        // The operators themselves: Assert.NotNull, which xUnit2024 suggests here, would run neither == nor !=.
#pragma warning disable xUnit2024
        Assert.False(error == null);
        Assert.True(null != error);
#pragma warning restore xUnit2024
    }

    [Fact]
    public void ObjectEquality_SameValues_AreEqual()
    {
        object first = new ValidationError("Name", "required", "Required");
        Assert.True(first.Equals(new ValidationError("Name", "required", "Required")));
    }

    [Fact]
    public void ObjectEquality_OtherTypeOrNull_IsNotEqual()
    {
        object first = new ValidationError("Name", "required", "Required");
        Assert.False(first.Equals("Name"));
        Assert.False(first.Equals(null));
    }
}