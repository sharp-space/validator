namespace Sharp.Validator.Tests.Pipeline;

// Regression: a block Where appended its condition after the ones its chains already held, so the inner condition ran
// first and threw on the null the outer condition was there to guard. Sync conditions also overtook an enclosing async
// guard. Conditions must run in nesting order, whatever mix of sync and async they are.
public sealed class WhereNestingOrderTests
{
    [Fact]
    public void OuterBlock_GuardsInnerBlock() =>
        Assert.True(new NestedBlocks().Validate(new Model()).IsValid);

    [Fact]
    public void OuterBlock_GuardsChainCondition() =>
        Assert.True(new ChainInsideBlock().Validate(new Model()).IsValid);

    [Fact]
    public async Task AsyncOuterBlock_GuardsAsyncInnerBlock() =>
        Assert.True((await new NestedAsyncBlocks().ValidateAsync(new Model())).IsValid);

    [Fact]
    public async Task AsyncOuterBlock_GuardsSyncChainCondition() =>
        Assert.True((await new AsyncOuterSyncInner().ValidateAsync(new Model())).IsValid);

    [Fact]
    public async Task SyncOuterBlock_GuardsAsyncChainCondition() =>
        Assert.True((await new SyncOuterAsyncInner().ValidateAsync(new Model())).IsValid);

    [Fact]
    public async Task GuardedRules_StillRunWhenEveryConditionHolds()
    {
        var model = new Model { Inner = new Inner { Country = "US" } };
        Assert.False(new NestedBlocks().Validate(model).IsValid);
        Assert.False(new ChainInsideBlock().Validate(model).IsValid);
        Assert.False((await new NestedAsyncBlocks().ValidateAsync(model)).IsValid);
        Assert.False((await new AsyncOuterSyncInner().ValidateAsync(model)).IsValid);
        Assert.False((await new SyncOuterAsyncInner().ValidateAsync(model)).IsValid);
    }

    [Fact]
    public async Task MixedConditions_RunInNestingThenChainOrder()
    {
        var validator = new Recording();
        await validator.ValidateAsync(new Model());
        Assert.Equal(["outer", "inner", "first", "second", "third"], validator.Calls);
    }

    [Fact]
    public async Task AsyncCondition_OnElements_GuardsSyncConditionOnElementProperty()
    {
        var validator = new Validator<Model>(builder =>
        {
            var items = builder.Elements(x => x.Items!).Where(x => Task.FromResult(x.Inner != null));
            items.Property(x => x.Country).Where(x => x.Inner!.Country == "US").NotEmpty();
        });

        var result = await validator.ValidateAsync(new Model { Items = [new Inner()] });
        Assert.True(result.IsValid);
    }

    private sealed class Inner
    {
        public string? Country { get; init; }
    }

    private sealed class Model
    {
        public Inner? Inner { get; init; }

        public List<Inner>? Items { get; init; }

        public string? Name { get; init; }
    }

    private sealed class NestedBlocks : Validator<Model>
    {
        public NestedBlocks() => Builder.Where(x => x.Inner != null, () =>
            Builder.Where(x => x.Inner!.Country == "US", () => Builder.Property(x => x.Name).NotEmpty()));
    }

    private sealed class ChainInsideBlock : Validator<Model>
    {
        public ChainInsideBlock() => Builder.Where(x => x.Inner != null, () =>
            Builder.Property(x => x.Name).Where(x => x.Inner!.Country == "US").NotEmpty());
    }

    private sealed class NestedAsyncBlocks : Validator<Model>
    {
        public NestedAsyncBlocks() => Builder.Where(x => Task.FromResult(x.Inner != null), () =>
            Builder.Where(x => Task.FromResult(x.Inner!.Country == "US"), () => Builder.Property(x => x.Name).NotEmpty()));
    }

    private sealed class AsyncOuterSyncInner : Validator<Model>
    {
        public AsyncOuterSyncInner() => Builder.Where(x => Task.FromResult(x.Inner != null), () =>
            Builder.Property(x => x.Name).Where(x => x.Inner!.Country == "US").NotEmpty());
    }

    private sealed class SyncOuterAsyncInner : Validator<Model>
    {
        public SyncOuterAsyncInner() => Builder.Where(x => x.Inner != null, () =>
            Builder.Property(x => x.Name).Where(x => Task.FromResult(x.Inner!.Country == "US")).NotEmpty());
    }

    private sealed class Recording : Validator<Model>
    {
        public Recording() => Builder.Where(_ => Record("outer"), () =>
            Builder.Where(_ => Task.FromResult(Record("inner")), () =>
                Builder.Property(x => x.Name)
                    .Where(_ => Record("first"))
                    .Where(_ => Task.FromResult(Record("second")))
                    .Where(_ => Record("third"))
                    .NotEmpty()));

        internal List<string> Calls { get; } = [];

        private bool Record(string name)
        {
            Calls.Add(name);
            return true;
        }
    }
}