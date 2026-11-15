using Sharp.Validator.Bindings;

namespace Sharp.Validator.Tests.Accessors;

// Regression: accessors looked through every top-level unary as if it were a harmless Convert. A nested
// x => !x.Policy.Enabled validated the raw flag, a flat one reused the generated accessor of x => x.Active, and an
// array's Length bypassed the null guard. The unary is part of the selected value, so it must survive the walk.
public sealed class UnarySelectorTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void NestedNot_IsApplied(bool enabled, bool expectedValid)
    {
        var validator = new Validator<Model>(x => x.Property(m => !m.Policy!.Enabled).Equal(true));
        Assert.Equal(expectedValid, validator.Validate(new Model { Policy = new Policy { Enabled = enabled } }).IsValid);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(-5, true)]
    public void NestedNegate_IsApplied(int max, bool expectedValid)
    {
        var validator = new Validator<Model>(x => x.Property(m => -m.Policy!.Max).GreaterThan(0));
        Assert.Equal(expectedValid, validator.Validate(new Model { Policy = new Policy { Max = max } }).IsValid);
    }

    [Fact]
    public void NestedUnary_KeepsTheNullIntermediateSkip()
    {
        var validator = new Validator<Model>(x => x.Property(m => !m.Policy!.Enabled).Equal(true));
        Assert.True(validator.Validate(new Model()).IsValid);
    }

    [Fact]
    public void FlatNot_DoesNotAliasTheOperandGeneratedAccessor()
    {
        AccessorRegistry.RegisterProperty<SeededModel, bool>("Active", static m => m.Active);
        var validator = new Validator<SeededModel>(x => x.Property(m => !m.Active).Equal(true));
        Assert.False(validator.Validate(new SeededModel { Active = true }).IsValid);
    }

    [Fact]
    public void FlatNegate_DoesNotAliasTheOperandGeneratedAccessor()
    {
        AccessorRegistry.RegisterProperty<SeededModel, int>("Amount", static m => m.Amount);
        var validator = new Validator<SeededModel>(x => x.Property(m => -m.Amount).GreaterThan(0));
        Assert.False(validator.Validate(new SeededModel { Amount = 5 }).IsValid);
    }

    [Fact]
    public void NestedAs_YieldsNullForAMismatch()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Policy!.Item as string).NotNull());
        var result = validator.Validate(new Model { Policy = new Policy { Item = 1 } });
        Assert.Equal("Policy.Item", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void NestedCheckedCast_StillOverflows()
    {
        var validator = new Validator<Model>(x => x.Property(m => checked((int)m.Policy!.Big)).GreaterThan(0));
        Assert.Throws<OverflowException>(() => validator.Validate(new Model { Policy = new Policy { Big = long.MaxValue } }));
    }

    [Fact]
    public void ArrayLength_SkipsANullArrayLikeAListCount()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Tags!.Length).GreaterThan(0));
        Assert.True(validator.Validate(new Model()).IsValid);
    }

    [Fact]
    public void ArrayLength_ReportsTheFullPath()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Tags!.Length).GreaterThan(1));
        var result = validator.Validate(new Model { Tags = ["a"] });
        Assert.Equal("Tags.Length", Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, false)]
    [InlineData(2, true)]
    public void NestedArrayLength_ReadsTheArray(int? length, bool expectedValid)
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Policy!.Tags!.Length).GreaterThan(0));
        var tags = length is { } count ? new string[count] : null;
        Assert.Equal(expectedValid, validator.Validate(new Model { Policy = new Policy { Tags = tags } }).IsValid);
    }

    [Fact]
    public void CrossPropertyTarget_KeepsItsUnary()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Minimum).GreaterThan(m => -m.Policy!.Max));
        Assert.False(validator.Validate(new Model { Minimum = -5, Policy = new Policy { Max = 1 } }).IsValid);
    }

    [Fact]
    public void ElementsNestedAs_YieldsNoSequenceForAMismatch()
    {
        var validator = new Validator<Model>(x => x.Elements(m => m.Policy!.Item as IEnumerable<string>).NotEmpty());
        Assert.True(validator.Validate(new Model { Policy = new Policy { Item = 1 } }).IsValid);
    }

    [Fact]
    // C# writes an int read as long? as two conversions, Convert(Convert(x, long), long?). The selector check looked
    // through one and rejected such a chain as computed. A widening still reads the member under it.
    public void NestedWideningIntoANullable_IsTheMemberItReads()
    {
        var validator = new Validator<Model>(x => x.Property<long?>(m => m.Policy!.Max).GreaterThan(0));
        Assert.True(validator.Validate(new Model()).IsValid);
        var error = Assert.Single(validator.Validate(new Model { Policy = new Policy { Max = 0 } }).Errors);
        Assert.Equal("Policy.Max", error.PropertyName);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(-5, true)]
    public void WideningOverAUnary_KeepsTheUnary(int max, bool expectedValid)
    {
        var validator = new Validator<Model>(x => x.Property<long?>(m => -m.Policy!.Max).GreaterThan(0));
        Assert.Equal(expectedValid, validator.Validate(new Model { Policy = new Policy { Max = max } }).IsValid);
    }

    [Fact]
    // One unary on top of the chain is read as the member it applies to. A second one computes a value from it, so the
    // selector is rejected as a computed one is, with the message that points to a Must.
    public void TwoUnaries_AreRejectedAsAComputedSelector()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            // ReSharper disable once DoubleNegationOperator — two unaries are the selector under test.
            new Validator<Model>(x => x.Property(m => !!m.Policy!.Enabled).Equal(true)));

        Assert.Contains("member chain", exception.Message);
        Assert.Contains("Must", exception.Message);
    }

    [Fact]
    // A widening over an array's Length still reads the Length hop, null-safely: Convert(Convert(ArrayLength(x.Tags))).
    public void WideningOverAnArrayLength_ReadsTheLength()
    {
        var validator = new Validator<Model>(x => x.Property<long?>(m => m.Tags!.Length).GreaterThan(1));
        Assert.True(validator.Validate(new Model()).IsValid);
        Assert.Equal("Tags.Length", Assert.Single(validator.Validate(new Model { Tags = ["a"] }).Errors).PropertyName);
    }

    [Fact]
    // A comparison target with a cast under an array's Length stays computed, as it was before conversions were looked
    // through: it is compiled as written and its message prints the value rather than a name.
    public void CrossPropertyTarget_WithACastUnderAnArrayLength_StaysComputed()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Minimum).Equal(m => ((string[])m.Policy!.Item!).Length));
        var error = Assert.Single(validator.Validate(new Model { Minimum = 1, Policy = new Policy { Item = new[] { "a", "b" } } }).Errors);
        Assert.Equal("'Minimum' must be equal to '2'.", error.Message);
    }

    private sealed class Policy
    {
        public bool Enabled { get; init; }

        public int Max { get; init; }

        public long Big { get; init; }

        public object? Item { get; init; }

        public string[]? Tags { get; init; }
    }

    private sealed class Model
    {
        public Policy? Policy { get; init; }

        public string[]? Tags { get; init; }

        public int Minimum { get; init; }
    }

    private sealed class SeededModel
    {
        public bool Active { get; init; }

        public int Amount { get; init; }
    }
}