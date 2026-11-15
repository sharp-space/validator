namespace Sharp.Validator.Tests.Accessors;

public sealed class AccessorTests
{
    [Fact]
    public void AccessorCache_ChainedSelectorsWithSameLeafPropertyInfo_AreDistinguished()
    {
        // Both selectors land on Wrapper<string>.Value, but they should not be cached as the same accessor.
        var validator = new Validator<Holder<string>>(builder =>
        {
            builder.Property(x => x.A!.Value).NotEmpty();
            builder.Property(x => x.B!.Value).NotEmpty();
        });

        var result = validator.Validate(new Holder<string>
        {
            A = new() { Value = "ok" },
            B = new() { Value = "" }
        });

        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);

        // Nested selectors now report the full dotted path, so the failing B-chain is "B.Value".
        Assert.Equal("B.Value", item.PropertyName);
        // The fact that we get exactly 1 error (and not 0 / 2) means accessors were distinct.
    }

    [Fact]
    public void NullableStructValueChainedSelector_Works()
    {
        var validator = new Validator<Holder<int>>(x => x.Property(holder => holder.A!.Value).GreaterThan(0));
        Assert.True(validator.Validate(new Holder<int> { A = new() { Value = 5 } }).IsValid);
        Assert.False(validator.Validate(new Holder<int> { A = new() { Value = 0 } }).IsValid);
    }

    private sealed class Holder<T>
    {
        public Wrapper<T>? A { get; init; }

        public Wrapper<T>? B { get; init; }
    }

    private sealed class Wrapper<T>
    {
        public T? Value { get; init; }
    }
}