using System.Linq.Expressions;

namespace Sharp.Validator.Tests.Accessors;

public sealed class ValueRootAccessorTests
{
    private static int[] StaticItems => [1];

    private static Holder Root(Model model) => model.Holder;

    private static Holder? OptionalRoot(Model model) => model.Optional;

    [Fact]
    public void CovariantNullSafeAccessor_ConvertsTheProducedValue()
    {
        Expression<Func<Model, string>> selector = x => x.Holder.Leaf.Text;
        var accessor = NullSafeAccessorCache<Model, object>.GetOrAdd("Holder.Leaf.Text", false, selector);
        Assert.Equal("value", accessor(new Model { Holder = new(new("value", [1])) }).Value);
    }

    [Fact]
    public void ValueAndNullableExpressionRoots_PreserveNullShortCircuiting()
    {
        Expression<Func<Model, string>> valueRoot = x => Root(x).Leaf.Text;
        Expression<Func<Model, string>> nullableRoot = x => OptionalRoot(x)!.Value.Leaf.Text;

        Assert.Equal("value",
            NullSafeAccessorCache<Model, string>.GetOrAdd("value", false, valueRoot)(new Model { Holder = new(new("value", [])) }).Value);

        Assert.False(NullSafeAccessorCache<Model, string>.GetOrAdd("optional", false, nullableRoot)(new Model()).HasValue);
    }

    [Fact]
    // A struct on the way to the collection can't be null, so only the Nullable<T> one is tested before its Value.
    public void ElementsThroughValueTypeMembers_PreserveNullShortCircuiting()
    {
        var validator = new Validator<Model>(b =>
        {
            b.Elements(x => x.Holder.Leaf.Items).GreaterThan(0);
            b.Elements(x => x.Optional!.Value.Leaf.Items).GreaterThan(1);
        });

        Assert.True(validator.Validate(new Model { Holder = new(new("", [1])) }).IsValid);
        var error = Assert.Single(validator.Validate(new Model { Holder = new(new("", [1])), Optional = new(new("", [1])) }).Errors);
        Assert.Equal("Optional.Leaf.Items[0]", error.PropertyName);
    }

    [Fact]
    // A chain's own selector can't start at a call or a static member: it has no path from the model.
    public void ElementsFromACallOrAStaticMember_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new Validator<Model>(b => b.Elements(x => Root(x).Leaf.Items)));
        Assert.Throws<InvalidOperationException>(() => new Validator<Model>(b => b.Elements(x => OptionalRoot(x)!.Value.Leaf.Items)));
        Assert.Throws<InvalidOperationException>(() => new Validator<Model>(b => b.Elements(x => StaticItems)));
    }

    [Fact]
    public void NullableValueOnlySelector_WhenRequested_HasAnEmptyPath()
    {
        Expression<Func<int?, int>> selector = value => value!.Value;
        Assert.Equal(string.Empty, selector.GetPropertyPath(true));

        Expression<Func<Model, int>> constant = _ => 42;
        Assert.Throws<InvalidOperationException>(() => constant.GetPropertyPath(true));
    }

    private readonly record struct Leaf(string Text, int[]? Items);

    private readonly record struct Holder(Leaf Leaf);

    private sealed class Model
    {
        public Holder Holder { get; init; }

        public Holder? Optional { get; init; }
    }
}