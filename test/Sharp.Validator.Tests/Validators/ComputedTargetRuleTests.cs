namespace Sharp.Validator.Tests.Validators;

// Only EqualValidator and NotEqualValidator took a function of the model, so a comparison or a range over a computed
// value had to be written against the accessor tuple the library uses inside. The comparison and range rules take a
// function too, which is what README suggests for a computed target under Native AOT.
public sealed class ComputedTargetRuleTests
{
    private static readonly Order Sample = new() { Price = 10, Minimum = 5, Maximum = 20 };

    [Fact]
    public void ComparisonRules_OverAComputedTarget_CompareWithItsValue()
    {
        Assert.True(IsValid(new GreaterThanValidator<Order, decimal>(x => (x.Minimum * 2) - 1)));
        Assert.False(IsValid(new GreaterThanValidator<Order, decimal>(x => x.Minimum * 2)));
        Assert.True(IsValid(new GreaterThanOrEqualValidator<Order, decimal>(x => x.Minimum * 2)));
        Assert.False(IsValid(new LessThanValidator<Order, decimal>(x => x.Minimum * 2)));
        Assert.True(IsValid(new LessThanOrEqualValidator<Order, decimal>(x => x.Minimum * 2)));
    }

    [Fact]
    public void EqualityRules_OverAComputedTarget_CompareWithItsValue()
    {
        Assert.True(IsValid(new EqualValidator<Order, decimal>(x => x.Minimum * 2)));
        Assert.False(IsValid(new NotEqualValidator<Order, decimal>(x => x.Minimum * 2)));
    }

    [Fact]
    public void RangeRules_OverComputedBounds_CompareWithTheirValues()
    {
        Assert.True(IsValid(new InclusiveBetweenValidator<Order, decimal>(x => x.Minimum * 2, x => x.Maximum)));
        Assert.False(IsValid(new ExclusiveBetweenValidator<Order, decimal>(x => x.Minimum * 2, x => x.Maximum)));
    }

    [Fact]
    public void ComputedTarget_IsPrintedByItsValue()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Price)
            .Add(new GreaterThanValidator<Order, decimal>(order => order.Minimum * 2)));

        Assert.Equal("'Price' must be greater than '10'.", Assert.Single(validator.Validate(Sample).Errors).Message);
    }

    private static bool IsValid(PropertyValidator<Order, decimal> rule) =>
        new Validator<Order>(x => x.Property(order => order.Price).Add(rule)).Validate(Sample).IsValid;

    private sealed class Order
    {
        public decimal Price { get; init; }

        public decimal Minimum { get; init; }

        public decimal Maximum { get; init; }
    }
}