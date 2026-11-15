namespace Sharp.Validator.Tests.Validators;

// Comparison rules on a value type skip the comparer field when it still holds the default, because the default compare
// inlines. A comparer the caller passes must still win.
public sealed class ComparerFastPathTests
{
    [Fact]
    public void ComparerPassedToAComparisonRule_ReplacesTheDefault()
    {
        var reversed = Comparer<int>.Create(static (left, right) => right.CompareTo(left));
        var validator = new Validator<Person>(x => x.Property(person => person.Age).GreaterThan(10, reversed));
        Assert.True(validator.Validate(new Person { Age = 5 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 15 }).IsValid);
    }

    [Fact]
    public void ComparerPassedToARule_ReplacesTheDefault()
    {
        var reversed = Comparer<int>.Create(static (left, right) => right.CompareTo(left));
        var validator = new Validator<Person>(x => x.Property(person => person.Age).InclusiveBetween(10, 1, reversed));
        Assert.True(validator.Validate(new Person { Age = 5 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 11 }).IsValid);
    }

    [Fact]
    // Targets read from the model go through the other constructors, which must record the comparer as well.
    public void ComparerPassed_WithPropertyTargets_ReplacesTheDefault()
    {
        var reversed = Comparer<int>.Create(static (left, right) => right.CompareTo(left));
        var greaterThan = new Validator<Window>(x => x.Property(window => window.Value).GreaterThan(window => window.Low, reversed));
        var between = new Validator<Window>(x =>
            x.Property(window => window.Value).InclusiveBetween(window => window.High, window => window.Low, reversed));

        Assert.True(greaterThan.Validate(new Window { Value = 1, Low = 5 }).IsValid);
        Assert.False(greaterThan.Validate(new Window { Value = 9, Low = 5 }).IsValid);
        Assert.True(between.Validate(new Window { Value = 5, Low = 1, High = 10 }).IsValid);
        Assert.False(between.Validate(new Window { Value = 11, Low = 1, High = 10 }).IsValid);
    }

    private sealed class Window
    {
        public int Value { get; init; }

        public int Low { get; init; }

        public int High { get; init; }
    }
}