using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Sharp.Validator.Bindings;

namespace Sharp.Validator.Tests.Registration;

// Support types are public so the DI container can activate the validators and so both test classes in this file can
// share them. These also get picked up by the assembly-scan tests in Registration/AddValidatorTests (harmless: they
// only add extra registrations those tests ignore).
public sealed class RegModel
{
    public string? Value { get; init; }
}

public sealed class RegValidator() : Validator<RegModel>(x => x.Property(regModel => regModel.Value).NotEmpty());

public sealed class RegModel2
{
    public string? Value { get; init; }
}

public sealed class RegValidator2() : Validator<RegModel2>(x => x.Property(regModel2 => regModel2.Value).NotEmpty());

// A generated registrar is found by assembly: AddValidators drains the registry of an assembly's registrars instead of
// reflecting over it, and the AddValidators overloads over several assemblies take a single or a per-type lifetime.
public sealed class ValidatorRegistryTests
{
    [Fact]
    public void NameLoadedAssembly_BeforeLookup_RunsGeneratedRegistrar()
    {
        var assembly = Assembly.Load("Sharp.Validator.LibraryFixtures");
        var modelType = assembly.GetType("Sharp.Validator.LibraryFixtures.LoadModel", true)!;
        var serviceType = typeof(IValidator<>).MakeGenericType(modelType);

        var services = new ServiceCollection();
        services.AddValidators(assembly);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService(serviceType));
    }

    [Fact]
    // ValidatorRegistry.AddRegistrar + TryGetFor with one registrar +
    // ValidatorServiceCollectionExtensions' registry-hit branch.
    public void AddRegistrarThenAddValidators_DrainsRegistryInsteadOfReflecting()
    {
        // A throwaway assembly handle with no real generated registrar, so we fully control the registry.
        var assembly = typeof(ServiceCollection).Assembly;
        ValidatorRegistry.AddRegistrar(assembly, (services, lifetime) =>
            services.Add(new ServiceDescriptor(
                typeof(IValidator<RegModel>), typeof(RegValidator), lifetime(typeof(RegValidator)))));

        var services = new ServiceCollection();
        services.AddValidators(assembly); // registry hit: the combined registrar runs instead of reflection

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IValidator<RegModel>>());
    }

    [Fact]
    // ValidatorRegistry.TryGetFor multicast branch: combined is null -> registrar, then combined + registrar.
    public void AddRegistrar_CombinesMultipleRegistrarsForSameAssembly()
    {
        var assembly = typeof(IServiceCollection).Assembly;
        ValidatorRegistry.AddRegistrar(assembly, (services, lifetime) =>
            services.Add(new ServiceDescriptor(
                typeof(IValidator<RegModel>), typeof(RegValidator), lifetime(typeof(RegValidator)))));

        ValidatorRegistry.AddRegistrar(assembly, (services, lifetime) =>
            services.Add(new ServiceDescriptor(
                typeof(IValidator<RegModel2>), typeof(RegValidator2), lifetime(typeof(RegValidator2)))));

        var services = new ServiceCollection();
        services.AddValidators(assembly);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IValidator<RegModel>>()); // first registrar ran (combined = registrar)
        Assert.NotNull(provider.GetService<IValidator<RegModel2>>()); // second registrar ran (combined + registrar)
    }

    [Fact]
    // ValidatorRegistry.AddRegistrar null-argument guards.
    public void AddRegistrar_ThrowsOnNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ValidatorRegistry.AddRegistrar(null!, (_, _) => { }));

        Assert.Throws<ArgumentNullException>(() =>
            ValidatorRegistry.AddRegistrar(
                typeof(ServiceCollection).Assembly,
                (Action<IServiceCollection, Func<Type, ServiceLifetime>>)null!));
    }

    [Fact]
    // AddValidators(IEnumerable<Assembly>, configure, ServiceLifetime).
    public void AddValidatorsEnumerable_WithSingleLifetime()
    {
        var assembly = typeof(FactAttribute).Assembly;
        ValidatorRegistry.AddRegistrar(assembly, (services, lifetime) =>
            services.Add(new ServiceDescriptor(
                typeof(IValidator<RegModel>), typeof(RegValidator), lifetime(typeof(RegValidator)))));

        var services = new ServiceCollection();
        services.AddValidators([assembly]);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IValidator<RegModel>>());
    }

    [Fact]
    // AddValidators(IEnumerable<Assembly>, configure, Func<Type, ServiceLifetime>).
    public void AddValidatorsEnumerable_WithPerTypeLifetime()
    {
        var assembly = typeof(Assert).Assembly;
        ValidatorRegistry.AddRegistrar(assembly, (services, lifetime) =>
            services.Add(new ServiceDescriptor(
                typeof(IValidator<RegModel>), typeof(RegValidator), lifetime(typeof(RegValidator)))));

        var services = new ServiceCollection();
        services.AddValidators([assembly], null, _ => ServiceLifetime.Scoped);

        var descriptor = services.Single(x => x.ServiceType == typeof(IValidator<RegModel>));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }
}