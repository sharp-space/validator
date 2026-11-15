using Microsoft.Extensions.DependencyInjection;
using Sharp.Validator.Bindings;

namespace Sharp.Validator.Tests.Pipeline;

[Collection(GlobalOptions.Name)]
// Regression: Global and scan wrappers reach every validator, and Include wraps merged child errors with the parent's
// wrappers, so one error came back as "[App] [App] … (!) (!)" and "p_Address.p_City_s_s". Ambient wrappers belong to
// the validation as a whole and apply once at the root. Wrappers from a validator's own Configure are that validator's.
public sealed class IncludeAmbientWrapperTests
{
    private static ValidatorBuilderOptions Ambient() => new()
    {
        PropertyNamePrefix = "p_",
        PropertyNameSuffix = "_s",
        ErrorMessagePrefix = "[App] ",
        ErrorMessageSuffix = " (!)"
    };

    private static T WithGlobal<T>(Func<T> test)
    {
        var snapshot = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = Ambient();
            return test();
        }
        finally
        {
            ValidatorBuilderOptions.Global = snapshot;
        }
    }

    [Fact]
    public void GlobalWrappers_ApplyOnceThroughInclude()
    {
        var error = WithGlobal(() =>
        {
            var parent = new Validator<Person>(x => x.Property(m => m.Address!).Include(new AmbientChild()));
            return Assert.Single(parent.Validate(new Person { Address = new Address() }).Errors);
        });

        Assert.Equal("p_Address.City_s", error.PropertyName);
        Assert.Equal("[App] 'City' must not be empty. (!)", error.Message);
    }

    [Fact]
    public async Task GlobalWrappers_ApplyOnceThroughAsyncInclude()
    {
        var child = new Validator<Address>(x => x.Property(m => m.City).MustAsync(_ => Task.FromResult(false)));
        var error = await WithGlobal(async () =>
        {
            var parent = new Validator<Person>(x => x.Property(m => m.Address!).Include(child));
            return Assert.Single((await parent.ValidateAsync(new Person { Address = new Address { City = "x" } })).Errors);
        });

        Assert.Equal("p_Address.City_s", error.PropertyName);
        Assert.StartsWith("[App] 'City'", error.Message);
    }

    [Fact]
    public void GlobalWrappers_ApplyOnceAcrossSeveralIncludeLevels()
    {
        var error = WithGlobal(() =>
        {
            var person = new Validator<Person>(x => x.Property(m => m.Address!).Include(new AmbientChild()));
            var holder = new Validator<Holder>(x => x.Property(m => m.Person!).Include(person));
            return Assert.Single(holder.Validate(new Holder { Person = new Person { Address = new Address() } }).Errors);
        });

        Assert.Equal("p_Person.Address.City_s", error.PropertyName);
        Assert.Equal("[App] 'City' must not be empty. (!)", error.Message);
    }

    [Fact]
    public void GlobalWrappers_ApplyOnceThroughAnElementsInclude()
    {
        var error = WithGlobal(() =>
        {
            var holder = new Validator<Holder>(x => x.Elements(m => m.Addresses!).Include(new AmbientChild()));
            return Assert.Single(holder.Validate(new Holder { Addresses = [new Address()] }).Errors);
        });

        Assert.Equal("p_Addresses[0].City_s", error.PropertyName);
    }

    [Fact]
    public void StandaloneChild_KeepsTheAmbientWrappers()
    {
        var error = WithGlobal(() => Assert.Single(new AmbientChild().Validate(new Address()).Errors));
        Assert.Equal("p_City_s", error.PropertyName);
        Assert.Equal("[App] 'p_City_s' must not be empty. (!)", error.Message);
    }

    [Fact]
    public void ValidatingTheChildAlone_AfterInclude_KeepsTheAmbientWrappers()
    {
        var error = WithGlobal(() =>
        {
            var child = new AmbientChild();
            var parent = new Validator<Person>(x => x.Property(m => m.Address!).Include(child));
            parent.Validate(new Person { Address = new Address() });

            return Assert.Single(child.Validate(new Address()).Errors);
        });

        Assert.Equal("p_City_s", error.PropertyName);
    }

    [Fact]
    public void ScanWrappers_ApplyOnceThroughInclude()
    {
        var defaults = new ValidatorBuilderOptions { PropertyNamePrefix = "s_", ErrorMessagePrefix = "[S] " };
        IServiceCollection services = new ServiceCollection();

        services.Add(ValidatorRegistry.CreateDescriptor(
            typeof(IValidator<Address>), typeof(AmbientChild), ServiceLifetime.Singleton, defaults));

        services.Add(ValidatorRegistry.CreateDescriptor(
            typeof(IValidator<Person>), typeof(AmbientParent), ServiceLifetime.Singleton, defaults));

        using var provider = services.BuildServiceProvider();
        var result = provider.GetRequiredService<IValidator<Person>>().Validate(new Person { Address = new Address() });
        var error = Assert.Single(result.Errors);
        Assert.Equal("s_Address.City", error.PropertyName);
        Assert.Equal("[S] 'City' must not be empty.", error.Message);
    }

    [Fact]
    public void WrappersFromConfigure_StayOnEachValidator()
    {
        var child = new AmbientChild();
        child.Configure(x =>
        {
            x.PropertyNamePrefix = "ADDR_";
            x.ErrorMessagePrefix = "[Child] ";
        });

        var parent = new Validator<Person>(x => x.Property(m => m.Address!).Include(child));
        parent.Configure(x =>
        {
            x.PropertyNamePrefix = "PERSON_";
            x.ErrorMessagePrefix = "[Parent] ";
        });

        var error = Assert.Single(parent.Validate(new Person { Address = new Address() }).Errors);
        Assert.Equal("PERSON_Address.ADDR_City", error.PropertyName);
        Assert.Equal("[Parent] [Child] 'ADDR_City' must not be empty.", error.Message);
    }

    public sealed class Holder
    {
        public Person? Person { get; init; }

        public List<Address>? Addresses { get; init; }
    }

    public sealed class AmbientChild : Validator<Address>
    {
        public AmbientChild() => Builder.Property(x => x.City).NotEmpty();
    }

    public sealed class AmbientParent : Validator<Person>
    {
        public AmbientParent(IValidator<Address> address) =>
            Builder.Property(x => x.Address!).Include((Validator<Address>)address);
    }
}