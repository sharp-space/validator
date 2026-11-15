using System.Reflection;
using System.Reflection.Emit;

namespace Sharp.Validator.Tests.Validators;

// The enums are the data under test: InEnum reads their members from the type, so code never names most of them.
// ReSharper disable UnusedMember.Local

// InEnum() checked a non-flags enum against a FrozenSet of its members. An enum whose members lie within 64 values of
// the smallest is now checked against a mask, and a flags enum that gives every bit a member of its own with one AND;
// the rest keep the set and the member-by-member check. These pin, for each kind of enum, that exactly its members, or
// for flags their combinations, still pass, including values far outside the members and at the underlying type's
// limits. An enum over bool, float or double, which only IL can declare, is refused when its rule is built: its values
// aren't the integers the checks compare.
public sealed class EnumMembershipTests
{
    [Fact]
    public void InEnum_DenseMembers_AcceptsExactlyTheMembers() =>
        AssertAccepted<Dense>(
            (0, true), (1, true), (2, true), (3, false),
            (-1, false), (63, false), (64, false),
            (int.MaxValue, false), (int.MinValue, false));

    [Fact]
    public void InEnum_MembersFromANegativeValue_AcceptsExactlyTheMembers() =>
        AssertAccepted<FromNegative>(
            (-2, true), (-1, true), (0, true), (
                1, true), (-3, false), (2, false),
            (short.MinValue, false), (short.MaxValue, false));

    [Fact]
    public void InEnum_SparseMembersWithin64_AcceptsExactlyTheMembers() =>
        AssertAccepted<Sparse>(
            (10, true), (40, true), (73, true),
            (9, false), (11, false), (72, false),
            (74, false), (10 + 64, false), (10 - 64, false));

    [Fact]
    public void InEnum_MembersFurtherApartThan64_AcceptsExactlyTheMembers() =>
        AssertAccepted<Wide>(
            (0, true), (64, true), (1, false),
            (63, false), (65, false), (128, false), (-64, false));

    [Fact]
    public void InEnum_SignedLimits_AcceptsExactlyTheMembers()
    {
        AssertAccepted<SByteEnds>(
            (sbyte.MinValue, true), (sbyte.MaxValue, true), (0, false), (-1, false));

        AssertAccepted<SByteLow>(
            (sbyte.MinValue, true), (sbyte.MinValue + 1, true), (sbyte.MaxValue, false),
            (0, false), (-1, false));

        AssertAccepted<Int64Low>(
            (long.MinValue, true), (long.MinValue + 1, true), (long.MaxValue, false),
            (0, false), (-1, false));
    }

    [Fact]
    public void InEnum_UInt64Limits_AcceptsExactlyTheMembers()
    {
        AssertAccepted<UInt64High>(
            (-1, true), (-2, true), (-3, false),
            (0, false), (1, false), (long.MaxValue, false));

        AssertAccepted<UInt64Ends>(
            (0, true), (-1, true), (1, false),
            (-2, false), (long.MinValue, false));
    }

    [Fact]
    public void InEnum_AliasedMembers_AcceptsEachValueOnce() =>
        AssertAccepted<Aliased>((1, true), (2, true), (0, false), (3, false));

    [Fact]
    public void InEnum_EnumWithoutMembers_AcceptsNoValue() =>
        AssertAccepted<Empty>((0, false), (1, false), (-1, false));

    [Fact]
    public void InEnum_FlagsWithABitPerMember_AcceptsTheirCombinations() =>
        AssertAccepted<Bits>((0, true), (1, true), (3, true),
            (7, true), (8, false), (9, false), (-1, false));

    [Fact]
    public void InEnum_UInt64FlagsWithoutABitPerMember_AcceptsTheirCombinations() =>
        AssertAccepted<UInt64Composite>((0, true), (3, true), (long.MinValue, true), (long.MinValue + 3, true),
            (1, false), (2, false), (long.MinValue + 1, false), (-1, false));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // C# can't declare an enum over char, but IL can, and the runtime reads one like any other. Its value is read by
    // size, as a ushort.
    public void InEnum_EnumOverChar_AcceptsExactlyTheMembers(bool flags)
    {
        var enumType = flags
            ? DefineEnum(typeof(char), true, '\0', (char)1, (char)2)
            : DefineEnum(typeof(char), false, 'a', 'b', 'c');

        var accepts = typeof(EnumMembershipTests).GetMethod(nameof(Accepts), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(enumType);

        if (flags) Assert.Equal((true, true, true, false, false), (Accepted(0), Accepted(1), Accepted(3), Accepted(4), Accepted(0xFF01)));
        else Assert.Equal((true, true, false, false, false), (Accepted('a'), Accepted('c'), Accepted('d'), Accepted(0), Accepted(0xFF61)));

        return;

        bool Accepted(long raw) => (bool)accepts.Invoke(null, [raw])!;
    }

    [Theory]
    [InlineData(typeof(bool), false, true)]
    [InlineData(typeof(float), 0f, 1f)]
    [InlineData(typeof(double), 0d, 1d)]
    public void InEnum_EnumOverANonIntegerType_IsRefusedWhenItsRuleIsBuilt(Type underlying, object first, object second)
    {
        var build = typeof(EnumMembershipTests).GetMethod(nameof(Build), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(DefineEnum(underlying, false, first, second));

        Assert.Contains("over an integer type", Assert.Throws<InvalidOperationException>(() => Built(false)).Message);
        Assert.Contains("over an integer type", Assert.Throws<InvalidOperationException>(() => Built(true)).Message);

        return;

        void Built(bool nullable) => build.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [nullable], null);
    }

    private static void AssertAccepted<TEnum>(params (long Raw, bool Accepted)[] cases) where TEnum : struct, Enum
    {
        var validator = new Validator<Box<TEnum>>(x => x.Property(box => box.Value).InEnum());
        foreach (var (raw, accepted) in cases)
        {
            var model = new Box<TEnum> { Value = (TEnum)Enum.ToObject(typeof(TEnum), raw) };
            Assert.Equal((raw, accepted), (raw, validator.Validate(model).IsValid));
        }
    }

    private static bool Accepts<TEnum>(long raw) where TEnum : struct, Enum =>
        new Validator<Box<TEnum>>(x => x.Property(box => box.Value).InEnum())
            .Validate(new Box<TEnum> { Value = (TEnum)Enum.ToObject(typeof(TEnum), raw) }).IsValid;

    private static object Build<TEnum>(bool nullable) where TEnum : struct, Enum =>
        nullable ? new NullableEnumValidator<Box<TEnum>, TEnum>() : new EnumValidator<Box<TEnum>, TEnum>();

    // Each call defines a new enum in an assembly of its own, named after its underlying type.
    private static Type DefineEnum(Type underlying, bool flags, params object[] values)
    {
        var name = $"{underlying.Name}{(flags ? "Flags" : "Members")}{Guid.NewGuid():N}";
        var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run)
            .DefineDynamicModule(name)
            .DefineEnum(name, TypeAttributes.Public, underlying);

        if (flags)
            builder.SetCustomAttribute(new CustomAttributeBuilder(typeof(FlagsAttribute).GetConstructor(Type.EmptyTypes)!, []));

        for (var index = 0; index < values.Length; index++)
            builder.DefineLiteral($"Member{index}", values[index]);

        return builder.CreateType();
    }

    private enum Dense
    {
        A,
        B,
        C
    }

    private enum FromNegative : short
    {
        MinusTwo = -2,
        MinusOne = -1,
        Zero = 0,
        One = 1
    }

    private enum Sparse
    {
        First = 10,
        Middle = 40,
        Last = 73
    }

    private enum Wide
    {
        Low = 0,
        High = 64
    }

    private enum SByteEnds : sbyte
    {
        Min = sbyte.MinValue,
        Max = sbyte.MaxValue
    }

    private enum SByteLow : sbyte
    {
        Min = sbyte.MinValue,
        AboveMin = sbyte.MinValue + 1
    }

    private enum Int64Low : long
    {
        Min = long.MinValue,
        AboveMin = long.MinValue + 1
    }

    private enum UInt64High : ulong
    {
        BelowMax = ulong.MaxValue - 1,
        Max = ulong.MaxValue
    }

    private enum UInt64Ends : ulong
    {
        Zero = 0,
        Max = ulong.MaxValue
    }

    private enum Aliased
    {
        A = 1,
        Alias = 1,
        B = 2
    }

    private enum Empty;

    [Flags]
    private enum Bits
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4,
        All = A | B | C
    }

    [Flags]
    private enum UInt64Composite : ulong
    {
        Ab = 3,
        High = 1UL << 63
    }

    private sealed class Box<TEnum> where TEnum : struct, Enum
    {
        public TEnum Value { get; init; }
    }
}