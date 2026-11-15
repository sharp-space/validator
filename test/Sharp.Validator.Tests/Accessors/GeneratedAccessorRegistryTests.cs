using System.Linq.Expressions;
using Sharp.Validator.Bindings;

namespace Sharp.Validator.Tests.Accessors;

public sealed class GeneratedAccessorRegistryTests
{
    [Fact]
    public void GeneratedPropertyAccessorsWinOnly_ForTheRegisteredFaithfulPath()
    {
        Assert.False(AccessorRegistry.HasProperty<Model, string?>("Text"));

        Func<Model, string?> generated = _ => "generated";
        AccessorRegistry.RegisterProperty("Text", generated);
        AccessorRegistry.RegisterProperty<Model, string?>("Other", _ => "other");
        Assert.True(AccessorRegistry.HasProperty<Model, string?>("Text"));
        Assert.False(AccessorRegistry.HasProperty<Model, string?>("Missing"));

        Expression<Func<Model, string?>> expression = x => x.Text;
        Assert.Same(generated, PropertyAccessorCache<Model, string?>.GetOrAdd("Text", true, expression));
        Assert.Equal("original", PropertyAccessorCache<Model, string?>.GetOrAdd("Text", false, expression)(new Model { Text = "original" }));
        Assert.Null(PropertyAccessorCache<Model, string?>.GetOrAdd("Missing", true, expression)(new Model()));

        var validator = new Validator<Model>(b => b.Property(x => x.Text).Equal("generated"));
        Assert.True(validator.Validate(new Model()).IsValid);

        var captured = new Model { Text = "captured" };
        Assert.Throws<InvalidOperationException>(() => new Validator<Model>(b => b.Property(x => captured.Text).Equal("captured")));
    }

    [Fact]
    public void Registry_NullSafeOrCollectionDuplicate_KeepsTheFirstAccessor()
    {
        Assert.False(AccessorRegistry.HasNullSafe<Model, string?>("Child.Text"));

        Func<Model, (bool, string?)> safe = _ => (true, "generated");

        AccessorRegistry.RegisterNullSafe("Child.Text", safe);
        AccessorRegistry.RegisterNullSafe<Model, string?>("Other", _ => (false, null));
        Assert.True(AccessorRegistry.HasNullSafe<Model, string?>("Child.Text"));
        Assert.False(AccessorRegistry.HasNullSafe<Model, string?>("Missing"));

        Expression<Func<Model, string?>> expression = x => x.Child!.Text;
        Assert.Same(safe, NullSafeAccessorCache<Model, string?>.GetOrAdd("Child.Text", true, expression));
        Assert.False(NullSafeAccessorCache<Model, string?>.GetOrAdd("Missing", true, expression)(new Model()).HasValue);
        Assert.True(new Validator<Model>(b => b.Property(x => x.Child!.Text).Equal("generated")).Validate(new Model()).IsValid);
        Assert.False(AccessorRegistry.HasElements<Model, string>("Items"));

        AccessorRegistry.RegisterElements<Model, string>("Items", _ => ["generated"]);
        AccessorRegistry.RegisterElements<Model, string>("Other", _ => []);
        Assert.True(AccessorRegistry.HasElements<Model, string>("Items"));
        Assert.False(AccessorRegistry.HasElements<Model, string>("Missing"));
        Assert.True(new Validator<Model>(b => b.Elements(x => x.Items).Equal("generated")).Validate(new Model()).IsValid);

        var captured = new Model { Items = ["captured"] };
        Assert.Throws<InvalidOperationException>(() => new Validator<Model>(b => b.Elements(x => captured.Items).Equal("captured")));
    }

    [Fact]
    // The generator registers by path alone, and the path leaves out a Nullable<T>.Value hop, so a conversion of a
    // nullable member and the same member's Value could each pick up the other's registration. Each reads through its
    // own compiled accessor instead, as it does in a build without the generator. Each test seeds its own model type,
    // since a registration lasts for the process.
    public void CastOfANullableMember_RegisteredForItsValue_ReadsThroughItsOwnAccessor()
    {
        // What the generator registers for x => x.Owner.Age.Value: a null age is no value, which skips the chain.
        AccessorRegistry.RegisterNullSafe<CastHolder, int>("Owner.Age", holder => holder.Owner.Age is { } age ? (true, age) : (false, 0));
        var validator = new Validator<CastHolder>(x => x.Property(holder => (int)holder.Owner.Age!).GreaterThan(0));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(new CastHolder()));
    }

    [Fact]
    public void BoxedValueOfANullableMember_RegisteredForTheMember_ReadsThroughItsOwnAccessor()
    {
        // What the generator registers for x => x.Owner.Age read as an object: a null age reads as null.
        AccessorRegistry.RegisterNullSafe<BoxHolder, object?>("Owner.Age", holder => (true, holder.Owner.Age));
        var validator = new Validator<BoxHolder>(x => x.Property(holder => (object?)holder.Owner.Age!.Value).NotNull());
        Assert.True(validator.Validate(new BoxHolder()).IsValid);
    }

    [Fact]
    // The generator registers a member the compiler converts, such as one boxed to object, and an array's Length.
    public void ConvertedMemberOrArrayLength_WithARegistration_ReadsThroughIt()
    {
        AccessorRegistry.RegisterProperty<NumberHolder, object?>("Number", _ => "generated");
        AccessorRegistry.RegisterNullSafe<NumberHolder, int>("Numbers.Length", _ => (true, 42));

        Assert.True(new Validator<NumberHolder>(x => x.Property<object?>(holder => holder.Number).Equal("generated"))
            .Validate(new NumberHolder()).IsValid);

        Assert.True(new Validator<NumberHolder>(x => x.Property(holder => holder.Numbers.Length).Equal(42))
            .Validate(new NumberHolder()).IsValid);
    }

    [Fact]
    // C# reads an int member as long? through two conversions. The generator registers the member converted the same
    // way, so its registration serves the target in place of an accessor compiled for each validator, which Native AOT
    // interprets. Before, only a single conversion could be served.
    public void WidenedTarget_RegisteredForItsPath_ReadsTheRegistration()
    {
        AccessorRegistry.RegisterNullSafe<WidenHolder, long?>("Limits.Floor", _ => (true, 7L));
        var validator = new Validator<WidenHolder>(x => x.Property(holder => holder.Balance).Equal(holder => holder.Limits!.Floor));
        Assert.True(validator.Validate(new WidenHolder { Balance = 7, Limits = new WidenLimits { Floor = 1 } }).IsValid);
    }

    [Fact]
    // Not for a Nullable<T>.Value read as another nullable type: the path leaves the hop out, so the generator's
    // registration under it is the nullable member's, which the Value's must not take.
    public void WidenedValueOfANullableMember_RegisteredForTheMember_ReadsThroughItsOwnAccessor()
    {
        AccessorRegistry.RegisterNullSafe<WidenValueHolder, long?>("Owner.Age", _ => (true, 100L));
        var validator = new Validator<WidenValueHolder>(x => x.Property(holder => holder.Limit)
            .GreaterThanOrEqual(holder => holder.Owner.Age!.Value));

        Assert.True(validator.Validate(new WidenValueHolder { Limit = 1, Owner = new Owner { Age = 1 } }).IsValid);
    }

    [Fact]
    public void Registry_NullPathOrDelegate_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AccessorRegistry.RegisterNullSafe<Model, string>(null!, _ => (true, "x")));
        Assert.Throws<ArgumentNullException>(() => AccessorRegistry.RegisterNullSafe<Model, string>("Text", null!));
        Assert.Throws<ArgumentNullException>(() => AccessorRegistry.RegisterElements<Model, string>(null!, _ => []));
        Assert.Throws<ArgumentNullException>(() => AccessorRegistry.RegisterElements<Model, string>("Items", null!));
    }

    private sealed class Model
    {
        public string? Text { get; init; }

        public Model? Child { get; init; }

        public IEnumerable<string>? Items { get; init; }
    }

    private sealed class Owner
    {
        public int? Age { get; init; }
    }

    private sealed class WidenLimits
    {
        public int Floor { get; init; }
    }

    private sealed class WidenHolder
    {
        public long? Balance { get; init; }

        public WidenLimits? Limits { get; init; }
    }

    private sealed class WidenValueHolder
    {
        public long? Limit { get; init; }

        public Owner Owner { get; init; } = new();
    }

    private sealed class CastHolder
    {
        public Owner Owner { get; } = new();
    }

    private sealed class BoxHolder
    {
        public Owner Owner { get; } = new();
    }

    private sealed class NumberHolder
    {
        public int Number { get; init; }

        public int[] Numbers { get; } = [];
    }
}