using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Sharp.Validator.Bindings;

namespace Sharp.Validator;

/// <summary>DI registration helpers for validators.</summary>
public static class ValidatorServiceCollectionExtensions
{
    private const string ScanRequiresUnreferencedCode =
        "Scans assemblies via reflection (Assembly.GetTypes); scanned validator types may be removed by the trimmer. " +
        "For trimming/NativeAOT, register validators explicitly with AddValidator<T>() instead.";

    // Validator types are reflected over: their IValidator<T> interfaces are inspected, and the type is activated by DI
    // through its constructor — both must survive trimming.
    private const DynamicallyAccessedMemberTypes ValidatorTypeMembers =
        DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicConstructors;

    private static readonly Type ValidatorInterface = typeof(IValidator<>);

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <typeparamref name="TValidator" /> as itself and against every <see cref="IValidator{T}" /> it implements. A Singleton or Scoped validator
        /// is one instance for all of them; a Transient one is new on every resolve.
        /// </summary>
        public IServiceCollection AddValidator<[DynamicallyAccessedMembers(ValidatorTypeMembers)] TValidator>(
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        ) where TValidator : class => services.AddValidator(typeof(TValidator), lifetime);

        /// <summary>
        /// Registers a validator type as itself and against every <see cref="IValidator{T}" /> it implements. A Singleton or Scoped validator is one instance
        /// for all of them; a Transient one is new on every resolve. An open generic type resolves to an instance of its own for each.
        /// </summary>
        public IServiceCollection AddValidator([DynamicallyAccessedMembers(ValidatorTypeMembers)] Type type, ServiceLifetime lifetime)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(type);

            var serviceTypes = type.IsAbstract ? [] : GetServiceTypes(type, false);
            if (serviceTypes.Length == 0)
                throw new ArgumentException($"Invalid validator type ({type.Name})", nameof(type));

            if (!type.IsGenericTypeDefinition)
                ValidatorRegistry.Register(services, type, lifetime, null, serviceTypes);
            else
            {
                // DI closes an open generic only through a type mapping, never through a factory, so IValidator<>
                // cannot forward to V<>: each resolves an instance of its own.
                services.Add(new ServiceDescriptor(type, type, lifetime));
                foreach (var serviceType in serviceTypes) services.Add(new ServiceDescriptor(serviceType, type, lifetime));
            }

            return services;
        }

        /// <summary>
        /// Registers an existing validator instance for <see cref="IValidator{TModel}" />. Every resolve returns this instance, and the container never disposes
        /// it: the caller that created it owns it.
        /// </summary>
        // No lifetime: the container can't scope or recreate an object it was handed. A Scoped or Transient factory
        // returning it made the container dispose the shared instance at the end of every scope.
        public IServiceCollection AddValidator<TModel>(IValidator<TModel> validator)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(validator);
            services.Add(new ServiceDescriptor(typeof(IValidator<TModel>), validator));
            return services;
        }

        /// <summary>
        /// Scans the given assemblies for validator types and registers each one. Equivalent to calling the single-assembly <c>AddValidators</c>
        /// overload per assembly.
        /// </summary>
        public IServiceCollection AddValidators(
            IEnumerable<Assembly> assemblies,
            Action<ValidatorBuilderOptions>? configure = null,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
        {
            ArgumentNullException.ThrowIfNull(assemblies);
            foreach (var assembly in assemblies) services.AddValidators(assembly, configure, lifetime);

            return services;
        }

        /// <summary>Scans the given assemblies and registers each validator with a per-type lifetime chosen by <paramref name="lifetime" />.</summary>
        public IServiceCollection AddValidators(
            IEnumerable<Assembly> assemblies,
            Action<ValidatorBuilderOptions>? configure,
            Func<Type, ServiceLifetime> lifetime
        )
        {
            ArgumentNullException.ThrowIfNull(assemblies);
            ArgumentNullException.ThrowIfNull(lifetime);
            foreach (var assembly in assemblies) services.AddValidators(assembly, configure, lifetime);

            return services;
        }

        /// <summary>
        /// Scans <paramref name="assembly" /> for non-abstract, non-open-generic types implementing <see cref="IValidator{T}" /> and registers each with a
        /// single <paramref name="lifetime" />. Types not implementing the interface are skipped.
        /// </summary>
        public IServiceCollection AddValidators(
            Assembly assembly,
            Action<ValidatorBuilderOptions>? configure = null,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        ) => services.AddValidators(assembly, configure, _ => lifetime);

        /// <summary>
        /// Scans <paramref name="assembly" /> and registers each validator with a per-type lifetime chosen by <paramref name="lifetime" />, which is called once
        /// for each validator type. Lets one call mix lifetimes (e.g. <see cref="ServiceLifetime.Scoped" /> for validators with scoped dependencies,
        /// <see cref="ServiceLifetime.Singleton" /> for the rest) without a follow-up RemoveAll/re-register. Types not implementing the interface are skipped;
        /// validators are classes — struct implementations are not registered. A validator that generated code could not name — <c>private</c>, <c>protected</c>
        /// or <c>file</c>-local, nested in such a type, or closing <see cref="IValidator{T}" /> over such a model — is skipped on every path and reported as
        /// SVL001; register it with <c>AddValidator&lt;T&gt;()</c> instead. Each validator is registered as itself and against every
        /// <see cref="IValidator{T}" /> it implements. A Singleton or Scoped validator is one instance for all of them; a Transient one is new on every resolve.
        /// <paramref name="configure" /> fills unset fields of validators registered by this scan only. When publishing with <c>PublishTrimmed</c> (JIT), make
        /// sure the scanned assembly was built with the Sharp.Validator source generator (the default when it references the package): the reflection fallback
        /// cannot see validator types the trimmer removed.
        /// </summary>
        /// <remarks>
        /// Auto-branches: when the Sharp.Validator source generator ran for <paramref name="assembly" /> (the default — it ships in the package's analyzer
        /// slot), the closed-generic, AOT-/trim-safe generated registrations are used; otherwise it falls back to a reflection scan under the JIT, or throws
        /// under Native AOT (where a scan over trimmed types would silently under-register).
        /// </remarks>
        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "Registrar-first: the reflection fallback only runs when no generated registrar exists " +
                "for the assembly. Under NativeAOT it throws instead of scanning; under PublishTrimmed+JIT " +
                "a registrar-less assembly may under-register — documented on this method.")]
        // Deliberately suppressed instead of propagating [RequiresUnreferencedCode]: the registrar-first branch (the
        // recommended, generator-backed path) never touches reflection, and annotating this public overload would warn
        // every trimmed consumer on that safe path. Residual risk, accepted and documented: under PublishTrimmed+JIT an
        // assembly without a generated registrar falls through to the reflection scan, where trimmed validator types
        // would be silently missed (Native AOT is safe — the guard below throws instead of scanning).
        public IServiceCollection AddValidators(
            Assembly assembly,
            Action<ValidatorBuilderOptions>? configure,
            Func<Type, ServiceLifetime> lifetime
        )
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentNullException.ThrowIfNull(lifetime);

            // Assembly.Load(name) does not guarantee that the manifest module initializer has run yet.
            // Force it before looking up the generated registrar so name-loaded assemblies do not silently fall back to
            // reflection under the JIT.
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

            // Prefer source-generated registrations (closed-generic typeofs => AOT-/trim-safe), contributed by a
            // [ModuleInitializer] in the scanned assembly.
            if (ValidatorRegistry.TryGetFor(assembly, out var register))
            {
                var defaults = CreateDefaults(configure);
                register!(services, lifetime, defaults);

                return services;
            }

            return services.AddValidatorsByReflection(assembly, configure, lifetime);
        }

        [RequiresUnreferencedCode(ScanRequiresUnreferencedCode)]
        [ExcludeFromCodeCoverage(Justification = "NativeAOT-only failure is verified by the native canary.")]
        // Keep the AOT guard at the call site so trimming eliminates the reflection scan under Native AOT.
        // Only this platform-specific guard is excluded; ScanValidatorsByReflection is measured separately.
        private IServiceCollection AddValidatorsByReflection(
            Assembly assembly,
            Action<ValidatorBuilderOptions>? configure,
            Func<Type, ServiceLifetime> lifetime
        )
        {
            // No generated registrar for this assembly. Under Native AOT a reflection scan would silently miss trimmed
            // validator types, so fail loudly with actionable guidance instead of registering nothing.
            if (!RuntimeFeature.IsDynamicCodeSupported)
            {
                throw new InvalidOperationException(
                    $"No source-generated validator registrations were found for assembly '{assembly.GetName().Name}'. " +
                    "Reference the Sharp.Validator analyzer (it ships in the Sharp.Validator package) so registrations are " +
                    "generated at build time, or register validators explicitly with AddValidator<T>().");
            }

            return services.ScanValidatorsByReflection(assembly, configure, lifetime);
        }

        [RequiresUnreferencedCode(ScanRequiresUnreferencedCode)]
        private IServiceCollection ScanValidatorsByReflection(
            Assembly assembly,
            Action<ValidatorBuilderOptions>? configure,
            Func<Type, ServiceLifetime> lifetime
        )
        {
            var defaults = CreateDefaults(configure);

            // The scan registers what the generated registrar would, so an assembly registers the same validators with
            // or without the generator and SVL001 describes both: classes only (a struct implementing IValidator<T> is
            // ignored), and only types and interfaces generated code could name.
            var types = assembly.GetTypes()
                .Where(x => x is { IsAbstract: false, IsGenericTypeDefinition: false, IsValueType: false } && IsNameable(x));

            foreach (var type in types)
            {
                var serviceTypes = GetServiceTypes(type, true);
                if (serviceTypes.Length > 0) ValidatorRegistry.Register(services, type, lifetime(type), defaults, serviceTypes);
            }

            return services;
        }
    }

    // The IValidator<T> interfaces a type registers as, an open one as IValidator<>. A scan leaves out an interface
    // closed over a model generated code could not name, as the generated registrar does.
    private static Type[] GetServiceTypes(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type,
        bool scanning
    )
    {
        var serviceTypes = new List<Type>();
        foreach (var interfaceType in type.GetInterfaces())
        {
            if (!interfaceType.IsGenericType || interfaceType.GetGenericTypeDefinition() != ValidatorInterface) continue;
            if (scanning && !IsNameable(interfaceType)) continue;

            serviceTypes.Add(interfaceType.ContainsGenericParameters ? ValidatorInterface : interfaceType);
        }

        return [.. serviceTypes];
    }

    // True when generated code in the type's assembly could write it in typeof(): the reflection form of the
    // generator's accessibility rule. A private, protected or private protected type is out, as is anything nested in
    // one or closed over one. So is a file-local type, which the compiler emits under a name C# cannot write
    // (<File>F{checksum}__Name), like every compiler-generated type.
    internal static bool IsNameable(Type type)
    {
        if (type.HasElementType) return IsNameable(type.GetElementType()!);

        for (var current = type; current is not null; current = current.DeclaringType)
            if (current.IsNestedPrivate || current.IsNestedFamily || current.IsNestedFamANDAssem ||
                current.Name.StartsWith('<'))
                return false;

        foreach (var argument in type.GenericTypeArguments)
            if (!IsNameable(argument))
                return false;

        return true;
    }

    private static ValidatorBuilderOptions? CreateDefaults(Action<ValidatorBuilderOptions>? configure)
    {
        if (configure is null) return null;

        var defaults = new ValidatorBuilderOptions();
        configure(defaults);
        return defaults;
    }
}