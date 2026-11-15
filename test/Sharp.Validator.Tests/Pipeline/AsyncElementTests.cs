namespace Sharp.Validator.Tests.Pipeline;

public sealed class AsyncElementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncElementProperty_CascadeStopOrWarning_DecidesHowManyElementsRun(bool list)
    {
        foreach (var stop in new[] { false, true })
        foreach (var warning in new[] { false, true })
        {
            var calls = 0;
            var validator = new Validator<Model>(b => b.Elements(x => x.Items)
                .Property(x => x!.Name, o => o.CascadeMode = stop ? ValidationCascadeMode.Stop : ValidationCascadeMode.Continue)
                .MustAsync(_ =>
                {
                    calls++;
                    return Task.FromResult(false);
                }, o => o.ErrorSeverity = warning ? ValidationErrorSeverity.Warning : ValidationErrorSeverity.Error));

            IEnumerable<Item?> items = list ? new List<Item?> { new(), new() } : new HashSet<Item?> { new(), new() };
            var result = await validator.ValidateAsync(new Model { Items = items }, TestContext.Current.CancellationToken);

            Assert.Equal(stop && !warning ? 1 : 2, calls);
            Assert.Equal(warning, result.IsValid);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // A list is walked with the walk's own read and any other source with the reader's; both skip a null element and
    // a null intermediate.
    public async Task AsyncElementProperties_NullIntermediateOrEmptySource_AreSkipped(bool list)
    {
        var validator = new Validator<Model>(b => b.Elements(x => x.Items)
            .Property(x => x!.Child!.Name).MustAsync(value => Task.FromResult(value is not null)));

        var token = TestContext.Current.CancellationToken;
        Assert.True((await validator.ValidateAsync(new Model(), token)).IsValid);
        Assert.True((await validator.ValidateAsync(new Model { Items = Source(null, new()) }, token)).IsValid);
        Assert.False((await validator.ValidateAsync(new Model { Items = Source(new Item { Child = new() }) }, token)).IsValid);

        return;

        IEnumerable<Item?> Source(params Item?[] items) => list ? new List<Item?>(items) : new HashSet<Item?>(items);
    }

    [Fact]
    public async Task AsyncCondition_AfterADeferredSyncFailure_CanStopTheGlobalPlan()
    {
        var later = 0;
        var validator = new Validator<Model>(b =>
        {
            b.Property(x => x.Items).NotNull().Where(_ => Task.FromResult(true));
            b.Property(x => x.Items).MustAsync(_ =>
            {
                later++;
                return Task.FromResult(true);
            });
        });

        validator.Configure(o => o.CascadeMode = ValidationCascadeMode.Stop);
        Assert.False((await validator.ValidateAsync(new Model(), TestContext.Current.CancellationToken)).IsValid);
        Assert.Equal(0, later);
    }

    [Fact]
    public async Task AsyncWhere_TokenOverload_KeepsTheElementBuilder()
    {
        var validator = new Validator<Model>(b => b.Elements(x => x.Items)
            .Where((_, token) => Task.FromResult(!token.IsCancellationRequested)).NotNull());

        Assert.True((await validator.ValidateAsync(new Model { Items = [] }, TestContext.Current.CancellationToken)).IsValid);
    }

    private sealed class Model
    {
        public IEnumerable<Item?>? Items { get; init; }
    }

    private sealed class Item
    {
        public string? Name { get; init; }

        public Item? Child { get; init; }
    }
}