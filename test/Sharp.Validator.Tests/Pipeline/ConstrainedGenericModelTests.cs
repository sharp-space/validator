namespace Sharp.Validator.Tests.Pipeline;

// Regression: a validator over a generic model constrained to an interface, such as Validator<TModel> where
// TModel : INamed, threw where its chains were declared, since C# writes x => x.Name there as x => ((INamed)x).Name and
// the selector check read that conversion as a cast of the model. Converting the model to a type it already is names
// the same value, so the chain starts at the model and reports under its own path.
public sealed class ConstrainedGenericModelTests
{
    [Fact]
    public void Chain_ThroughTheConstraint_ReportsUnderItsPath()
    {
        var error = Assert.Single(new NamedRules<Person>().Validate(new Person()).Errors);

        Assert.Equal("Name", error.PropertyName);
    }

    [Fact]
    public void NestedChain_ThroughTheConstraint_SkipsANullIntermediate()
    {
        var validator = new NamedRules<Person>();

        Assert.True(validator.Validate(new Person { Name = "Kim" }).IsValid);
        Assert.Equal(
            "Address.City",
            Assert.Single(validator.Validate(new Person { Name = "Kim", Address = new Address() }).Errors).PropertyName);
    }

    [Fact]
    public void Elements_ThroughTheConstraint_ReportsEachElement()
    {
        var model = new Person { Name = "Kim", Tags = ["vip", ""] };

        Assert.Equal("Tags[1]", Assert.Single(new NamedRules<Person>().Validate(model).Errors).PropertyName);
        Assert.True(new NamedRules<Person>().Validate(new Person { Name = "Kim" }).IsValid);
    }

    [Fact]
    public void ValueTypeModel_ThroughTheConstraint_IsValidated()
    {
        var validator = new NamedRules<Pet>();

        Assert.Equal("Name", Assert.Single(validator.Validate(new Pet()).Errors).PropertyName);
        Assert.True(validator.Validate(new Pet { Name = "Coco" }).IsValid);
    }

    [Fact]
    public void PartialValidation_SelectsAChainThroughTheConstraint()
    {
        var model = new Person { Address = new Address() };

        var result = new NamedRules<Person>().Validate(model, x => x.IncludeProperty(person => person.Address!.City));

        Assert.Equal("Address.City", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void ExplicitConversion_ToATypeTheModelIs_NamesTheSameChain()
    {
        var validator = new Validator<Person>(x => x.Property(person => ((INamed)person).Name).NotEmpty());

        Assert.Equal("Name", Assert.Single(validator.Validate(new Person()).Errors).PropertyName);
    }

    [Fact]
    public void ConversionToATypeTheModelIsNot_StillCastsTheModel()
    {
        var conversion = Assert.Throws<InvalidOperationException>(() =>
            new Validator<object>(x => x.Property(model => ((Person)model).Name).NotEmpty()));
        var typeAs = Assert.Throws<InvalidOperationException>(() =>
            new Validator<object>(x => x.Property(model => (model as Person)!.Name).NotEmpty()));

        Assert.Contains("casts the model", conversion.Message);
        Assert.Contains("casts the model", typeAs.Message);
    }

    private sealed class NamedRules<TModel> : Validator<TModel> where TModel : INamed
    {
        public NamedRules()
        {
            Builder.Property(x => x.Name).NotEmpty();
            Builder.Property(x => x.Address!.City).NotEmpty();
            Builder.Elements(x => x.Tags).NotEmpty();
        }
    }

    private interface INamed
    {
        string? Name { get; }

        Address? Address { get; }

        IReadOnlyList<string>? Tags { get; }
    }

    private sealed class Person : INamed
    {
        public string? Name { get; init; }

        public Address? Address { get; init; }

        public IReadOnlyList<string>? Tags { get; init; }
    }

    private struct Pet : INamed
    {
        public string? Name { get; init; }

        public Address? Address { get; init; }

        public IReadOnlyList<string>? Tags { get; init; }
    }

    private sealed class Address
    {
        public string? City { get; init; }
    }
}
