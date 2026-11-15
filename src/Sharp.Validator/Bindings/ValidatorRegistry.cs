using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Sharp.Validator.Bindings;

/// <summary>
/// Hook through which source-generated code supplies trim- and Native AOT-safe validator registrations for a compilation. Generated code calls
/// <c>AddRegistrar</c> from a <c>[ModuleInitializer]</c>; the auto-branching <c>AddValidators(Assembly)</c> overloads drain the matching registrar
/// instead of reflecting over the assembly. Infrastructure for the Sharp.Validator source generator; not for direct use.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ValidatorRegistry
{
    private static readonly Lock Gate = new();

    private static readonly List<(Assembly Assembly, Action<IServiceCollection, Func<Type, ServiceLifetime>, ValidatorBuilderOptions?> Register)>
        Registrars = [];

    /// <summary>Called by generated code (from a module initializer) to contribute one assembly's validator registrations.</summary>
    public static void AddRegistrar(Assembly assembly, Action<IServiceCollection, Func<Type, ServiceLifetime>> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        AddRegistrar(assembly, (services, lifetime, _) => register(services, lifetime));
    }

    /// <summary>Called by generated code (from a module initializer) to contribute one assembly's validator registrations.</summary>
    public static void AddRegistrar(Assembly assembly, Action<IServiceCollection, Func<Type, ServiceLifetime>, ValidatorBuilderOptions?> register)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(register);

        lock (Gate)
        {
            Registrars.Add((assembly, register));
        }
    }

    /// <summary>
    /// Combines every registrar contributed for <paramref name="assembly" /> into one callback. Returns <c>false</c> when none ran — i.e. the generator
    /// wasn't applied to that assembly.
    /// </summary>
    internal static bool TryGetFor(
        Assembly assembly,
        out Action<IServiceCollection, Func<Type, ServiceLifetime>, ValidatorBuilderOptions?>? register
    )
    {
        Action<IServiceCollection, Func<Type, ServiceLifetime>, ValidatorBuilderOptions?>? combined = null;

        lock (Gate)
        {
            foreach (var (registeredAssembly, registrar) in Registrars)
                if (registeredAssembly == assembly)
                    combined = combined is null ? registrar : combined + registrar;
        }

        register = combined;
        return combined is not null;
    }

    /// <summary>
    /// Registers <paramref name="implementationType" /> as itself and forwards each of <paramref name="serviceTypes" /> to that registration with the same
    /// lifetime. A Singleton or Scoped validator is then one instance for the type and every <see cref="IValidator{T}" /> it implements; a Transient one is
    /// new on every resolve. <paramref name="defaults" /> apply once to each instance the container creates.
    /// </summary>
    public static void Register(
        IServiceCollection services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType,
        ServiceLifetime lifetime,
        ValidatorBuilderOptions? defaults,
        params Type[] serviceTypes
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceTypes);

        services.Add(CreateDescriptor(implementationType, implementationType, lifetime, defaults));

        // Same lifetime as the registration it resolves, so a singleton never holds a scoped instance.
        foreach (var serviceType in serviceTypes)
            services.Add(new ServiceDescriptor(serviceType, provider => provider.GetRequiredService(implementationType), lifetime));
    }

    /// <summary>
    /// Creates a descriptor that applies scan-local validator defaults after DI activation. Registrars generated before <see cref="Register" /> existed call
    /// this once per interface.
    /// </summary>
    public static ServiceDescriptor CreateDescriptor(
        Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType,
        ServiceLifetime lifetime,
        ValidatorBuilderOptions? defaults
    )
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        if (defaults is null)
            return new ServiceDescriptor(serviceType, implementationType, lifetime);

        return new ServiceDescriptor(serviceType, provider =>
        {
            var instance = ActivatorUtilities.CreateInstance(provider, implementationType);
            if (instance is IValidatorConfigurationTarget target)
                target.ApplyDefaults(defaults);

            return instance;
        }, lifetime);
    }
}