namespace Sharp.Validator.Tests.Validators;

// The enums are the data under test: InEnum reads their members from the type, so code never names most of them.
// ReSharper disable UnusedMember.Local

// Regression: [Flags] enums with a UInt64 underlying type and values >= 2^63 previously threw OverflowException because
// the implementation routed through Int64 and called Convert.ToInt64(ulong) — a checked conversion that fails on values
// with the high bit set.
public sealed class FlagEnumUInt64Tests
{
    [Fact]
    public void HighBitValue_IsAcceptedWithoutOverflow()
    {
        var validator = new Validator<Model>(x => x.Property(model => model.Flags).InEnum());
        var result = validator.Validate(new Model { Flags = BigFlags.High });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CombinationIncludingHighBit_IsAccepted()
    {
        var validator = new Validator<Model>(x => x.Property(model => model.Flags).InEnum());
        var result = validator.Validate(new Model { Flags = BigFlags.Low | BigFlags.High });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void UndefinedBits_AreRejected()
    {
        var validator = new Validator<Model>(x => x.Property(model => model.Flags).InEnum());

        // 1 << 5 is not a named value in BigFlags.
        const BigFlags bogus = (BigFlags)((1UL << 63) | (1UL << 5));
        var result = validator.Validate(new Model { Flags = bogus });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void None_IsAccepted()
    {
        var validator = new Validator<Model>(x => x.Property(model => model.Flags).InEnum());
        Assert.True(validator.Validate(new Model { Flags = BigFlags.None }).IsValid);
    }

    [Flags]
    private enum BigFlags : ulong
    {
        None = 0,
        Low = 1UL << 0,
        Mid = 1UL << 30,
        High = 1UL << 63,
        LowAndHigh = Low | High
    }

    private sealed class Model
    {
        public BigFlags Flags { get; init; }
    }
}