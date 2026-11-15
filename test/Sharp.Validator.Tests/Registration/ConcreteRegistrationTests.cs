using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Sharp.Validator.Tests.Registration;

public sealed class Line
{
    public string? Sku { get; init; }
}

public sealed class Basket
{
    public Line? Line { get; init; }
}

public sealed class LineValidator() : Validator<Line>(x => x.Property(line => line.Sku).NotEmpty());

// Takes its child by type, because Include composes a Validator<T>, not an IValidator<T>.
public sealed class BasketValidator : Validator<Basket>
{
    public BasketValidator(LineValidator line) => Builder.Property(basket => basket.Line).Include(line);
}

public sealed class Invoice
{
    public string? Number { get; init; }
}

public sealed class Receipt
{
    public string? Number { get; init; }
}

// One validator for two models.
public sealed class InvoiceReceiptValidator() : Validator<Invoice>(x => x.Property(invoice => invoice.Number).NotEmpty()),
    IValidator<Receipt>
{
    private static readonly Validator<Receipt> Receipt = new(x => x.Property(receipt => receipt.Number).NotEmpty());

    ValidationResult IValidator<Receipt>.Validate(ValidationContext<Receipt> context) => Receipt.Validate(context);

    ValueTask<ValidationResult> IValidator<Receipt>.ValidateAsync(
        ValidationContext<Receipt> context,
        CancellationToken cancellationToken
    ) => Receipt.ValidateAsync(context, cancellationToken);
}

public sealed class OpenValidator<TModel>() : Validator<TModel>(_ => { });

// Regression: registration added a validator only as IValidator<T>, so a validator that took another by its type, as
// Include needs, failed to activate, and a validator for two models got one instance per model. Every registration
// path now registers the validator type itself and forwards each IValidator<T> to it, so they resolve to one instance
// per lifetime scope.
public sealed class ConcreteRegistrationTests
{
    private static ServiceProvider Scan(Func<Type, ServiceLifetime>? lifetime = null, Action<ValidatorBuilderOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddValidators(Assembly.GetExecutingAssembly(), configure, lifetime ?? (_ => ServiceLifetime.Singleton));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void ValidatorThatTakesAnotherByType_Activates()
    {
        using var provider = Scan();
        var result = provider.GetRequiredService<IValidator<Basket>>().Validate(new Basket { Line = new Line() });
        Assert.Equal("Line.Sku", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void ValidatorTypeAndItsInterface_AreOneInstance()
    {
        using var provider = Scan();
        Assert.Same(provider.GetRequiredService<LineValidator>(), provider.GetRequiredService<IValidator<Line>>());
    }

    [Fact]
    public void Validator_ForTwoModels_IsOneInstance()
    {
        using var provider = Scan();
        var invoice = provider.GetRequiredService<IValidator<Invoice>>();
        Assert.Same(invoice, provider.GetRequiredService<IValidator<Receipt>>());
        Assert.Same(invoice, provider.GetRequiredService<InvoiceReceiptValidator>());
    }

    [Fact]
    // BasketValidator takes LineValidator, so it is scoped too: a singleton holding a scoped validator fails the build.
    public void ScopedValidator_IsOneInstancePerScope()
    {
        using var provider = Scan(type => type == typeof(LineValidator) || type == typeof(BasketValidator)
            ? ServiceLifetime.Scoped
            : ServiceLifetime.Singleton);

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var line = first.ServiceProvider.GetRequiredService<LineValidator>();
        Assert.Same(line, first.ServiceProvider.GetRequiredService<IValidator<Line>>());
        Assert.NotSame(line, second.ServiceProvider.GetRequiredService<IValidator<Line>>());
        Assert.NotNull(first.ServiceProvider.GetRequiredService<IValidator<Basket>>());
    }

    [Fact]
    public void Scan_DefaultsApplyOnceToTheSharedInstance()
    {
        using var provider = Scan(configure: options => options.PropertyNamePrefix = "P_");
        var line = provider.GetRequiredService<LineValidator>();
        Assert.Same(line, provider.GetRequiredService<IValidator<Line>>());
        Assert.Equal("P_Sku", Assert.Single(line.Validate(new Line()).Errors).PropertyName);
    }

    [Fact]
    public void ExplicitRegistration_RegistersTheValidatorTypeToo()
    {
        var services = new ServiceCollection();
        services.AddValidator<LineValidator>();
        services.AddValidator<BasketValidator>(ServiceLifetime.Transient);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Assert.Same(provider.GetRequiredService<LineValidator>(), provider.GetRequiredService<IValidator<Line>>());
        Assert.NotNull(provider.GetRequiredService<IValidator<Basket>>());
    }

    [Fact]
    // README: the interface resolves through the validator's own registration, so the two share an instance only as far
    // as the lifetime does.
    public void ScopedValidator_IsOneInstanceForBothRegistrationsInAScope()
    {
        var services = new ServiceCollection();
        services.AddValidator<LineValidator>(ServiceLifetime.Scoped);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Same(scope.ServiceProvider.GetRequiredService<LineValidator>(), scope.ServiceProvider.GetRequiredService<IValidator<Line>>());
    }

    [Fact]
    public void DisposableValidator_IsDisposedOnceForEachRegistrationItWasResolvedThrough()
    {
        var services = new ServiceCollection();
        services.AddValidator<DisposableLineValidator>();

        var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<DisposableLineValidator>();
        Assert.Same(validator, provider.GetRequiredService<IValidator<Line>>());

        provider.Dispose();

        Assert.Equal(2, validator.DisposeCalls);
    }

    [Fact]
    public async Task AsyncDisposableValidator_IsDisposedOnceForEachRegistrationItWasResolvedThrough()
    {
        var services = new ServiceCollection();
        services.AddValidator<AsyncDisposableLineValidator>();

        var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<AsyncDisposableLineValidator>();
        Assert.Same(validator, provider.GetRequiredService<IValidator<Line>>());

        await provider.DisposeAsync();

        Assert.Equal(2, validator.DisposeCalls);
    }

    [Fact]
    public void TransientValidator_IsNewOnEveryResolveOfEitherRegistration()
    {
        var services = new ServiceCollection();
        services.AddValidator<LineValidator>(ServiceLifetime.Transient);

        using var provider = services.BuildServiceProvider();
        Assert.NotSame(provider.GetRequiredService<LineValidator>(), provider.GetRequiredService<IValidator<Line>>());
        Assert.NotSame(provider.GetRequiredService<IValidator<Line>>(), provider.GetRequiredService<IValidator<Line>>());
    }

    [Fact]
    public void OpenGenericValidator_ResolvedByTypeOrInterface_GivesTheValidator()
    {
        var services = new ServiceCollection();
        services.AddValidator(typeof(OpenValidator<>), ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<OpenValidator<Line>>(provider.GetRequiredService<IValidator<Line>>());
        Assert.NotNull(provider.GetRequiredService<OpenValidator<Line>>());
    }

    [Fact]
    // The fixtures assembly is built with the generator, so this goes through its generated registrar. It is loaded by
    // name, as InfrastructureSurfaceTests does, so this test never runs its module initializer first.
    public void GeneratedRegistrar_RegistersTheValidatorTypeToo()
    {
        var assembly = Assembly.Load("Sharp.Validator.LibraryFixtures");
        var validatorType = assembly.GetType("Sharp.Validator.LibraryFixtures.LoadModelValidator", true)!;
        var modelType = assembly.GetType("Sharp.Validator.LibraryFixtures.LoadModel", true)!;
        var services = new ServiceCollection();
        services.AddValidators(assembly);

        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService(validatorType), provider.GetRequiredService(typeof(IValidator<>).MakeGenericType(modelType)));
    }

    // The interface registration hands out the instance the type registration created, and the container tracks what
    // either one returns, so a disposable validator is disposed once for each registration it was resolved through.
    // Identity is the contract kept; Dispose has to tolerate a second call, as .NET's dispose pattern requires.
    // Private, so the scans over this assembly in the other tests don't register a second validator for Line.
    private sealed class DisposableLineValidator() : Validator<Line>(x => x.Property(line => line.Sku).NotEmpty()), IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
    }

    // The same holds for DisposeAsync: the container awaits it once for each registration the validator was resolved
    // through. Private for the same reason as DisposableLineValidator.
    private sealed class AsyncDisposableLineValidator()
        : Validator<Line>(x => x.Property(line => line.Sku).NotEmpty()), IAsyncDisposable
    {
        public int DisposeCalls { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }
}