namespace Sharp.Validator.Tests.Pipeline;

// Include<TChild> with a TChild derived from the property's type runs the child validator only for values of that type
// and skips every other value. IncludeDerived<TDerived>, which did the same, is gone.
public sealed class IncludeDerivedTypeTests
{
    [Fact]
    public void Include_DerivedTypeValidator_RunsForValuesOfThatType()
    {
        // TChild (Dog) is inferred from the validator argument's Validator<Dog> base.
        Validator<Dog> dogValidator = new DogValidator();
        var validator = new Validator<Owner>(builder => builder
            .Property(x => x.Pet!)
            .Include(dogValidator));

        // Pet is a Dog with an empty Breed -> the derived validator fires.
        var failing = validator.Validate(new Owner { Pet = new Dog { Breed = "" } });
        Assert.False(failing.IsValid);
        var item = Assert.Single(failing.Errors);
        Assert.Equal("Pet.Breed", item.PropertyName);

        // Pet is a Dog with a valid Breed -> passes.
        Assert.True(validator.Validate(new Owner { Pet = new Dog { Breed = "Corgi" } }).IsValid);
    }

    [Fact]
    public void Include_DerivedTypeValidator_SkipsValuesOfAnotherType()
    {
        Validator<Dog> dogValidator = new DogValidator();
        var validator = new Validator<Owner>(builder => builder
            .Property(x => x.Pet!)
            .Include(dogValidator));

        // Pet is a Cat -> "Pet as Dog" is null -> derived validator is skipped.
        Assert.True(validator.Validate(new Owner { Pet = new Cat() }).IsValid);
    }

    private abstract class Animal;

    private sealed class Dog : Animal
    {
        public string? Breed { get; init; }
    }

    private sealed class Cat : Animal;

    private sealed class Owner
    {
        public Animal? Pet { get; init; }
    }

    private sealed class DogValidator() : Validator<Dog>(x => x.Property(dog => dog.Breed).NotEmpty());
}