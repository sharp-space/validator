namespace Sharp.Validator.Tests.Validators;

// Equal follows equality semantics for null instead of the skip-null convention: null == null passes, null vs non-null
// fails. Previously a null value passed against any target — a confirmation field (Equal(x => x.Password)) silently
// passed when left null. The {TargetValue} placeholder is now always substituted ("null" for a null target) instead of
// leaking the raw placeholder into the message. The default message names a property target rather than print its
// value, so these rules ask for the value explicitly.
//
// A comparer passed to Equal or NotEqual decides for null too, as IEqualityComparer<T> lets it, and NotEqual fails
// exactly where Equal passes. Equal used to bypass the comparer when one side was null while NotEqual asked it, so a
// comparer that treats null and "" alike made both rules fail for "" against a null target.
public sealed class EqualNullSemanticsTests
{
    private const string ValueMessage = "'{PropertyName}' must match '{TargetValue}'.";

    private static Validator<Person> NameEqualsEmail() =>
        new(x => x.Property(person => person.Name).Equal(person => person.Email, options => options.ErrorMessage = ValueMessage));

    [Fact]
    public void NullValue_AgainstNonNullTarget_Fails()
    {
        var result = NameEqualsEmail().Validate(new Person { Name = null, Email = "x" });
        Assert.False(result.IsValid);
        Assert.Contains("'x'", result.Errors[0].Message);
    }

    [Fact]
    public void NullValue_AgainstNullTarget_Passes() => Assert.True(NameEqualsEmail().Validate(new Person { Name = null, Email = null }).IsValid);

    [Fact]
    public void NonNullValue_AgainstNullTarget_FailsWithSubstitutedPlaceholder()
    {
        var result = NameEqualsEmail().Validate(new Person { Name = "x", Email = null });
        Assert.False(result.IsValid);
        Assert.DoesNotContain("{TargetValue}", result.Errors[0].Message);
        Assert.Contains("'null'", result.Errors[0].Message);
    }

    [Fact]
    public void NotEqual_NullAgainstNullFailure_SubstitutesPlaceholder()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .NotEqual(person => person.Email, options => options.ErrorMessage = ValueMessage));

        var result = validator.Validate(new Person { Name = null, Email = null });
        Assert.False(result.IsValid);
        Assert.DoesNotContain("{TargetValue}", result.Errors[0].Message);
        Assert.Contains("'null'", result.Errors[0].Message);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData("", null)]
    [InlineData("", "")]
    [InlineData("a", "b")]
    [InlineData("a", "a")]
    public void EqualAndNotEqual_WithComparer_AreComplementary(string? value, string? target)
    {
        var comparer = new NullAsEmptyComparer();
        var equal = new Validator<Person>(x => x.Property(person => person.Name).Equal(target, comparer));
        var notEqual = new Validator<Person>(x => x.Property(person => person.Name).NotEqual(target, comparer));
        var model = new Person { Name = value };
        Assert.Equal(comparer.Equals(target, value), equal.Validate(model).IsValid);
        Assert.NotEqual(equal.Validate(model).IsValid, notEqual.Validate(model).IsValid);
    }

    [Fact]
    public void Comparer_DecidesNullAgainstAPropertyTargetToo()
    {
        var comparer = new NullAsEmptyComparer();
        var equal = new Validator<Person>(x => x.Property(person => person.Name).Equal(person => person.Email, comparer));
        var notEqual = new Validator<Person>(x => x.Property(person => person.Name).NotEqual(person => person.Email, comparer));
        var model = new Person { Name = "", Email = null };
        Assert.True(equal.Validate(model).IsValid);
        Assert.False(notEqual.Validate(model).IsValid);
    }

    private sealed class NullAsEmptyComparer : IEqualityComparer<string?>
    {
        public bool Equals(string? x, string? y) => string.Equals(x ?? "", y ?? "", StringComparison.Ordinal);

        public int GetHashCode(string? obj) => StringComparer.Ordinal.GetHashCode(obj ?? "");
    }
}