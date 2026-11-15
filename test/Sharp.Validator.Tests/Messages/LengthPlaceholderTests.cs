namespace Sharp.Validator.Tests.Messages;

// Regression: MinimumLength/MaximumLength Func-overloads previously routed through the (Func, Func) base ctor, which
// always seeded both {MinLength} and {MaxLength} placeholders.
// That caused user messages referencing the unused side to surface "-1" or "0".
public sealed class LengthPlaceholderTests
{
    [Fact]
    public void MinimumLengthFunc_DoesNotLeakMaxLengthPlaceholder()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .MinimumLength(x => x.Age, x => x.ErrorMessage = "min={MinLength} max={MaxLength}"));

        var result = validator.Validate(new Person { Name = "ab", Age = 5 });
        Assert.False(result.IsValid);

        var message = result.Errors[0].Message!;
        Assert.Contains("min=5", message);

        // {MaxLength} placeholder must remain literal — the validator has no meaningful max.
        Assert.Contains("max={MaxLength}", message);
        Assert.DoesNotContain("max=-1", message);
    }

    [Fact]
    public void MaximumLengthFunc_DoesNotLeakMinLengthPlaceholder()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .MaximumLength(x => x.Age, x => x.ErrorMessage = "min={MinLength} max={MaxLength}"));

        var result = validator.Validate(new Person { Name = "abcdefghij", Age = 5 });
        Assert.False(result.IsValid);

        var message = result.Errors[0].Message!;
        Assert.Contains("max=5", message);
        Assert.Contains("min={MinLength}", message);
        Assert.DoesNotContain("min=0", message);
    }

    [Fact]
    public void LengthFunc_KeepsBothPlaceholders()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .Length(x => x.Age, x => x.Age + 2, x => x.ErrorMessage = "min={MinLength} max={MaxLength}"));

        var result = validator.Validate(new Person { Name = "abcdef", Age = 2 });
        Assert.False(result.IsValid);

        var message = result.Errors[0].Message!;
        Assert.Contains("min=2", message);
        Assert.Contains("max=4", message);
    }

    [Fact]
    // Regression: constant bounds were only baked into the default message, so a custom message printed the raw
    // {MaxLength} placeholder that README documents.
    public void MaximumLengthConstant_FillsMaxLengthInACustomMessage()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .MaximumLength(3, x => x.ErrorMessage = "max {MaxLength}, got {Length}"));

        Assert.Equal("max 3, got 5", validator.Validate(new Person { Name = "abcde" }).Errors[0].Message);
    }

    [Fact]
    public void MinimumLengthConstant_FillsMinLengthInACustomMessage()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .MinimumLength(9, x => x.ErrorMessage = "min {MinLength}, got {Length}"));

        Assert.Equal("min 9, got 5", validator.Validate(new Person { Name = "abcde" }).Errors[0].Message);
    }

    [Fact]
    public void LengthRange_FillsBothBoundsIncludingAnExplicitZero()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .Length(0, 3, x => x.ErrorMessage = "{MinLength}-{MaxLength}"));

        Assert.Equal("0-3", validator.Validate(new Person { Name = "abcde" }).Errors[0].Message);
    }

    [Fact]
    public void ExactLength_FillsBothBoundsInACustomMessage()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .Length(3, x => x.ErrorMessage = "{MinLength}/{MaxLength}"));

        Assert.Equal("3/3", validator.Validate(new Person { Name = "abcde" }).Errors[0].Message);
    }

    [Fact]
    public void MaximumLengthConstant_LeavesMinLengthUnset()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .MaximumLength(3, x => x.ErrorMessage = "{MinLength}"));

        Assert.Equal("{MinLength}", validator.Validate(new Person { Name = "abcde" }).Errors[0].Message);
    }

    [Fact]
    public void MinimumLengthConstant_LeavesMaxLengthUnset()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .MinimumLength(9, x => x.ErrorMessage = "{MaxLength}"));

        Assert.Equal("{MaxLength}", validator.Validate(new Person { Name = "abcde" }).Errors[0].Message);
    }
}