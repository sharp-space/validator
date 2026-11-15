namespace Sharp.Validator.Tests.Validators;

// Comparison and range rules on strings must default to ordinal comparison. Comparer<string>.Default is
// culture-sensitive: verdicts (and RangeValidator's ctor bounds check) would vary with the current thread culture and
// diverge from Equal/NotEqual's ordinal default.
public sealed class StringComparisonDefaultTests
{
    [Fact]
    public void GreaterThanString_DefaultsToOrdinal()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).GreaterThan("B"));

        // Ordinal: 'a' (97) > 'B' (66). Culture-aware collation would order "a" before "B" and fail.
        Assert.True(validator.Validate(new Person { Name = "a" }).IsValid);
    }

    [Fact]
    public void InclusiveBetweenString_DefaultsToOrdinal()
    {
        // Ordinal: 'A' (65) < '_' (95), so the bounds are valid. Culture-aware collation sorts '_' before letters and
        // would reject these bounds in the constructor.
        var validator = new Validator<Person>(x => x.Property(person => person.Name).InclusiveBetween("A", "_"));
        Assert.True(validator.Validate(new Person { Name = "K" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "a" }).IsValid);
    }

    [Fact]
    public void ExplicitComparer_StillWins()
    {
        var validator = new Validator<Person>(x =>
            x.Property(person => person.Name).GreaterThan("B", StringComparer.OrdinalIgnoreCase));

        // Ignore-case: "a" vs "B" compares as "a" vs "b" → less than → invalid.
        Assert.False(validator.Validate(new Person { Name = "a" }).IsValid);
    }
}