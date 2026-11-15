namespace Sharp.Validator.Tests.Accessors;

// A cast in a selector (e.g. ((IHasId)x.Item).Id) ended its dotted path at the Convert node, so the path was just "Id".
// Keyed by it, the accessor cache reused a sibling x.Id rule's accessor, a silent false negative. The cache learned to
// tell them apart, but the chain still reported its errors under "Id", and a partial validation of x => x.Id selected
// it. No cache key can fix that, so such a selector is rejected where it is declared.
public sealed class AccessorCastCollisionTests
{
    [Fact]
    public void CastSelector_IsRejectedWhereItIsDeclared()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new Validator<Holder>(builder =>
        {
            builder.Property(x => x.Id).Equal(1);

            // The cast is deliberate: it is the shape under test, so the "redundant" cast must stay.
            // ReSharper disable once RedundantCast
            builder.Property(x => ((IHasId)x.Item).Id).Equal(1);
        }));

        Assert.Contains("reads through a cast", exception.Message, StringComparison.Ordinal);
    }

    private interface IHasId
    {
        int Id { get; }
    }

    private sealed class Item : IHasId
    {
        public int Id { get; init; }
    }

    private sealed class Holder
    {
        public int Id { get; init; }

        public Item Item { get; } = new();
    }
}