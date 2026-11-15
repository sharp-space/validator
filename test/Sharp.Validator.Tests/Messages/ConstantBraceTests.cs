namespace Sharp.Validator.Tests.Messages;

// Regression: Equal and the range rules wrote a constant into their message template, so a constant with a brace read
// as a placeholder: Equal("{PropertyName}") reported the property's own name, and a bound of "{PropertyValue}" printed
// the value being checked. A constant is baked into the template only when it has no brace; otherwise it goes in as a
// parameter, which formatting never rescans. The range rules expose their bounds as {From} and {To} that way too.
public sealed class ConstantBraceTests
{
    [Fact]
    public void Equal_PrintsAConstantWithABraceAsWritten()
    {
        var validator = new Validator<Model>(x => x.Property(model => model.S).Equal("{PropertyName}"));
        Assert.Equal("'S' must be equal to '{PropertyName}'.", validator.Validate(new Model { S = "x" }).Errors[0].Message);
    }

    [Fact]
    public void NotEqual_PrintsAConstantWithABraceAsWritten()
    {
        var validator = new Validator<Model>(x => x.Property(model => model.S).NotEqual("{PropertyName}"));
        Assert.Equal("'S' must not be equal to '{PropertyName}'.",
            validator.Validate(new Model { S = "{PropertyName}" }).Errors[0].Message);
    }

    [Fact]
    public void Range_PrintsBoundsWithABraceAsWritten()
    {
        var validator = new Validator<Model>(x => x.Property(model => model.S).InclusiveBetween("{PropertyValue}", "{PropertyValue}z"));
        Assert.Equal("'S' must be between {PropertyValue} and {PropertyValue}z.", validator.Validate(new Model { S = "a" }).Errors[0].Message);
    }

    [Fact]
    public void CustomMessage_ReadsTheRangeBounds()
    {
        var validator = new Validator<Model>(x => x.Property(model => model.N)
            .ExclusiveBetween(1, 5, options => options.ErrorMessage = "{From} < {PropertyValue} < {To}"));

        Assert.Equal("1 < 9 < 5", validator.Validate(new Model { N = 9 }).Errors[0].Message);
    }

    private sealed class Model
    {
        public string? S { get; init; }

        public int N { get; init; }
    }
}