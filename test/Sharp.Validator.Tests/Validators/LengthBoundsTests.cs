namespace Sharp.Validator.Tests.Validators;

// The length rules read -1 as "no upper bound", so MaximumLength(-1) and an exact Length(-1) accepted any length and
// Length(5, -1) meant "at least 5". A bound is now a length: a negative or reversed constant throws where it is
// declared, and bounds read from the model throw when a validation reads them, because they come from the definition,
// not from the input. MinimumLength says "no upper bound". The password counts follow: 0 turns a sub-rule off, and a
// negative count throws.
public sealed class LengthBoundsTests
{
    public static TheoryData<string, Action<ValidatorBuilder<Person>>> NegativeOrReversedConstants => new()
    {
        { "Length(-1, 5)", x => x.Property(person => person.Name).Length(-1, 5) },
        { "Length(5, 4)", x => x.Property(person => person.Name).Length(5, 4) },
        { "Length(5, -1)", x => x.Property(person => person.Name).Length(5, -1) },
        { "Length(-1)", x => x.Property(person => person.Name).Length(-1) },
        { "MaximumLength(-1)", x => x.Property(person => person.Name).MaximumLength(-1) },
        { "MinimumLength(-1)", x => x.Property(person => person.Name).MinimumLength(-1) }
    };

    public static TheoryData<string, Action<ValidatorBuilder<Person>>> NegativeOrReversedModelBounds => new()
    {
        { "Length(-1, 5)", x => x.Property(person => person.Name).Length(_ => -1, _ => 5) },
        { "Length(5, 4)", x => x.Property(person => person.Name).Length(_ => 5, _ => 4) },
        { "Length(5, -1)", x => x.Property(person => person.Name).Length(_ => 5, _ => -1) },
        { "Length(-1)", x => x.Property(person => person.Name).Length(_ => -1) },
        { "MaximumLength(-1)", x => x.Property(person => person.Name).MaximumLength(_ => -1) },
        { "MinimumLength(-1)", x => x.Property(person => person.Name).MinimumLength(_ => -1) }
    };

    [Theory]
    [MemberData(nameof(NegativeOrReversedConstants))]
    public void NegativeOrReversedConstantBound_ThrowsWhereItIsDeclared(string rule, Action<ValidatorBuilder<Person>> declare)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Validator<Person>(declare));
        Assert.NotEmpty(rule);
    }

    [Theory]
    [MemberData(nameof(NegativeOrReversedModelBounds))]
    public void NegativeOrReversedBound_FromTheModel_ThrowsWhenValidationReadsIt(
        string rule,
        Action<ValidatorBuilder<Person>> declare
    )
    {
        var validator = new Validator<Person>(declare);
        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(new Person { Name = "abc" }));
        Assert.Contains("length bounds", exception.Message);
        Assert.NotEmpty(rule);
    }

    [Fact]
    public void MinimumLength_HasNoUpperBound()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).MinimumLength(5));
        Assert.True(validator.Validate(new Person { Name = new string('x', 10_000) }).IsValid);
        Assert.Contains("at least 5", Assert.Single(validator.Validate(new Person { Name = "x" }).Errors).Message);
    }

    [Fact]
    public void Password_NegativeCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator<Person>(x => x.Property(person => person.Name).Password(options => options.RequiredDigits = -1)));

        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordDigitCountValidator<Person>(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordLengthValidator<Person>(-1));
    }

    [Fact]
    public void Password_ZeroCount_TurnsTheSubRuleOff()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Password(options =>
        {
            options.RequiredLength = 0;
            options.RequiredDigits = 0;
            options.RequiredNonAlphanumerics = 0;
            options.RequiredLowercaseLetters = 0;
        }));

        Assert.True(validator.Validate(new Person { Name = "A" }).IsValid);
    }
}