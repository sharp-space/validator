using System.Linq.Expressions;

namespace Sharp.Validator.Tests.Accessors;

[Collection(GlobalOptions.Name)]
// Covers the nested / nullable cross-property support: selectors that dereference an intermediate member
// (x => x.Policy.Max) now (a) report a dotted path name and (b) skip — rather than throw — when an
// intermediate is null.
public sealed class NestedCrossPropertyTests
{
    private static Validator<Order> MaxAtLeastMin() =>
        new(x => x.Property(order => order.Policy!.Max).GreaterThanOrEqual(order => order.Policy!.Min));

    [Fact]
    public void NestedCrossProperty_PassesWhenInvariantHolds() =>
        Assert.True(MaxAtLeastMin().Validate(new Order { Policy = new() { Max = 10, Min = 5 } }).IsValid);

    [Fact]
    public void NestedCrossProperty_FailsWhenInvariantViolated() =>
        Assert.False(MaxAtLeastMin().Validate(new Order { Policy = new() { Max = 5, Min = 10 } }).IsValid);

    [Fact]
    public void NestedCrossProperty_SkipsWhenIntermediateIsNull() =>
        // Policy == null previously threw NullReferenceException; both the host and target accessors now report
        // "absent", so the rule is skipped and the result is valid.
        Assert.True(MaxAtLeastMin().Validate(new Order { Policy = null }).IsValid);

    [Fact]
    public void NestedHostProperty_SkipsWhenIntermediateIsNull()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Address!.City).NotEmpty());

        // Address == null → skipped (no throw, no error).
        Assert.True(validator.Validate(new Person { Address = null }).IsValid);

        // Address present with an empty City → NotEmpty fires as usual.
        Assert.False(validator.Validate(new Person { Address = new Address { City = "" } }).IsValid);
    }

    [Fact]
    public void NestedElements_SkipWhenIntermediateIsNull()
    {
        var validator = new Validator<Cart>(x => x.Elements(cart => cart.Bag!.Items).NotEmpty());

        // Bag == null → collection accessor returns null → skipped (no throw).
        Assert.True(validator.Validate(new Cart { Bag = null }).IsValid);

        // Bag present with an empty element → NotEmpty fires with the dotted, indexed path.
        var result = validator.Validate(new Cart { Bag = new Bag { Items = ["ok", ""] } });
        Assert.False(result.IsValid);
        Assert.Equal("Bag.Items[1]", result.Errors[0].PropertyName);
    }

    [Fact]
    public void NestedCrossProperty_ReportsDottedPathNameUnderCamelCasePolicy()
    {
        var validator = MaxAtLeastMin();
        validator.Configure(x => x.PropertyNamePolicy = PropertyNamePolicy.CamelCase);
        var result = validator.Validate(new Order { Policy = new() { Max = 5, Min = 10 } });
        Assert.False(result.IsValid);
        Assert.Equal("policy.max", result.Errors[0].PropertyName);
    }

    [Fact]
    public async Task AsyncNestedHost_SkipsWhenIntermediateIsNull()
    {
        // Exercises the async evaluator's nested-host branch (the sync tests above don't).
        var validator = new Validator<Order>(builder =>
            builder.Property(x => x.Policy!.Max).MustAsync(_ => Task.FromResult(false)));

        // Policy == null → nested host skipped → the always-false async rule never runs → valid.
        Assert.True((await validator.ValidateAsync(new Order { Policy = null })).IsValid);

        // Policy present → async rule runs → returns false → invalid.
        Assert.False((await validator.ValidateAsync(new Order { Policy = new() { Max = 5, Min = 10 } })).IsValid);
    }

    [Fact]
    public async Task AsyncNestedElements_SkipWhenIntermediateIsNull()
    {
        var validator = new Validator<Cart>(builder =>
            builder.Elements(x => x.Bag!.Items).MustAsync(x => Task.FromResult(!string.IsNullOrEmpty(x))));

        // Bag == null → collection accessor returns null → skipped → valid.
        Assert.True((await validator.ValidateAsync(new Cart { Bag = null })).IsValid);

        // Bag present with an empty element → async rule fails.
        Assert.False((await validator.ValidateAsync(new Cart { Bag = new Bag { Items = ["ok", ""] } })).IsValid);
    }

    [Fact]
    public void NullableValueSegment_IsElidedFromPropertyName()
    {
        // x => x.NullableDay.Value dereferences a Nullable<T> intermediate. The accessor must be null-safe (skip when
        // absent) AND the ".Value" hop must be elided from the reported name.
        var validator = new Validator<Person>(x => x.Property(person => person.NullableDay!.Value).InEnum());
        validator.Configure(x => x.PropertyNamePolicy = PropertyNamePolicy.CamelCase);

        // null intermediate → skipped (no InvalidOperationException from Nullable.Value).
        Assert.True(validator.Validate(new Person { NullableDay = null }).IsValid);

        // Present but undefined enum → fails; name is "nullableDay", not "nullableDay.value".
        var result = validator.Validate(new Person { NullableDay = (DayOfWeek)99 });
        Assert.False(result.IsValid);
        Assert.Equal("nullableDay", result.Errors[0].PropertyName);
    }

    [Fact]
    // C# writes an int target read as long? as two conversions, Convert(Convert(x, long), long?), and the target check
    // looked through one. Such a target was compiled as written, as a computed one is: a null Limits threw
    // NullReferenceException where a target read as int? skips the rule, and Equal printed the target's value rather
    // than its name. Every widening into a nullable type did the same, as decimal? from int and double? from float do.
    public void WidenedTarget_SkipsWhenIntermediateIsNull()
    {
        var validator = new Validator<Account>(x => x.Property(account => account.Balance)
            .GreaterThanOrEqual(account => account.Limits!.Floor));

        Assert.True(validator.Validate(new Account { Balance = 1 }).IsValid);
        Assert.False(validator.Validate(new Account { Balance = 1, Limits = new Limits { Floor = 2 } }).IsValid);
    }

    [Fact]
    public void WidenedTarget_IsNamedInTheMessage()
    {
        var validator = new Validator<Account>(x => x.Property(account => account.Balance).Equal(account => account.Limits!.Floor));
        var error = Assert.Single(validator.Validate(new Account { Balance = 1, Limits = new Limits { Floor = 7 } }).Errors);
        Assert.Equal("'Balance' must be equal to 'Limits.Floor'.", error.Message);
    }

    [Fact]
    public void WidenedBounds_SkipWhenIntermediateIsNull()
    {
        var validator = new Validator<Account>(x => x.Property(account => account.Balance)
            .InclusiveBetween(account => account.Limits!.Floor, account => account.Limits!.Ceiling));

        Assert.True(validator.Validate(new Account { Balance = 9 }).IsValid);
        Assert.False(validator.Validate(new Account { Balance = 9, Limits = new Limits { Floor = 0, Ceiling = 5 } }).IsValid);
    }

    [Fact]
    // A target that reads a member as another nullable type is now a faithful member chain, compiled once for the
    // process like any other, where it was compiled for each validator. A conversion through a type in between still
    // isn't, into a nullable type or not: the path and the type don't show it, so each such target keeps an accessor
    // of its own.
    public void WidenedTarget_IsCompiledOncePerProcess()
    {
        Assert.Same(NullableTarget(account => account.Limits!.Floor), NullableTarget(account => account.Limits!.Floor));

        Assert.NotSame(
            NullableTarget(account => (short)account.Limits!.Floor),
            NullableTarget(account => (short)account.Limits!.Floor));

        Assert.NotSame(Target(account => (short)account.Limits!.Floor), Target(account => (short)account.Limits!.Floor));
        Assert.Same(DecimalTarget(account => account.Limits!.Floor), DecimalTarget(account => account.Limits!.Floor));
    }

    private static Func<Account, (bool HasValue, decimal? Value)> DecimalTarget(Expression<Func<Account, decimal?>> target) =>
        CrossPropertyTarget<Account, decimal?>.Of(target).Accessor;

    [Fact]
    // A nullable member, or its Value, read as long? through two conversions keys like a single conversion of the same
    // member would: (long) x.Other and x.Other.Value both on long. The cast throws on a null Other where the Value
    // skips, so neither may enter the cache under that key, or whichever validator came first would decide the other's
    // null. The same holds for an int? member widened, (long) x.Small against x.Small.Value. The cache lasts for the
    // process, so each order has a model type of its own.
    public void CastAndValueOfANullableMember_KeepTheirNullBehavior_ValueBuiltFirst()
    {
        var value = new Validator<ValueBuiltFirst>(x => x.Property(m => m.Balance).GreaterThanOrEqual(m => m.Other!.Value));
        var cast = new Validator<ValueBuiltFirst>(x => x.Property(m => m.Balance).GreaterThanOrEqual(m => (long)m.Other!));
        var widenedValue = new Validator<ValueBuiltFirst>(x => x.Property(m => m.Balance).GreaterThanOrEqual(m => m.Small!.Value));
        var widenedCast = new Validator<ValueBuiltFirst>(x => x.Property(m => m.Balance).GreaterThanOrEqual(m => (long)m.Small!));
        var model = new ValueBuiltFirst { Balance = 1 };
        Assert.True(value.Validate(model).IsValid);
        Assert.Throws<InvalidOperationException>(() => cast.Validate(model));
        Assert.True(widenedValue.Validate(model).IsValid);
        Assert.Throws<InvalidOperationException>(() => widenedCast.Validate(model));
    }

    [Fact]
    public void CastAndValueOfANullableMember_KeepTheirNullBehavior_CastBuiltFirst()
    {
        var cast = new Validator<CastBuiltFirst>(x => x.Property(m => m.Balance).GreaterThanOrEqual(m => (long)m.Other!));
        var value = new Validator<CastBuiltFirst>(x => x.Property(m => m.Balance).GreaterThanOrEqual(m => m.Other!.Value));
        var widenedCast = new Validator<CastBuiltFirst>(x => x.Property(m => m.Balance).GreaterThanOrEqual(m => (long)m.Small!));
        var widenedValue = new Validator<CastBuiltFirst>(x => x.Property(m => m.Balance).GreaterThanOrEqual(m => m.Small!.Value));
        var model = new CastBuiltFirst { Balance = 1 };
        Assert.True(value.Validate(model).IsValid);
        Assert.Throws<InvalidOperationException>(() => cast.Validate(model));
        Assert.True(widenedValue.Validate(model).IsValid);
        Assert.Throws<InvalidOperationException>(() => widenedCast.Validate(model));
    }

    private static Func<Account, (bool HasValue, long? Value)> NullableTarget(Expression<Func<Account, long?>> target) =>
        CrossPropertyTarget<Account, long?>.Of(target).Accessor;

    private static Func<Account, (bool HasValue, long Value)> Target(Expression<Func<Account, long>> target) =>
        CrossPropertyTarget<Account, long>.Of(target).Accessor;

    [Fact]
    // The cache key holds the path, the type and whether the outer conversion checks overflow, and a pair counts only
    // when both of its conversions check or neither does. So a target that checks the narrowing keeps throwing on
    // overflow where one over the same member that doesn't wraps.
    public void NarrowingIntoANullable_KeepsItsOverflowCheck()
    {
        var model = new Narrow { Small = 1, Limits = new Limits { Floor = 65_537 } };
        var wrapping = new Validator<Narrow>(x => x.Property(narrow => narrow.Small).Equal(narrow => (short?)narrow.Limits!.Floor));

        var checkedPair = new Validator<Narrow>(x => x.Property(narrow => narrow.Small)
            .Equal(narrow => checked((short?)narrow.Limits!.Floor)));

        var checkedInner = new Validator<Narrow>(x => x.Property(narrow => narrow.Small)
            .Equal(narrow => checked((short)narrow.Limits!.Floor)));

        Assert.True(wrapping.Validate(model).IsValid);
        Assert.Throws<OverflowException>(() => checkedPair.Validate(model));
        Assert.Throws<OverflowException>(() => checkedInner.Validate(model));
    }

    [Fact]
    public void OtherWideningsIntoANullable_SkipWhenIntermediateIsNull()
    {
        var credit = new Validator<Account>(x => x.Property(account => account.Credit).LessThan(account => account.Limits!.Ceiling));
        var share = new Validator<Account>(x => x.Property(account => account.Share).LessThan(account => account.Limits!.Ratio));

        Assert.True(credit.Validate(new Account { Credit = 9 }).IsValid);
        Assert.True(share.Validate(new Account { Share = 9 }).IsValid);
        Assert.False(credit.Validate(new Account { Credit = 9, Limits = new Limits { Ceiling = 5 } }).IsValid);
        Assert.False(share.Validate(new Account { Share = 9, Limits = new Limits { Ratio = 5 } }).IsValid);
    }

    private sealed class Policy
    {
        public decimal Max { get; init; }

        public decimal Min { get; init; }
    }

    private sealed class Order
    {
        public Policy? Policy { get; init; }
    }

    private sealed class Bag
    {
        public List<string?>? Items { get; init; }
    }

    private sealed class Cart
    {
        public Bag? Bag { get; init; }
    }

    private sealed class Limits
    {
        public int Floor { get; init; }

        public int Ceiling { get; init; }

        public float Ratio { get; init; }
    }

    private sealed class Account
    {
        public long? Balance { get; init; }

        public decimal? Credit { get; init; }

        public double? Share { get; init; }

        public Limits? Limits { get; init; }
    }

    private sealed class ValueBuiltFirst
    {
        public long? Balance { get; init; }

        public long? Other { get; init; }

        public int? Small { get; init; }
    }

    private sealed class CastBuiltFirst
    {
        public long? Balance { get; init; }

        public long? Other { get; init; }

        public int? Small { get; init; }
    }

    private sealed class Narrow
    {
        public short? Small { get; init; }

        public Limits? Limits { get; init; }
    }
}