namespace Sharp.Validator.Tests.Validators;

// InEnum() used to accept any property type and throw when the validator was built, so
// x.Property(m => m.Number).InEnum() compiled. It is now declared only for an enum and a nullable enum, so that call no
// longer compiles. A rule constructed directly over a non-enum type still throws.
public sealed class EnumNonEnumTests
{
    [Fact]
    public void RuleConstructedOverANonEnumType_Throws() => Assert.Throws<InvalidOperationException>(() => new EnumValidator<NonEnumModel, int>());

    [Fact]
    public void InEnum_OnEnumProperty_ValidatesCorrectly()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Day).InEnum());
        Assert.True(validator.Validate(new Person { Day = DayOfWeek.Monday }).IsValid);
        Assert.False(validator.Validate(new Person { Day = (DayOfWeek)42 }).IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(DayOfWeek.Friday, true)]
    [InlineData((DayOfWeek)42, false)]
    public void InEnum_NullableEnumProperty_ValidatesOnlyDefinedValues(DayOfWeek? day, bool valid)
    {
        var validator = new Validator<Person>(x => x.Property(person => person.NullableDay).InEnum());
        Assert.Equal(valid, validator.Validate(new Person { NullableDay = day }).IsValid);
    }

    [Fact]
    public void InEnum_SupportsFlagsEnumCompositions()
    {
        var model = new FlagsModel { Perms = Permissions.Read | Permissions.Write };

        var validator = new Validator<FlagsModel>(x => x.Property(flagsModel => flagsModel.Perms).InEnum());
        Assert.True(validator.Validate(model).IsValid);

        // 0 is None — defined value
        Assert.True(validator.Validate(new FlagsModel { Perms = Permissions.None }).IsValid);

        // 8 is outside all defined bits
        Assert.False(validator.Validate(new FlagsModel { Perms = (Permissions)8 }).IsValid);
    }

    [Fact]
    // Targets EnumStringValidator ctor guards: a null type and a non-enum type.
    public void InEnumString_WithNullOrNonEnumType_Throws()
    {
        // Cast disambiguates from the generic InEnum(Action?) overload so the Type overload is selected.
        Assert.Throws<ArgumentNullException>(() =>
            new Validator<Person>(x => x.Property(person => person.Name).InEnum(null!)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator<Person>(x => x.Property(person => person.Name).InEnum(typeof(int))));
    }

    private sealed class NonEnumModel;

    private sealed class FlagsModel
    {
        public Permissions Perms { get; init; }
    }
}