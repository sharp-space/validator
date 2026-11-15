namespace Sharp.Validator.Tests.Pipeline;

// A rule on the model itself, Property(x => x), and a collection as the root model, Elements(x => x), aren't supported,
// since every error is reported under a property. They failed when declared with "is invalid" and nothing more, as did
// a computed selector. The exception now says what to write instead.
public sealed class SelectorGuidanceTests
{
    [Fact]
    public void Rule_OnTheModelItselfPointsToContextModel()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Booking>(x => x.Property(booking => booking).Must(booking => booking.Start < booking.End)));

        Assert.Contains("booking => booking", exception.Message);
        Assert.Contains("context.Model", exception.Message);
    }

    [Fact]
    // What the message and README point to: the check sits on the property its error belongs to.
    public void Check_AcrossProperties_ReportsUnderThePropertyItSitsOn()
    {
        var validator = new Validator<Booking>(x => x.Property(booking => booking.End).Must((end, context) => end > context.Model.Start,
            options => options.ErrorMessage = "'{PropertyName}' must be after the start."));

        var error = Assert.Single(validator.Validate(new Booking { Start = new DateTime(2026, 1, 2), End = new DateTime(2026, 1, 1) }).Errors);
        Assert.Equal("End", error.PropertyName);
        Assert.Equal("'End' must be after the start.", error.Message);
    }

    [Fact]
    public void ConvertedModel_GetsTheSameGuidance()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Booking>(x => x.Property(booking => (object)booking).NotNull()));

        Assert.Contains("context.Model", exception.Message);
    }

    [Fact]
    public void CollectionAsTheRootModel_PointsToAModelThatHoldsIt()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Validator<List<string>>(x => x.Elements(tags => tags).NotEmpty()));

        Assert.Contains("a model that holds it", exception.Message);
    }

    [Fact]
    public void Rule_OnTheElementItselfPointsToTheElementsChain()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Booking>(x => x.Elements(booking => booking.Tags!).Property(tag => tag).NotEmpty()));

        Assert.Contains("Elements chain", exception.Message);
    }

    [Fact]
    public void ComputedSelector_PointsToAMustOnThePropertyItReads()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Booking>(x => x.Property(booking => booking.Tags!.Count(tag => tag.Length > 0)).GreaterThan(0)));

        Assert.Contains("member chain", exception.Message);
        Assert.Contains("Must", exception.Message);
    }

    [Fact]
    // NewExpression body.
    public void Property_WithNewExpression_ThrowsInvalidOperation() =>
        Assert.Throws<InvalidOperationException>(() =>
            new Validator<Person>(x => x.Property(person => new { person.Name }).NotNull()));

    [Fact]
    // Otherwise-invalid (non-member) body.
    public void Property_WithNonMemberExpression_ThrowsInvalidOperation() =>
        Assert.Throws<InvalidOperationException>(() =>
            new Validator<Person>(x => x.Property(person => person.Name!.Trim()).NotNull()));

    [Fact]
    // A selector is a member chain from the model. One that read through an indexer, a call or a cast, or started at
    // something other than its parameter, ended its path at the last member: x => x.Members[0].Name reported its errors
    // as "Name", merged them with a top-level Name's in ToDictionary(), and IncludeProperty(x => x.Name) ran it. Such a
    // selector throws where it is declared, and the message says what to write instead.
    public void Selector_ThroughAnIndexer_PointsToTheElementsChain()
    {
        var list = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Household>(x => x.Property(home => home.Members![0].Name).NotEmpty()));

        var array = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Household>(x => x.Property(home => home.Owners![0].Name).NotEmpty()));

        Assert.Contains("reads through an indexer", list.Message);
        Assert.Contains("Elements(...).Property(...)", list.Message);
        Assert.Contains("reads through an indexer", array.Message);
    }

    [Fact]
    public void Selector_ThroughAMethodCall_PointsToAMust()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Household>(x => x.Property(home => home.Head()!.Name).NotEmpty()));

        Assert.Contains("reads through a method call", exception.Message);
        Assert.Contains("Must", exception.Message);
    }

    [Fact]
    public void Selector_ThroughACast_PointsToInclude()
    {
        var member = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Household>(x => x.Property(home => ((Member)home.Contact!).Name).NotEmpty()));

        var model = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Household>(x => x.Property(home => ((SharedHousehold)home).Agreement).NotEmpty()));

        Assert.Contains("reads through a cast", member.Message);
        Assert.Contains("Include(...)", member.Message);
        Assert.Contains("casts the model", model.Message);
        Assert.Contains("context.Model", model.Message);
    }

    [Fact]
    // An array's Length is a hop of the path, so a cast under it is a cast in the middle: ((int[]) x.Contact).Length
    // passed as the chain Contact.Length and threw NullReferenceException on a null Contact instead of skipping.
    public void Selector_ThroughACastUnderAnArrayLength_PointsToInclude()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Household>(x => x.Property(home => ((int[])home.Contact!).Length).GreaterThan(0)));

        Assert.Contains("reads through a cast", exception.Message);
    }

    [Fact]
    public void Selector_FromAStaticMember_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Household>(x => x.Property(_ => Household.Fallback.Name).NotEmpty()));

        Assert.Contains("reads a static member", exception.Message);
    }

    [Fact]
    public void Selector_FromACapturedValue_IsRejected()
    {
        var other = new Household { First = new Member { Name = "" } };
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Household>(x => x.Property(_ => other.First!.Name).NotEmpty()));

        Assert.Contains("doesn't start at its parameter", exception.Message);
    }

    [Fact]
    public void Selector_FromAComputedValue_IsRejected()
    {
        var coalesce = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Household>(x => x.Property(home => (home.First ?? home.Second)!.Name).NotEmpty()));

        var condition = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Household>(x => x.Property(home => (home.Shared ? home.First : home.Second)!.Name).NotEmpty()));

        Assert.Contains("doesn't start at its parameter", coalesce.Message);
        Assert.Contains("doesn't start at its parameter", condition.Message);
    }

    [Fact]
    // An Elements chain and the Property chains on its items take the same check.
    public void ElementsAndItemSelectors_ThroughAnIndexer_AreRejected()
    {
        var elements = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Household>(x => x.Elements(home => home.Members![0].Nicknames).NotEmpty()));

        var item = Assert.Throws<InvalidOperationException>(() =>
            new Validator<Household>(x => x.Elements(home => home.Members).Property(member => member.Nicknames![0].Length)
                .GreaterThan(0)));

        Assert.Contains("reads through an indexer", elements.Message);
        Assert.Contains("reads through an indexer", item.Message);
    }

    private sealed class Booking
    {
        public DateTime Start { get; init; }

        public DateTime End { get; init; }

        public List<string>? Tags { get; init; }
    }

    private class Member
    {
        public string? Name { get; init; }

        public List<string>? Nicknames { get; init; }
    }

    private class Household
    {
        public static Member Fallback { get; } = new();

        public List<Member>? Members { get; init; }

        public Member[]? Owners { get; init; }

        public object? Contact { get; init; }

        public Member? First { get; init; }

        public Member? Second { get; init; }

        public bool Shared { get; init; }

        public Member? Head() => First;
    }

    private sealed class SharedHousehold : Household
    {
        public string? Agreement { get; init; }
    }
}