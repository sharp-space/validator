namespace Sharp.Validator.Tests.Pipeline;

// Include(child, configure) used to discard the parent-prefix defaults wholesale whenever any configure callback was
// supplied — an empty callback silently dropped the "Parent." prefix. Defaults are now prefilled and the callback
// overrides individual options.
public sealed class IncludeConfigureDefaultsTests
{
    private static Validator<Address> Child() =>
        new(x => x.Property(address => address.City).NotEmpty());

    [Fact]
    public void EmptyConfigureCallback_KeepsTheParentPrefix()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Address!).Include(Child(), _ => { }));
        var result = validator.Validate(new Person { Address = new Address() });
        Assert.Equal("Address.City", result.Errors[0].PropertyName);
    }

    [Fact]
    public void Configure_CanStillOptOutOfThePrefix()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Address!)
            .Include(Child(), options => options.IncludeParentPropertyName = false));

        var result = validator.Validate(new Person { Address = new Address() });
        Assert.Equal("City", result.Errors[0].PropertyName);
    }
}