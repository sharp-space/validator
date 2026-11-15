namespace Sharp.Validator.Tests.Accessors;

// A Property chain started from an Elements chain must register with the primary (validator-level) builder. Before the
// composition refactor a chained builder's PrimaryBuilder pointed at the intermediate builder, so IsAsync set through
// the chain never reached the root: ValidateAsync took the sync fast path and silently skipped the async rule.
public sealed class ChainedPropertyRootingTests
{
    [Fact]
    public async Task AsyncRule_OnAnElementProperty_ReachesTheRootBuilder()
    {
        var validator = new Validator<Order>(x => x.Elements(order => order.Lines).Property(line => line.Sku)
            .MustAsync(async _ =>
            {
                await Task.Yield();
                return false;
            }));

        Assert.Throws<InvalidOperationException>(() => validator.Validate(new Order { Lines = [new Line { Sku = "s" }] }));

        var result = await validator.ValidateAsync(new Order { Lines = [new Line { Sku = "s" }] });
        Assert.False(result.IsValid);
        Assert.Equal("Lines[0].Sku", result.Errors[0].PropertyName);
    }

    private sealed class Order
    {
        public List<Line> Lines { get; init; } = [];
    }

    private sealed class Line
    {
        public string? Sku { get; init; }
    }
}