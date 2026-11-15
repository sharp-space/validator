namespace Sharp.Validator.Tests.Validators;

// InclusiveBetween and ExclusiveBetween took fixed bounds only, so a range set by two other properties, such as a
// quantity between the order's minimum and maximum, needed two comparison rules with two messages. Both now take their
// bounds as expressions too.
public sealed class BetweenCrossPropertyTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(0, false)]
    [InlineData(6, false)]
    public void InclusiveBetween_ReadsBothBoundsFromTheModel(int quantity, bool valid)
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Quantity)
            .InclusiveBetween(order => order.Minimum, order => order.Maximum));

        Assert.Equal(valid, validator.Validate(new Order { Quantity = quantity, Minimum = 1, Maximum = 5 }).IsValid);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, true)]
    [InlineData(5, false)]
    public void ExclusiveBetween_ReadsBothBoundsFromTheModel(int quantity, bool valid)
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Quantity)
            .ExclusiveBetween(order => order.Minimum, order => order.Maximum));

        Assert.Equal(valid, validator.Validate(new Order { Quantity = quantity, Minimum = 1, Maximum = 5 }).IsValid);
    }

    [Fact]
    public void DefaultMessage_PrintsTheBoundsTheModelHad()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Quantity)
            .InclusiveBetween(order => order.Minimum, order => order.Maximum));

        var error = Assert.Single(validator.Validate(new Order { Quantity = 9, Minimum = 1, Maximum = 5 }).Errors);
        Assert.Equal("'Quantity' must be between 1 and 5.", error.Message);
        Assert.Equal("InclusiveBetween", error.Code);
    }

    [Fact]
    public void CustomMessage_ReadsTheBoundNames()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Quantity)
            .ExclusiveBetween(order => order.Minimum, order => order.Limits!.Max,
                options => options.ErrorMessage = "{FromPropertyName}={From} {ToPropertyName}={To}"));

        var error = Assert.Single(validator.Validate(new Order { Quantity = 9, Minimum = 1, Limits = new Limits { Max = 5 } }).Errors);
        Assert.Equal("Minimum=1 Limits.Max=5", error.Message);
    }

    [Fact]
    // Like {TargetPropertyName}: the naming policy applies, the name wrappers don't.
    public void Bound_NamesFollowTheNamingPolicy()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Quantity)
            .ExclusiveBetween(order => order.Minimum, order => order.Limits!.Max,
                options => options.ErrorMessage = "{FromPropertyName} {ToPropertyName}"));

        validator.Configure(options =>
        {
            options.PropertyNamePolicy = PropertyNamePolicy.SnakeCase;
            options.PropertyNamePrefix = "PRE_";
        });

        var error = Assert.Single(validator.Validate(new Order { Quantity = 9, Minimum = 1, Limits = new Limits { Max = 5 } }).Errors);
        Assert.Equal("minimum limits.max", error.Message);
    }

    [Fact]
    public void BoundBehindANullMember_SkipsTheCheck()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Quantity)
            .InclusiveBetween(order => order.Limits!.Min, order => order.Limits!.Max));

        Assert.True(validator.Validate(new Order { Quantity = 99 }).IsValid);
    }

    [Fact]
    public void InvertedRange_AdmitsNoValue()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Quantity)
            .InclusiveBetween(order => order.Minimum, order => order.Maximum));

        Assert.False(validator.Validate(new Order { Quantity = 3, Minimum = 5, Maximum = 1 }).IsValid);
    }

    [Fact]
    // A computed bound is compiled and has no name, so its name placeholder reads empty, as a fixed bound's does.
    public void ComputedBound_IsCompiled()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Quantity)
            .InclusiveBetween(order => order.Minimum, order => order.Minimum * 2,
                options => options.ErrorMessage = "[{ToPropertyName}] {To}"));

        var error = Assert.Single(validator.Validate(new Order { Quantity = 9, Minimum = 2 }).Errors);
        Assert.Equal("[] 4", error.Message);
    }

    private sealed class Limits
    {
        public int Min { get; init; }

        public int Max { get; init; }
    }

    private sealed class Order
    {
        public int Quantity { get; init; }

        public int Minimum { get; init; }

        public int Maximum { get; init; }

        public Limits? Limits { get; init; }
    }
}