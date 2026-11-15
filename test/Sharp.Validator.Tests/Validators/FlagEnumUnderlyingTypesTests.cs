namespace Sharp.Validator.Tests.Validators;

// The enums are the data under test: InEnum reads their members from the type, so code never names most of them.
// ReSharper disable UnusedMember.Local

// Covers the cached flags path in EnumValidator across underlying integer widths and signedness. Other tests cover int
// (Permissions) and ulong (FlagEnumUInt64Tests); this pins byte, short and long, and a signed sbyte enum with a
// negative high-bit member, whose values EnumValidator reads once, sign-extended, into its Members rather than
// converting them on every call.
public sealed class FlagEnumUnderlyingTypesTests
{
    [Fact]
    public void FlagsEnum_ByteUnderlying_ValidatesDefinedBits()
    {
        var validator = new Validator<Holder>(x => x.Property(holder => holder.Byte).InEnum());
        Assert.True(validator.Validate(new Holder { Byte = ByteFlags.A | ByteFlags.B }).IsValid);
        Assert.True(validator.Validate(new Holder { Byte = ByteFlags.All }).IsValid);
        Assert.True(validator.Validate(new Holder { Byte = ByteFlags.None }).IsValid);
        Assert.False(validator.Validate(new Holder { Byte = (ByteFlags)8 }).IsValid); // bit 3 undefined
    }

    [Fact]
    public void FlagsEnum_ShortUnderlying_ValidatesDefinedBits()
    {
        var validator = new Validator<Holder>(x => x.Property(holder => holder.Short).InEnum());
        Assert.True(validator.Validate(new Holder { Short = ShortFlags.A | ShortFlags.C }).IsValid);
        Assert.True(validator.Validate(new Holder { Short = ShortFlags.None }).IsValid);
        Assert.False(validator.Validate(new Holder { Short = (ShortFlags)8 }).IsValid); // bit 3 undefined
    }

    [Fact]
    public void FlagsEnum_LongUnderlyingWithHighBits_ValidatesDefinedBits()
    {
        var validator = new Validator<Holder>(x => x.Property(holder => holder.Long).InEnum());
        Assert.True(validator.Validate(new Holder { Long = LongFlags.High }).IsValid);
        Assert.True(validator.Validate(new Holder { Long = LongFlags.Low | LongFlags.High }).IsValid);
        Assert.False(validator.Validate(new Holder { Long = (LongFlags)(1L << 50) }).IsValid); // bit 50 undefined
    }

    [Fact]
    public void FlagsEnum_SByteUnderlyingWithNegativeMember_ValidatesDefinedBits()
    {
        var validator = new Validator<Holder>(x => x.Property(holder => holder.SByte).InEnum());
        Assert.True(validator.Validate(new Holder { SByte = SByteFlags.High }).IsValid); // -128 / 0x80
        Assert.True(validator.Validate(new Holder { SByte = SByteFlags.A | SByteFlags.High }).IsValid); // 0x81
        Assert.True(validator.Validate(new Holder { SByte = SByteFlags.None }).IsValid);
        Assert.False(validator.Validate(new Holder { SByte = (SByteFlags)4 }).IsValid); // bit 2 undefined
    }

    [Fact]
    public void FlagsEnum_UShortUnderlying_ValidatesDefinedBits()
    {
        var validator = new Validator<Holder>(x => x.Property(holder => holder.UShort).InEnum());
        Assert.True(validator.Validate(new Holder { UShort = UShortFlags.A | UShortFlags.B }).IsValid);
        Assert.True(validator.Validate(new Holder { UShort = UShortFlags.None }).IsValid);
        Assert.False(validator.Validate(new Holder { UShort = (UShortFlags)8 }).IsValid); // bit 3 undefined
    }

    [Fact]
    public void FlagsEnum_UIntUnderlying_ValidatesDefinedBits()
    {
        var validator = new Validator<Holder>(x => x.Property(holder => holder.UInt).InEnum());
        Assert.True(validator.Validate(new Holder { UInt = UIntFlags.A | UIntFlags.C }).IsValid);
        Assert.True(validator.Validate(new Holder { UInt = UIntFlags.None }).IsValid);
        Assert.False(validator.Validate(new Holder { UInt = (UIntFlags)8 }).IsValid); // bit 3 undefined
    }

    [Fact]
    public void FlagsZero_WithoutANamedMember_IsTheValidEmptyCombination()
    {
        var validator = new Validator<FlagModel>(x => x.Property(m => m.Flags).InEnum());
        Assert.True(validator.Validate(new FlagModel { Flags = 0 }).IsValid);
    }

    [Flags]
    private enum ByteFlags : byte
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4,
        All = A | B | C
    }

    [Flags]
    private enum ShortFlags : short
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4
    }

    [Flags]
    private enum LongFlags : long
    {
        None = 0,
        Low = 1,
        Mid = 1L << 40,
        High = 1L << 62
    }

    [Flags]
    // Signed underlying with a member whose value is negative (high bit set): High = -128 == 0x80.
    private enum SByteFlags : sbyte
    {
        None = 0,
        A = 1,
        B = 2,
        High = -128
    }

    private sealed class Holder
    {
        public ByteFlags Byte { get; init; }

        public ShortFlags Short { get; init; }

        public LongFlags Long { get; init; }

        public SByteFlags SByte { get; init; }

        public UShortFlags UShort { get; init; }

        public UIntFlags UInt { get; init; }
    }

    [Flags]
    private enum UShortFlags : ushort
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4
    }

    [Flags]
    private enum UIntFlags : uint
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4
    }

    [Flags]
    private enum FlagsWithoutZero
    {
        A = 1,
        B = 2
    }

    private sealed class FlagModel
    {
        public FlagsWithoutZero Flags { get; init; }
    }
}