namespace Sharp.Validator.Tests.Validators;

public sealed class NullEmptyTests
{
    [Fact]
    public void Null_PassesOnlyWhenValueIsNull()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Null());
        Assert.True(validator.Validate(new Person { Name = null }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "x" }).IsValid);
    }

    [Fact]
    public void NotNull_FailsOnlyWhenValueIsNull()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotNull());
        Assert.False(validator.Validate(new Person { Name = null }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "x" }).IsValid);
    }

    [Fact]
    public void Empty_StringWhitespaceTreatedAsEmpty()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Empty());
        Assert.True(validator.Validate(new Person { Name = null }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "   " }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "x" }).IsValid);
    }

    [Fact]
    public void NotEmpty_StringWhitespaceTreatedAsEmpty()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        Assert.False(validator.Validate(new Person { Name = null }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "   " }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "x" }).IsValid);
    }

    private static IEnumerable<string?> Iterate(params string?[] values)
    {
        foreach (var value in values)
            yield return value;
    }

    [Fact]
    // A List is an ICollection, so null, an empty list and a non-empty list are decided by Count.
    public void NotEmpty_Collection_IsDecidedByCount()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Tags).NotEmpty());
        Assert.False(validator.Validate(new Person { Tags = null }).IsValid);
        Assert.False(validator.Validate(new Person { Tags = new List<string?>() }).IsValid);
        Assert.True(validator.Validate(new Person { Tags = ["a"] }).IsValid);
    }

    [Fact]
    // An iterator has no Count: null, an empty iterator and a non-empty iterator are decided by enumeration.
    public void NotEmpty_Enumerable_IsDecidedByEnumeration()
    {
        var validator = new Validator<SequenceBag>(x => x.Property(sequenceBag => sequenceBag.Items).NotEmpty());
        Assert.False(validator.Validate(new SequenceBag { Items = null }).IsValid);
        Assert.False(validator.Validate(new SequenceBag { Items = Iterate() }).IsValid);
        Assert.True(validator.Validate(new SequenceBag { Items = Iterate("a") }).IsValid);
    }

    [Fact]
    // Empty on a collection (Empty's ICollection / enumerator branches), for completeness.
    public void Empty_OnCollection()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Tags).Empty());
        Assert.True(validator.Validate(new Person { Tags = null }).IsValid);
        Assert.True(validator.Validate(new Person { Tags = [] }).IsValid);
        Assert.False(validator.Validate(new Person { Tags = ["a"] }).IsValid);

        var sequenceValidator = new Validator<SequenceBag>(x => x.Property(sequenceBag => sequenceBag.Items).Empty());
        Assert.True(sequenceValidator.Validate(new SequenceBag { Items = null }).IsValid);
        Assert.True(sequenceValidator.Validate(new SequenceBag { Items = Iterate() }).IsValid);
        Assert.False(sequenceValidator.Validate(new SequenceBag { Items = Iterate("a") }).IsValid);
    }

    [Fact]
    public void Empty_ValueType_DefaultValueIsEmpty()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).Empty());
        Assert.True(validator.Validate(new Person { Age = 0 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 7 }).IsValid);
    }

    [Fact]
    public void NotEmpty_ValueType_DefaultValueFails()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).NotEmpty());
        Assert.False(validator.Validate(new Person { Age = 0 }).IsValid);
        Assert.True(validator.Validate(new Person { Age = 7 }).IsValid);
    }

    // Typed as IEnumerable<string?> and backed by an iterator, so the value is not an ICollection and has no Count.
    private sealed class SequenceBag
    {
        public IEnumerable<string?>? Items { get; init; }
    }
}