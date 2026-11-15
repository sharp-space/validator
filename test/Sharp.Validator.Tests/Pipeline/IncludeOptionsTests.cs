namespace Sharp.Validator.Tests.Pipeline;

public sealed class IncludeOptionsTests
{
    [Fact]
    public void Configure_AfterInclude_PropagatesOptionsToChild()
    {
        var address = new AddressValidator();
        var personValidator = new Validator<Person>(x => x.Property(person => person.Address!).Include(address));
        personValidator.Configure(x => x.PropertyNamePrefix = "PERSON_");

        var result = personValidator.Validate(new Person { Address = new Address() });
        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);
        Assert.Equal("PERSON_Address.City", item.PropertyName);
    }

    [Fact]
    public void Include_DoesNotMutateSharedValidatorOptions()
    {
        var address = new AddressValidator();
        address.Configure(x => x.PropertyNamePrefix = "ADDR_");

        var before = address.Validate(new Address());
        Assert.Equal("ADDR_City", before.Errors[0].PropertyName);

        var personValidator = new Validator<Person>(x => x.Property(person => person.Address!).Include(address));
        personValidator.Configure(x => x.PropertyNamePrefix = "PERSON_");
        personValidator.Validate(new Person { Address = new Address() });

        // standalone call must still see the original prefix
        var after = address.Validate(new Address());
        Assert.Equal("ADDR_City", after.Errors[0].PropertyName);
    }

    [Fact]
    public void Configure_BeforeInclude_AppliesParentPrefixOnce()
    {
        var address = new AddressValidator();
        var personValidator = new PrefixBeforeIncludePersonValidator(address);
        var result = personValidator.Validate(new Person { Address = new Address() });
        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);
        Assert.Equal("PERSON_Address.City", item.PropertyName);
    }

    [Fact]
    public async Task AsyncInclude_AppliesParentPrefixOnce()
    {
        var address = new AsyncAddressValidator();
        var personValidator = new Validator<Person>(x => x.Property(person => person.Address!).Include(address));
        personValidator.Configure(x => x.PropertyNamePrefix = "PERSON_");

        var result = await personValidator.ValidateAsync(new Person { Address = new Address { City = "" } });
        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);
        Assert.Equal("PERSON_Address.City", item.PropertyName);
    }

    [Fact]
    public void Elements_IncludeAppliesParentPrefixOnce()
    {
        var address = new AddressValidator();
        var teamValidator = new Validator<Team>(x => x.Elements(team => team.Addresses!).Include(address));
        teamValidator.Configure(x => x.PropertyNamePrefix = "TEAM_");

        var result = teamValidator.Validate(new Team { Addresses = [new Address()] });
        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);
        Assert.Equal("TEAM_Addresses[0].City", item.PropertyName);
    }

    [Fact]
    // Include with a non-null configure callback.
    public void Property_OptionsOverrideIncludedParentPropertyName()
    {
        var child = new Validator<Address>(x => x.Property(address => address.City).NotEmpty());
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Address!, x => x.PropertyName = "Addr")
            .Include(child));

        var result = validator.Validate(new Person { Address = new Address { City = "" } });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Addr.City");
    }

    private sealed class AddressValidator() : Validator<Address>(x => x.Property(address => address.City).NotEmpty());

    private sealed class AsyncAddressValidator() : Validator<Address>(x => x.Property(address => address.City)
        .MustAsync(city => Task.FromResult(!string.IsNullOrEmpty(city))));

    private sealed class Team
    {
        public List<Address>? Addresses { get; init; }
    }

    private sealed class PrefixBeforeIncludePersonValidator : Validator<Person>
    {
        public PrefixBeforeIncludePersonValidator(Validator<Address> address)
        {
            Configure(options => options.PropertyNamePrefix = "PERSON_");
            Builder.Property(x => x.Address!).Include(address);
        }
    }
}