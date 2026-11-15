namespace Sharp.Validator.Tests.Pipeline;

// Regression: the element-source reuse path built a closure on every Elements chain even with reuse off, and NotEmpty
// fell through to a boxed enumerator for every non-empty List. README promises no per-rule or per-element allocation on
// the all-valid path, so the cost of one validation must not grow with chains, rules, or elements.
public sealed class AllValidAllocationTests
{
    private static readonly Model Valid = new()
    {
        Tags = ["a", "b"],
        Items = [new Item { Label = "x" }, new Item { Label = "y" }],
        Lists = [[1], [2], [3]]
    };

    private static long AllocatedBytes(Validator<Model> validator)
    {
        validator.Validate(Valid);
        var before = GC.GetAllocatedBytesForCurrentThread();
        validator.Validate(Valid);

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static long AllocatedBytes<T>(Validator<T> validator, T model)
    {
        validator.Validate(model);
        var before = GC.GetAllocatedBytesForCurrentThread();
        validator.Validate(model);

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    // A context used to carry the parameters, a partial validation's selection and the async chains whose sync rules
    // failed in fields of its own, 104 bytes on a 64-bit runtime. Those live in a side object now, which a validation
    // creates only when it uses one of them, so a context is ten pointers wide: its header (two), the model, the
    // errors, an Include child's root, the side object, the message override, the message parameters, the element
    // sources, and the error count with the flag that marks a running validation beside it.
    public void Validation_AllocatesOneContextTenPointersWide() =>
        Assert.Equal(10 * IntPtr.Size, AllocatedBytes(new Validator<Model>(x => x.Property(m => m.Tags).NotNull())));

    [Fact]
    // An Include child shared the parent's Parameters through a holder that every validation with an Include created,
    // 24 bytes, whether anything read Parameters or not. The child reads them through the root context now, so an
    // Include costs its child's context and nothing else.
    public void Include_AllocatesOnlyTheChildContext()
    {
        var child = new Validator<Item>(x => x.Property(item => item.Label).NotEmpty());
        var withInclude = new Validator<Holder>(x => x.Property(holder => holder.Item).Include(child));
        var withoutInclude = new Validator<Holder>(x => x.Property(holder => holder.Item).NotNull());
        var holder = new Holder { Item = new Item { Label = "x" } };
        Assert.Equal(
            AllocatedBytes(child, holder.Item),
            AllocatedBytes(withInclude, holder) - AllocatedBytes(withoutInclude, holder));
    }

    private static Validator<Model> Repeat(int count, Action<ValidatorBuilder<Model>> rule) => new(builder =>
    {
        for (var index = 0; index < count; index++) rule(builder);
    });

    [Fact]
    // A result can't be changed, so a validation that records nothing returns one shared result instead of a new one.
    public async Task ModelThatPassesWithoutEntries_GetsTheSharedEmptyResult()
    {
        var validator = new Validator<Model>(x => x.Elements(m => m.Tags!).NotEmpty());
        Assert.Same(ValidationResult.Empty, validator.Validate(Valid));
        Assert.Same(ValidationResult.Empty, await validator.ValidateAsync(Valid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Elements_ChainsAddNoAllocation() =>
        Assert.Equal(
            AllocatedBytes(Repeat(1, x => x.Elements(m => m.Tags!).NotEmpty())),
            AllocatedBytes(Repeat(5, x => x.Elements(m => m.Tags!).NotEmpty())));

    [Fact]
    public void ElementPropertyChains_AddNoAllocation() =>
        Assert.Equal(
            AllocatedBytes(Repeat(1, x => x.Elements(m => m.Items!).Property(i => i.Label).NotEmpty())),
            AllocatedBytes(Repeat(5, x => x.Elements(m => m.Items!).Property(i => i.Label).NotEmpty())));

    [Fact]
    public void NotEmpty_OnANonEmptyCollection_AddsNoAllocation() =>
        Assert.Equal(
            AllocatedBytes(Repeat(1, x => x.Property(m => m.Tags).NotNull())),
            AllocatedBytes(Repeat(1, x => x.Property(m => m.Tags).NotEmpty())));

    [Fact]
    public void Empty_OnANonEmptyCollection_StillFails()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Tags).Empty());
        Assert.False(validator.Validate(Valid).IsValid);
    }

    [Fact]
    public void NotEmpty_OnAnEmptyCollection_StillFails()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Tags).NotEmpty());
        Assert.False(validator.Validate(new Model { Tags = [] }).IsValid);
    }

    private sealed class Item
    {
        public string? Label { get; init; }
    }

    private sealed class Model
    {
        public List<string>? Tags { get; init; }

        public List<Item>? Items { get; init; }

        public List<List<int>>? Lists { get; init; }
    }

    private sealed class Holder
    {
        public Item? Item { get; init; }
    }
}