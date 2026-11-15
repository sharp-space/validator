namespace Sharp.Validator.Tests.Validators;

public sealed class RangeComparerTests
{
    [Fact]
    public void InclusiveBetween_SwappedBoundsWithDefaultComparer_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator<Person>(x => x.Property(person => person.Age).InclusiveBetween(100, 1)));

    [Fact]
    public void InclusiveBetween_SwappedBoundsWithCustomComparer_ThrowsForArbitraryNegatives() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator<Person>(x => x.Property(person => person.Age).InclusiveBetween(100, 1, new WeirdComparer())));

    [Fact]
    public void InclusiveBetween_EqualBounds_AreAllowed()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).InclusiveBetween(5, 5));
        Assert.True(validator.Validate(new Person { Age = 5 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 6 }).IsValid);
    }

    // IComparer<T> only guarantees the sign of the return value, not -1/0/1
    private sealed class WeirdComparer : IComparer<int>
    {
        public int Compare(int x, int y) =>
            x < y ? -42 : x > y ? 42 : 0;
    }
}