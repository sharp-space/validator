using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Sharp.Validator.Tests.Registration;

public sealed class ScanModel
{
    public string? Name { get; init; }
}

// Regression: the reflection scan registered every validator it found, while the generated registrar skips the types
// generated code cannot name and SVL001 reports them. Whether a private, protected or file-local validator was
// registered therefore depended on whether the assembly was built with the generator. The scan now skips the same
// types, and the IValidator<T> interfaces closed over such a model.
public class ScanAccessibilityTests
{
    internal static void Rules(ValidatorBuilder<ScanModel> builder) => builder.Property(model => model.Name).NotEmpty();

    [Fact]
    public void Scan_RegistersOnlyValidatorsGeneratedCodeCouldName()
    {
        var services = new ServiceCollection();
        services.AddValidators(Assembly.GetExecutingAssembly());

        using var provider = services.BuildServiceProvider();
        var registered = provider.GetServices<IValidator<ScanModel>>().Select(x => x.GetType()).ToHashSet();
        Assert.Equal(
            // ReSharper disable once UseCollectionExpression — the explicit set keeps the comparison order-free.
            new HashSet<Type> { typeof(ProtectedInternalValidator), typeof(InternalValidator), typeof(MixedValidator) },
            registered);
    }

    [Fact]
    public void Scan_SkipsAnInterfaceClosedOverAModelGeneratedCodeCouldNotName()
    {
        var services = new ServiceCollection();
        services.AddValidators(Assembly.GetExecutingAssembly());

        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IValidator<PrivateModel>>());
    }

    [Fact]
    public void ExplicitRegistration_StillAcceptsAPrivateValidator()
    {
        var services = new ServiceCollection();
        services.AddValidator<PrivateValidator>();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<PrivateValidator>(provider.GetRequiredService<IValidator<ScanModel>>());
    }

    private sealed class PrivateModel
    {
        public string? Name { get; init; }
    }

    private sealed class PrivateValidator() : Validator<ScanModel>(Rules);

    protected sealed class ProtectedValidator() : Validator<ScanModel>(Rules);

    private protected sealed class PrivateProtectedValidator() : Validator<ScanModel>(Rules);

    protected internal sealed class ProtectedInternalValidator() : Validator<ScanModel>(Rules);

    internal sealed class InternalValidator() : Validator<ScanModel>(Rules);

    // ReSharper disable once UnusedType.Local — the scan must skip a validator nested in a private class.
    private static class Hidden
    {
        // ReSharper disable once UnusedType.Local — the scan must skip it.
        public sealed class PublicInPrivateValidator() : Validator<ScanModel>(Rules);
    }

    // Nameable itself, but it also closes IValidator<T> over a private model, which generated code could not write in
    // typeof(). Only the nameable interface is registered.
    public sealed class MixedValidator() : Validator<ScanModel>(Rules), IValidator<PrivateModel>
    {
        private static readonly Validator<PrivateModel> Inner = new(x => x.Property(model => model.Name).NotEmpty());

        ValidationResult IValidator<PrivateModel>.Validate(ValidationContext<PrivateModel> context) => Inner.Validate(context);

        ValueTask<ValidationResult> IValidator<PrivateModel>.ValidateAsync(
            ValidationContext<PrivateModel> context,
            CancellationToken cancellationToken
        ) => Inner.ValidateAsync(context, cancellationToken);
    }
}

// ReSharper disable once UnusedType.Local — the scan must skip a file-local validator.
file sealed class FileLocalValidator() : Validator<ScanModel>(ScanAccessibilityTests.Rules);