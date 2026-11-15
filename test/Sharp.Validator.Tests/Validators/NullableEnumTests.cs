namespace Sharp.Validator.Tests.Validators;

// The enums are the data under test: InEnum reads their members from the type, so code never names most of them.
// ReSharper disable UnusedMember.Local

// InEnum() on a nullable flags enum converted every value with Convert.ToInt64, which boxed it: 48 B for each value
// that passed, where the enum's own rule reads the bits in place. The nullable form now checks the unwrapped value with
// the enum's rule, so it costs what the enum form costs, in a list too. It must still accept exactly what it accepted:
// a directly constructed EnumValidator<TModel, TEnum?> still converts the old way, so the tests compare the nullable
// form with it and with the enum form, for every underlying width and sign.
public sealed class NullableEnumTests
{
    [Fact]
    public void ValidNullableFlagsValue_AllocatesWhatANonNullableOneDoes()
    {
        var plain = new Validator<Box<Int32Flags>>(x => x.Property(box => box.Value).InEnum());
        var nullable = new Validator<Box<Int32Flags>>(x => x.Property(box => box.Optional).InEnum());
        var model = new Box<Int32Flags> { Value = Int32Flags.A | Int32Flags.Low, Optional = Int32Flags.A | Int32Flags.Low };
        Assert.Equal(AllocatedBytes(plain, model), AllocatedBytes(nullable, model));
    }

    [Fact]
    public void ListOfValidNullableFlagsValues_CostsNothingPerElement()
    {
        var validator = new Validator<Box<Int32Flags>>(x => x.Elements(box => box.Optionals).InEnum());
        Assert.Equal(AllocatedBytes(validator, WithOptionals(2)), AllocatedBytes(validator, WithOptionals(200)));
    }

    [Fact]
    public void NullableFlagsEnum_EveryUnderlyingType_AcceptsDefinedCombinations()
    {
        AssertAccepted<SByteFlags>((0, true), (3, true), (sbyte.MinValue, true), (sbyte.MinValue + 1, true), (4, false), (-1, false));
        AssertAccepted<ByteFlags>((0, true), (0x83, true), (4, false), (0xFF, false));
        AssertAccepted<Int16Flags>((0, true), (short.MinValue + 3, true), (4, false), (-1, false));
        AssertAccepted<UInt16Flags>((0, true), (0x8003, true), (4, false), (0xFFFF, false));
        AssertAccepted<Int32Flags>((0, true), (int.MinValue + 3, true), (4, false), (-1, false));
        AssertAccepted<UInt32Flags>((0, true), (0x8000_0003, true), (4, false), (0xFFFF_FFFF, false));
        AssertAccepted<Int64Flags>((0, true), (long.MinValue + 3, true), (4, false), (-1, false));
        AssertAccepted<UInt64Flags>((0, true), (unchecked((long)((1UL << 63) | 3)), true), (4, false), (-1, false));
    }

    [Fact]
    public void NullableEnum_AliasedFlags_KeepTheirMeaning() =>
        AssertAccepted<Aliased>((0, true), (1, true), (3, true), (4, false));

    [Fact]
    public void NullableEnum_CompositeOnlyFlags_KeepTheirMeaning() =>
        AssertAccepted<CompositeOnly>((0, true), (3, true), (12, true), (15, true), (1, false), (7, false));

    [Fact]
    public void NullableEnum_NegativeMembers_KeepTheirMeaning() =>
        AssertAccepted<Plain>((-1, true), (1, true), (0, false), (2, false));

    [Fact]
    public void Null_Passes()
    {
        var model = new Box<Plain>();
        Assert.True(new Validator<Box<Plain>>(x => x.Property(box => box.Optional).InEnum()).Validate(model).IsValid);

        Assert.True(new Validator<Box<Plain>>(x => x.Property(box => box.Optional).Add<EnumValidator<Box<Plain>, Plain?>>())
            .Validate(model).IsValid);
    }

    [Fact]
    public void Failure_NamesTheValue()
    {
        var validator = new Validator<Box<Plain>>(x => x.Property(box => box.Optional).InEnum());
        var error = Assert.Single(validator.Validate(new Box<Plain> { Optional = (Plain)2 }).Errors);
        Assert.Equal("InEnum", error.Code);
        Assert.Equal("'Optional' has a range of values which does not include '2'.", error.Message);
    }

    // Each case runs through the enum form, the nullable form, and the directly constructed nullable rule.
    private static void AssertAccepted<TEnum>(params (long Raw, bool Accepted)[] cases) where TEnum : struct, Enum
    {
        var plain = new Validator<Box<TEnum>>(x => x.Property(box => box.Value).InEnum());
        var nullable = new Validator<Box<TEnum>>(x => x.Property(box => box.Optional).InEnum());
        var direct = new Validator<Box<TEnum>>(x => x.Property(box => box.Optional).Add<EnumValidator<Box<TEnum>, TEnum?>>());
        foreach (var (raw, accepted) in cases)
        {
            var value = (TEnum)Enum.ToObject(typeof(TEnum), raw);
            var model = new Box<TEnum> { Value = value, Optional = value };
            Assert.Equal((raw, accepted, accepted, accepted),
                (raw, plain.Validate(model).IsValid, nullable.Validate(model).IsValid, direct.Validate(model).IsValid));
        }
    }

    private static Box<Int32Flags> WithOptionals(int count) =>
        new() { Optionals = [.. Enumerable.Range(0, count).Select(index => (Int32Flags?)(Int32Flags)(index % 4))] };

    private static long AllocatedBytes<TModel>(Validator<TModel> validator, TModel model)
    {
        validator.Validate(model);
        var before = GC.GetAllocatedBytesForCurrentThread();
        validator.Validate(model);

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Flags]
    private enum SByteFlags : sbyte
    {
        None = 0,
        A = 1,
        B = 2,
        Low = sbyte.MinValue
    }

    [Flags]
    private enum ByteFlags : byte
    {
        None = 0,
        A = 1,
        B = 2,
        High = 0x80
    }

    [Flags]
    private enum Int16Flags : short
    {
        None = 0,
        A = 1,
        B = 2,
        Low = short.MinValue
    }

    [Flags]
    private enum UInt16Flags : ushort
    {
        None = 0,
        A = 1,
        B = 2,
        High = 0x8000
    }

    [Flags]
    private enum Int32Flags
    {
        None = 0,
        A = 1,
        B = 2,
        Low = int.MinValue
    }

    [Flags]
    private enum UInt32Flags : uint
    {
        None = 0,
        A = 1,
        B = 2,
        High = 0x8000_0000
    }

    [Flags]
    private enum Int64Flags : long
    {
        None = 0,
        A = 1,
        B = 2,
        Low = long.MinValue
    }

    [Flags]
    private enum UInt64Flags : ulong
    {
        None = 0,
        A = 1,
        B = 2,
        High = 1UL << 63
    }

    [Flags]
    private enum Aliased
    {
        None = 0,
        A = 1,
        Alpha = 1,
        B = 2
    }

    [Flags]
    private enum CompositeOnly
    {
        Ab = 3,
        Cd = 12
    }

    private enum Plain : short
    {
        Negative = -1,
        One = 1,
        Uno = 1
    }

    private sealed class Box<TEnum> where TEnum : struct, Enum
    {
        public TEnum Value { get; init; }

        public TEnum? Optional { get; init; }

        public List<TEnum?>? Optionals { get; init; }
    }
}