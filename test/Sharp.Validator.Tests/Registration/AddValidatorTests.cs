using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Sharp.Validator.Tests.Registration;

[Collection(GlobalOptions.Name)]
public sealed class AddValidatorTests
{
    [Fact]
    public void AddValidatorGeneric_RegistersIValidatorTModel()
    {
        var services = new ServiceCollection();
        services.AddValidator<FooValidator>();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IValidator<FooModel>>());
    }

    [Fact]
    public void AddValidator_LifetimeRespected()
    {
        var services = new ServiceCollection();
        services.AddValidator<FooValidator>(ServiceLifetime.Transient);

        using var provider = services.BuildServiceProvider();
        var first = provider.GetService<IValidator<FooModel>>();
        var second = provider.GetService<IValidator<FooModel>>();
        Assert.NotSame(first, second);
    }

    [Fact]
    public void AddValidatorSingleton_IsDefault()
    {
        var services = new ServiceCollection();
        services.AddValidator<FooValidator>();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetService<IValidator<FooModel>>();
        var second = provider.GetService<IValidator<FooModel>>();
        Assert.Same(first, second);
    }

    [Fact]
    public void AddValidatorInstanceRegistration_ReturnsSameInstance()
    {
        var validator = new FooValidator();
        var services = new ServiceCollection();
        services.AddValidator(validator);

        using var provider = services.BuildServiceProvider();
        Assert.Same(validator, provider.GetService<IValidator<FooModel>>());
    }

    [Fact]
    public void AddValidator_ThrowsOnAbstractType()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddValidator(typeof(AbstractValidator), ServiceLifetime.Singleton));
    }

    [Fact]
    public void AddValidator_ThrowsOnTypeNotImplementingIValidator()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddValidator(typeof(string), ServiceLifetime.Singleton));
    }

    [Fact]
    public void AddValidatorsAssembly_WithInvalidTypes_RegistersOnlyValidators()
    {
        var services = new ServiceCollection();
        services.AddValidators(Assembly.GetExecutingAssembly());
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IValidator<FooModel>>());
        Assert.NotNull(provider.GetService<IValidator<BarModel>>());
    }

    [Fact]
    public void AddValidatorsAssembly_AppliesPerTypeLifetimeSelector()
    {
        var services = new ServiceCollection();

        // One scan, mixed lifetimes: Foo → Scoped, everything else → Singleton (no RemoveAll/re-register).
        services.AddValidators(Assembly.GetExecutingAssembly(), null,
            x => x == typeof(FooValidator) ? ServiceLifetime.Scoped : ServiceLifetime.Singleton);

        var foo = services.Single(x => x.ServiceType == typeof(IValidator<FooModel>));
        var bar = services.Single(x => x.ServiceType == typeof(IValidator<BarModel>));

        Assert.Equal(ServiceLifetime.Scoped, foo.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, bar.Lifetime);
    }

    [Fact]
    public void AddValidatorsConfigure_IsScopedToServiceCollection()
    {
        var firstServices = new ServiceCollection();
        firstServices.AddValidators(Assembly.GetExecutingAssembly(),
            options => options.PropertyNamePrefix = "FIRST_");

        var secondServices = new ServiceCollection();
        secondServices.AddValidators(Assembly.GetExecutingAssembly(),
            options => options.PropertyNamePrefix = "SECOND_");

        using var firstProvider = firstServices.BuildServiceProvider();
        using var secondProvider = secondServices.BuildServiceProvider();

        var firstValidator = firstProvider.GetRequiredService<IValidator<FooModel>>();
        var secondValidator = secondProvider.GetRequiredService<IValidator<FooModel>>();

        Assert.Equal("FIRST_X", firstValidator.Validate(new FooModel()).Errors[0].PropertyName);
        Assert.Equal("SECOND_X", secondValidator.Validate(new FooModel()).Errors[0].PropertyName);
    }

    [Fact]
    public void AddValidatorsConfigure_DoesNotMutateGlobalOptions()
    {
        var snapshot = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = new ValidatorBuilderOptions();
            var services = new ServiceCollection();

            services.AddValidators(Assembly.GetExecutingAssembly(),
                options => options.PropertyNamePrefix = "SCAN_");

            Assert.Null(ValidatorBuilderOptions.Global.PropertyNamePrefix);
        }
        finally
        {
            ValidatorBuilderOptions.Global = snapshot;
        }
    }

    // Internal, not private: a scan registers only the validators generated code could name (ScanAccessibilityTests).
    internal sealed class FooModel
    {
        public string? X => null;
    }

    internal sealed class BarModel
    {
        public string? Y => null;
    }

    internal sealed class FooValidator() : Validator<FooModel>(x => x.Property(fooModel => fooModel.X).NotEmpty());

    internal sealed class BarValidator() : Validator<BarModel>(x => x.Property(barModel => barModel.Y).NotEmpty());

    private abstract class AbstractValidator : Validator<FooModel>;
}