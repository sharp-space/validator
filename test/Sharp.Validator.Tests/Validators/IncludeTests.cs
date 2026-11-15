namespace Sharp.Validator.Tests.Validators;

public sealed class IncludeTests
{
    [Fact]
    public void Include_AggregatesChildErrorsWithDottedPropertyName()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Address!).Include(new AddressValidator()));

        var result = validator.Validate(new Person { Address = new Address() });
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, x => x.PropertyName == "Address.City");
        Assert.Contains(result.Errors, x => x.PropertyName == "Address.Street");
    }

    [Fact]
    public void Include_SkipsWhenPropertyIsNull()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Address!).Include(new AddressValidator()));
        var result = validator.Validate(new Person { Address = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task IncludeAsync_WhenChildIsAsync_AggregatesChildErrors()
    {
        var asyncChild = new Validator<Address>(builder => builder
            .Property(x => x.City)
            .MustAsync(async (value, _) =>
            {
                await Task.Yield();
                return !string.IsNullOrEmpty(value);
            }));

        var validator = new Validator<Person>(x => x.Property(person => person.Address!).Include(asyncChild));
        var result = await validator.ValidateAsync(new Person { Address = new Address { City = "" } });
        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);
        Assert.Equal("Address.City", item.PropertyName);
    }

    private sealed class AddressValidator() : Validator<Address>(builder =>
    {
        builder.Property(x => x.City).NotEmpty();
        builder.Property(x => x.Street).NotEmpty();
    });
}