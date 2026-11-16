using System.Linq.Expressions;

namespace Sharp.Validator.Tests.Accessors;

// The static accessor caches keyed a path-faithful selector by expression.ToString(), which rendered the whole selector
// for every validator built, a Scoped one in every scope. They now key it by its dotted path, its body's node type and
// the type under its one unary. Selectors that share a path but not their meaning must still get accessors of their
// own, whichever is built first: an unchecked and a checked cast of one member, where the checked one throws and the
// other wraps; and a cast of a nullable member and of its Value, since the path leaves out the Value hop. A null member
// reads as null through the first and as no value through the second, so a NotNull on the first fails while the second
// skips its chain. Sharing an accessor would have let NotNull pass a null.
public sealed class AccessorKeyTests
{
    [Fact]
    public void CheckedCast_AfterAnUncheckedOneOfTheSameMember_StillChecks()
    {
        var wrapping = new Validator<WrapsFirst>(x => x.Property(model => (long)model.Big).Must(value => value == -1));
        var checking = new Validator<WrapsFirst>(x => x.Property(model => checked((long)model.Big)).Must(value => value == -1));

        Assert.True(wrapping.Validate(new WrapsFirst()).IsValid);
        Assert.Throws<OverflowException>(() => checking.Validate(new WrapsFirst()));
    }

    [Fact]
    public void UncheckedCast_AfterACheckedOneOfTheSameMember_StillWraps()
    {
        var checking = new Validator<ChecksFirst>(x => x.Property(model => checked((long)model.Big)).Must(value => value == -1));
        var wrapping = new Validator<ChecksFirst>(x => x.Property(model => (long)model.Big).Must(value => value == -1));

        Assert.Throws<OverflowException>(() => checking.Validate(new ChecksFirst()));
        Assert.True(wrapping.Validate(new ChecksFirst()).IsValid);
    }

    [Fact]
    public void CastOfANullableMember_BuiltBeforeACastOfItsValue_StillReadsNull()
    {
        var boxing = new Validator<BoxesFirst>(x => x.Property(model => (object?)model.Child.Age).NotNull());
        var unwrapping = new Validator<BoxesFirst>(x => x.Property(model => (object?)model.Child.Age!.Value).NotNull());

        Assert.False(boxing.Validate(new BoxesFirst()).IsValid);
        Assert.True(unwrapping.Validate(new BoxesFirst()).IsValid);
    }

    [Fact]
    public void CastOfANullableMember_BuiltAfterACastOfItsValue_StillReadsNull()
    {
        var unwrapping = new Validator<UnwrapsFirst>(x => x.Property(model => (object?)model.Child.Age!.Value).NotNull());
        var boxing = new Validator<UnwrapsFirst>(x => x.Property(model => (object?)model.Child.Age).NotNull());

        Assert.True(unwrapping.Validate(new UnwrapsFirst()).IsValid);
        Assert.False(boxing.Validate(new UnwrapsFirst()).IsValid);
    }

    [Fact]
    // A generic model reads a member of its constraint through a conversion, x => ((INamed)x).Name. The chain is
    // path-faithful, so each validator built over it, a Scoped one in every scope, takes the accessor the first one
    // compiled rather than compiling its own.
    public void ChainThroughTheConstraint_TakesTheFirstCompiledAccessor()
    {
        Assert.Same(Read<SharesAccessors>(), Read<SharesAccessors>());
        Assert.Same(ReadNested<SharesAccessors>(), ReadNested<SharesAccessors>());

        return;

        static Func<TModel, string?> Read<TModel>() where TModel : INamed
        {
            Expression<Func<TModel, string?>> selector = x => x.Name;
            return PropertyAccessorCache<TModel, string?>.GetOrAdd(selector.GetPropertyPath(), selector.IsPathFaithfulMemberChain(), selector);
        }

        static Func<TModel, (bool HasValue, string? Value)> ReadNested<TModel>() where TModel : INamed
        {
            Expression<Func<TModel, string?>> selector = x => x.Owner!.Name;
            return NullSafeAccessorCache<TModel, string?>.GetOrAdd(selector.GetPropertyPath(), selector.IsPathFaithfulMemberChain(), selector);
        }
    }

    [Fact]
    // The path of x => ((Named)x).Name is the path of x => x.Name, yet the first reads the base member that the
    // model's own Name hides, so the type the root converts to keeps their accessors apart.
    public void ChainThroughABaseConversion_AfterTheMemberThatHidesIt_ReadsTheBaseMember()
    {
        var own = new Validator<HidesName>(x => x.Property(model => model.Name).NotEmpty());
        var inherited = new Validator<HidesName>(x => x.Property(model => ((Named)model).Name).NotEmpty());
        var model = new HidesName("inherited", "");
        Assert.False(own.Validate(model).IsValid);
        Assert.True(inherited.Validate(model).IsValid);
    }

    private interface INamed
    {
        string? Name { get; }

        Named? Owner { get; }
    }

    private sealed class SharesAccessors : INamed
    {
        public string? Name { get; init; }

        public Named? Owner { get; init; }
    }

    private class Named(string? name)
    {
        public string? Name { get; } = name;
    }

    private sealed class HidesName(string? inherited, string? name) : Named(inherited)
    {
        public new string? Name { get; } = name;
    }

    private sealed class WrapsFirst
    {
        public ulong Big { get; } = ulong.MaxValue;
    }

    private sealed class ChecksFirst
    {
        public ulong Big { get; } = ulong.MaxValue;
    }

    private sealed class Child
    {
        public int? Age { get; init; }
    }

    private sealed class BoxesFirst
    {
        public Child Child { get; } = new();
    }

    private sealed class UnwrapsFirst
    {
        public Child Child { get; } = new();
    }
}