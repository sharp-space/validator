using System.Globalization;

namespace Sharp.Validator.Tests.Messages;

[Collection(GlobalOptions.Name)]
// How the reported property path is composed from the name policy, the prefix and suffix, indexers, and includes.
// The culture test changes CultureInfo.CurrentCulture, so the class runs in the serialized collection.
public sealed class PropertyPathNamingTests
{
    [Fact]
    public void NamingAndIncludeMatrix_UsesStructuredIndexedPaths()
    {
        var address = new Validator<Address>(x => x.Property(m => m.City).NotEmpty());
        var dog = new Validator<Dog>(x => x.Property(m => m.Chip).NotEmpty());
        var validator = new Validator<Model>(builder =>
        {
            builder.Property(x => x.Home!).Include(address);
            builder.Elements(x => x.Addresses).Include(address);
            builder.Elements(x => x.Items).Property(x => x.Home!).Include(address);
            builder.Elements(x => x.Pets).Include(dog);
            builder.Elements(x => x.Items).Property(x => x.Pet!).Include(dog);
        });

        validator.Configure(options =>
        {
            options.PropertyNamePolicy = PropertyNamePolicy.CamelCase;
            options.PropertyNamePrefix = "PRE_";
            options.PropertyNameSuffix = "_SUF";
        });

        var result = validator.Validate(new Model
        {
            Home = new Address(),
            Addresses = [new Address()],
            Items = [new Item { Home = new Address(), Pet = new Dog() }],
            Pets = [new Animal(), new Dog()]
        });

        Assert.Equal(
            [
                "PRE_home.City_SUF",
                "PRE_addresses[0].City_SUF",
                "PRE_items[0].home.City_SUF",
                "PRE_pets[1].Chip_SUF",
                "PRE_items[0].pet.Chip_SUF"
            ],
            result.Errors.Select(x => x.PropertyName));
    }

    [Fact]
    public void Nullable_ElementValueElidesTheValueSegment()
    {
        var validator = new Validator<NullableModel>(builder => builder
            .Elements(x => x.Numbers)
            .Property(x => x!.Value)
            .GreaterThan(0));

        var result = validator.Validate(new NullableModel { Numbers = [1, null, -1] });
        Assert.Equal("Numbers[2]", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void TargetPropertyName_UsesPolicyWithoutExternalWrappers()
    {
        var validator = new Validator<Person>(x => x.Property(m => m.Age)
            .GreaterThan(m => m.NullableAge!.Value,
                options => options.ErrorMessage = "{PropertyName} vs {TargetPropertyName}"));

        validator.Configure(options =>
        {
            options.PropertyNamePolicy = PropertyNamePolicy.SnakeCase;
            options.PropertyNamePrefix = "PRE_";
            options.PropertyNameSuffix = "_SUF";
        });

        var error = Assert.Single(validator.Validate(new Person { Age = 1, NullableAge = 2 }).Errors);
        Assert.Equal("PRE_age_SUF vs nullable_age", error.Message);
    }

    [Fact]
    public void NamePolicy_IsInvariantUnderTurkishCulture()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var validator = new Validator<IdModel>(x => x.Property(m => m.ID).NotEmpty());
            validator.Configure(x => x.PropertyNamePolicy = PropertyNamePolicy.SnakeCase);
            Assert.Equal("id", Assert.Single(validator.Validate(new IdModel()).Errors).PropertyName);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private sealed class Model
    {
        public Address? Home { get; init; }

        public List<Address> Addresses { get; init; } = [];

        public List<Item> Items { get; init; } = [];

        public List<Animal> Pets { get; init; } = [];
    }

    private sealed class Item
    {
        public Address? Home { get; init; }

        public Animal? Pet { get; init; }
    }

    private class Animal;

    private sealed class Dog : Animal
    {
        public string? Chip { get; init; }
    }

    private sealed class NullableModel
    {
        public List<int?> Numbers { get; init; } = [];
    }

    private sealed class IdModel
    {
        // ReSharper disable once InconsistentNaming — an all-caps name is the input under test.
        public string? ID { get; init; }
    }
}