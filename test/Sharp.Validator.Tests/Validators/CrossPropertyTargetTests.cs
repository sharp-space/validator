namespace Sharp.Validator.Tests.Validators;

// Cross-property rules took a member chain or nothing: a computed comparison target threw at registration, and Equal
// took a plain delegate, so it had no target name for a message and a null on the way to its target threw. Every rule
// now takes an expression: a member chain reads null-safely and names its target, anything else is compiled as written.
public sealed class CrossPropertyTargetTests
{
    [Fact]
    public void Comparison_TakesAComputedTarget()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Quantity).GreaterThan(order => order.Minimum * 2));
        Assert.True(validator.Validate(new Order { Quantity = 5, Minimum = 2 }).IsValid);
        Assert.False(validator.Validate(new Order { Quantity = 4, Minimum = 2 }).IsValid);
    }

    [Fact]
    public void Equal_NamesAMemberTargetForItsMessage()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Code).Equal(order => order.Original!.Code,
            options => options.ErrorMessage = "'{PropertyName}' must match '{TargetPropertyName}'."));

        var result = validator.Validate(new Order { Code = "a", Original = new Order { Code = "b" } });
        Assert.Equal("'Code' must match 'Original.Code'.", result.Errors[0].Message);
    }

    [Fact]
    // Regression: the default message printed the target's value, so a confirmation rule wrote the password it compared
    // against into the error, and from there into logs. A member target is named instead.
    public void Equal_MemberTarget_DefaultMessageNamesTheTarget()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Code).Equal(order => order.Original!.Code));
        Assert.Equal("'Code' must be equal to 'Original.Code'.",
            validator.Validate(new Order { Code = "secret-a", Original = new Order { Code = "secret-b" } }).Errors[0].Message);
    }

    [Fact]
    public void NotEqual_MemberTarget_DefaultMessageNamesTheTarget()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Code).NotEqual(order => order.Original!.Code));
        Assert.Equal("'Code' must not be equal to 'Original.Code'.",
            validator.Validate(new Order { Code = "secret", Original = new Order { Code = "secret" } }).Errors[0].Message);
    }

    [Fact]
    public void ComputedTarget_StillShowsItsValueInTheDefaultMessage()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Quantity).Equal(order => order.Minimum * 2));
        Assert.Equal("'Quantity' must be equal to '4'.", validator.Validate(new Order { Quantity = 3, Minimum = 2 }).Errors[0].Message);
    }

    [Fact]
    public void Equal_ComputedTarget_ComparesAgainstTheComputedValue()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Code).Equal(order => order.Original!.Code!.ToUpperInvariant()));
        Assert.True(validator.Validate(new Order { Code = "AB", Original = new Order { Code = "ab" } }).IsValid);
    }

    [Fact]
    public void NotEqual_ComputedTarget_ComparesAgainstTheComputedValue()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Code).NotEqual(order => order.Original!.Code!.ToUpperInvariant()));
        Assert.False(validator.Validate(new Order { Code = "AB", Original = new Order { Code = "ab" } }).IsValid);
    }

    [Fact]
    public void Equal_SkipsATargetItCannotReach()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Code).Equal(order => order.Original!.Code));
        Assert.True(validator.Validate(new Order { Code = "a" }).IsValid);
    }

    private sealed class Order
    {
        public int Quantity { get; init; }

        public int Minimum { get; init; }

        public string? Code { get; init; }

        public Order? Original { get; init; }
    }
}