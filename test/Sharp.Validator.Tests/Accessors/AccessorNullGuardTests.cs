namespace Sharp.Validator.Tests.Accessors;

// The compiled accessors tested every member on the way to the value with Expression.Equal(member, null), which binds
// to the == a reference type declares. An operator that always answers true made a present member look absent, so the
// rule was skipped, and a value object whose == reads both operands threw NullReferenceException for a model that was
// fine. Absent has to mean a null reference or an empty Nullable<T>, which is what the generated accessors' `is null`
// tests, so the two paths judge the same model the same way.
public sealed class AccessorNullGuardTests
{
    [Fact]
    public void MemberWhoseEqualityAlwaysMatches_StillCountsAsPresent()
    {
        var validator = new Validator<Holder>(x => x.Property(holder => holder.Loose!.Name).NotEmpty());
        var error = Assert.Single(validator.Validate(new Holder { Loose = new AlwaysEqual { Name = "" } }).Errors);
        Assert.Equal("Loose.Name", error.PropertyName);
        Assert.True(validator.Validate(new Holder()).IsValid);
    }

    [Fact]
    public void MemberWhoseEqualityReadsBothOperands_IsCheckedWithoutCallingIt()
    {
        var validator = new Validator<Holder>(x => x.Property(holder => holder.Strict!.Code).NotEmpty());
        Assert.Single(validator.Validate(new Holder { Strict = new Strict { Code = "" } }).Errors);
        Assert.True(validator.Validate(new Holder { Strict = new Strict { Code = "a" } }).IsValid);
        Assert.True(validator.Validate(new Holder()).IsValid);
    }

    [Fact]
    public void ElementsBehindSuchAMember_AreValidated()
    {
        var validator = new Validator<Holder>(x => x.Elements(holder => holder.Loose!.Tags).NotEmpty());
        var error = Assert.Single(validator.Validate(new Holder { Loose = new AlwaysEqual { Tags = ["", "a"] } }).Errors);
        Assert.Equal("Loose.Tags[0]", error.PropertyName);
    }

    [Fact]
    // A cross-property target may start at a cast, which a chain's own selector may not; the cast's result is the root
    // the null-safe accessor tests, with `is null`, never with the operator that matches everything here.
    public void CastRootedTargetWhoseEqualityAlwaysMatches_IsCompared()
    {
        var validator = new Validator<Holder>(x => x.Property(holder => holder.Value).Equal(holder => ((AlwaysEqual)holder.Any!).Name));
        Assert.Single(validator.Validate(new Holder { Value = "a", Any = new AlwaysEqual { Name = "b" } }).Errors);
        Assert.True(validator.Validate(new Holder { Value = "a", Any = new AlwaysEqual { Name = "a" } }).IsValid);
    }

    [Fact]
    public void CrossPropertyTargetBehindSuchAMember_IsCompared()
    {
        var validator = new Validator<Holder>(x => x.Property(holder => holder.Value).Equal(holder => holder.Loose!.Name));
        Assert.Single(validator.Validate(new Holder { Value = "a", Loose = new AlwaysEqual { Name = "b" } }).Errors);
        Assert.True(validator.Validate(new Holder { Value = "a", Loose = new AlwaysEqual { Name = "a" } }).IsValid);
    }

    [Fact]
    // A Nullable<T> member is tested for a value, never with T's own operator, which throws here if it is called.
    public void NullableStructMember_IsCheckedWithoutItsOperator()
    {
        var validator = new Validator<Holder>(x => x.Property(holder => holder.Gauge!.Value.Level).GreaterThan(0));
        Assert.Single(validator.Validate(new Holder { Gauge = new Gauge { Level = 0 } }).Errors);
        Assert.True(validator.Validate(new Holder { Gauge = new Gauge { Level = 1 } }).IsValid);
        Assert.True(validator.Validate(new Holder()).IsValid);
    }

    [Fact]
    // The guard tests the member it already read rather than reading it again, as the generated accessors do, so a
    // getter on the way to the value runs once per validation even when it computes or has a side effect.
    public void Member_OnTheWayToTheValue_IsReadOnce()
    {
        var inner = new CountingInner { Name = "", Tags = [""] };
        var model = new Counting(inner);
        var property = new Validator<Counting>(x => x.Property(counting => counting.Inner!.Name).NotEmpty());
        var elements = new Validator<Counting>(x => x.Elements(counting => counting.Inner!.Tags).NotEmpty());

        Assert.Single(property.Validate(model).Errors);
        Assert.Equal((1, 1), (model.InnerReads, inner.NameReads));
        Assert.Single(elements.Validate(model).Errors);
        Assert.Equal(2, model.InnerReads);
    }

    private sealed class Counting(CountingInner inner)
    {
        public int InnerReads { get; private set; }

        // ReSharper disable once ReturnTypeCanBeNotNullable — the guarded selectors read through a nullable member.
        public CountingInner? Inner
        {
            get
            {
                InnerReads++;
                return inner;
            }
        }
    }

    private sealed class CountingInner
    {
        public int NameReads { get; private set; }

        public string? Name
        {
            get
            {
                NameReads++;
                return field;
            }
            init;
        }

        public List<string?>? Tags { get; init; }
    }

    private sealed class Holder
    {
        public AlwaysEqual? Loose { get; init; }

        public Strict? Strict { get; init; }

        public object? Any { get; init; }

        public string? Value { get; init; }

        // ReSharper disable once MemberHidesStaticFromOuterClass — named after its type, as a member usually is.
        public Gauge? Gauge { get; init; }
    }

    private sealed class AlwaysEqual
    {
        public string? Name { get; init; }

        public List<string?>? Tags { get; init; }

        public override bool Equals(object? obj) => obj is AlwaysEqual;

        public override int GetHashCode() => 0;

        // The operators ignore their operands: a guard that asked them whether the member is there would be told yes.
        // ReSharper disable UnusedParameter.Local
        public static bool operator ==(AlwaysEqual? left, AlwaysEqual? right) => true;

        public static bool operator !=(AlwaysEqual? left, AlwaysEqual? right) => false;
        // ReSharper restore UnusedParameter.Local
    }

    // A hand-written value object whose operators assume both operands are set.
    private sealed class Strict
    {
        public string Code { get; init; } = "";

        public static bool operator ==(Strict left, Strict right) => left.Code == right.Code;

        public static bool operator !=(Strict left, Strict right) => !(left == right);

        public override bool Equals(object? obj) => obj is Strict other && other.Code == Code;

        public override int GetHashCode() => Code.GetHashCode(StringComparison.Ordinal);
    }

    private readonly struct Gauge
    {
        public int Level { get; init; }

        // The operators throw without reading their operands, so a guard that called them fails the test.
        // ReSharper disable UnusedParameter.Local
        public static bool operator ==(Gauge left, Gauge right) => throw new InvalidOperationException("== was called.");

        public static bool operator !=(Gauge left, Gauge right) => throw new InvalidOperationException("!= was called.");
        // ReSharper restore UnusedParameter.Local

        public override bool Equals(object? obj) => obj is Gauge other && other.Level == Level;

        public override int GetHashCode() => Level;
    }
}