namespace Sharp.Validator.Tests.Validators;

// A string property's chain is a string chain, so its comparison targets and Equal values take the nullable form:
// comparing a string with a string? property or value needs no '!', which a string? chain never did. This file is part
// of the compile-time gate the warnings-as-errors build keeps. A null target or bound skips an order comparison or
// range, and Equal and NotEqual judge it by equality.
public sealed class NullableTargetTests
{
    [Theory]
    [InlineData("a", false)]
    [InlineData(null, true)]
    public void Equal_NullableValueOnStringChain_JudgesByEquality(string? target, bool fails)
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Name).Equal(target));
        Assert.Equal(fails, !validator.Validate(new Model { Name = "a" }).IsValid);
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData(null, false)]
    public void NotEqual_NullableValueOnStringChain_JudgesByEquality(string? target, bool fails)
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Name).NotEqual(target, StringComparer.Ordinal));
        Assert.Equal(fails, !validator.Validate(new Model { Name = "a" }).IsValid);
    }

    [Theory]
    [InlineData("A", false)]
    [InlineData("b", true)]
    [InlineData(null, true)]
    public void Equal_NullablePropertyOnStringChain_JudgesByEquality(string? nick, bool fails)
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Name).Equal(m => m.Nick, StringComparer.OrdinalIgnoreCase));
        var result = validator.Validate(new Model { Name = "a", Nick = nick });
        Assert.Equal(fails, !result.IsValid);

        if (fails) Assert.Equal("'Name' must be equal to 'Nick'.", Assert.Single(result.Errors).Message);
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData(null, false)]
    public void NotEqual_NullablePropertyOnStringChain_JudgesByEquality(string? nick, bool fails)
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Name).NotEqual(m => m.Nick));
        Assert.Equal(fails, !validator.Validate(new Model { Name = "a", Nick = nick }).IsValid);
    }

    [Theory]
    [InlineData("b", true)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    public void GreaterThan_NullablePropertyOnStringChain_SkipsANullTarget(string? nick, bool fails)
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Name).GreaterThan(m => m.Nick));
        Assert.Equal(fails, !validator.Validate(new Model { Name = "a", Nick = nick }).IsValid);
    }

    [Theory]
    [InlineData("b", "c", true)]
    [InlineData("0", "c", false)]
    [InlineData(null, "0", false)]
    [InlineData("b", null, false)]
    public void InclusiveBetween_NullableBoundsOnStringChain_SkipANullBound(string? from, string? to, bool fails)
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Name).InclusiveBetween(m => m.Nick, m => m.Upper, StringComparer.Ordinal));
        Assert.Equal(fails, !validator.Validate(new Model { Name = "a", Nick = from, Upper = to }).IsValid);
    }

    [Fact]
    public void ComputedNullableTarget_OnStringChain_SkipsANullTarget()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Name).Add(new LessThanValidator<Model, string>(m => m.Nick)));
        Assert.True(validator.Validate(new Model { Name = "b" }).IsValid);
        Assert.False(validator.Validate(new Model { Name = "b", Nick = "a" }).IsValid);
    }

    [Fact]
    public void Equal_StringPropertyOnNullableStringChain_JudgesByEquality()
    {
        var validator = new Validator<Model>(x => x.Property(m => m.Nick).Equal(m => m.Name));
        Assert.False(validator.Validate(new Model { Name = "a" }).IsValid);
        Assert.True(validator.Validate(new Model { Name = "a", Nick = "a" }).IsValid);
    }

    private sealed class Model
    {
        public string Name { get; init; } = "";

        public string? Nick { get; init; }

        public string? Upper { get; init; }
    }
}