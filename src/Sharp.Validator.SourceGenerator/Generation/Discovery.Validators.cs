using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Sharp.Validator.SourceGenerator.Analyzers;

namespace Sharp.Validator.SourceGenerator.Generation;

/// <summary>
/// Compile-time discovery of the <c>IValidator&lt;T&gt;</c> implementations a type provides, feeding the AOT-safe DI registrations emitted by
/// <c>Emitter.Validators.cs</c>. Shared with <see cref="InaccessibleValidatorAnalyzer" /> so the analyzer's SVL001 verdicts always match what the
/// generator actually skips.
/// </summary>
internal static partial class Discovery
{
    private const string ValidatorNamespace = "Sharp.Validator";

    /// <summary>True when <paramref name="definition" /> is the open <c>Sharp.Validator.IValidator&lt;T&gt;</c>.</summary>
    internal static bool IsValidatorInterface(INamedTypeSymbol definition) =>
        definition is { MetadataName: "IValidator`1", ContainingNamespace: { IsGlobalNamespace: false } ns } &&
        ns.ToDisplayString() == ValidatorNamespace;

    /// <summary>
    /// True for the concrete shapes the generated registrar can register: non-abstract, non-open-generic classes (records included). Matches the reflection
    /// scan's contract — validators are classes.
    /// </summary>
    internal static bool IsRegistrableValidatorShape(INamedTypeSymbol type) =>
        type is { TypeKind: TypeKind.Class, IsAbstract: false, IsGenericType: false };

    /// <summary>
    /// True when generated consumer code can reference the symbol. Delegates to the compiler's own accessibility check from the compiling assembly's vantage
    /// point, so <c>protected internal</c> nestings (accessible assembly-wide) are judged correctly.
    /// </summary>
    internal static bool IsAccessibleFromGeneratedCode(ISymbol symbol, Compilation compilation) =>
        !ContainsFileLocalType(symbol) &&
        compilation.IsSymbolAccessibleWithin(symbol, compilation.Assembly);

    private static bool ContainsFileLocalType(ISymbol symbol)
    {
        if (symbol is IArrayTypeSymbol array)
            return ContainsFileLocalType(array.ElementType);

        if (symbol is not INamedTypeSymbol named) return false;

        for (var containing = named; containing != null; containing = containing.ContainingType)
            if (containing.IsFileLocal)
                return true;

        foreach (var argument in named.TypeArguments)
            if (ContainsFileLocalType(argument))
                return true;

        return false;
    }

    /// <summary>Enumerates the closed <c>IValidator&lt;T&gt;</c> interfaces a concrete type implements (one entry each).</summary>
    internal static IEnumerable<ValidatorInfo> GetValidators(INamedTypeSymbol type, Compilation compilation)
    {
        var implementationFqn = type.ToDisplayString(FullyQualifiedFormat);
        var typeAccessible = IsAccessibleFromGeneratedCode(type, compilation);
        var typeExperimental = new HashSet<string>(StringComparer.Ordinal);
        CollectExperimentalIds(type, typeExperimental);
        foreach (var interfaceType in type.AllInterfaces)
            if (IsValidatorInterface(interfaceType.OriginalDefinition))
            {
                // The closed interface must be referenceable too: typeof(IValidator<PrivateModel>) would not compile in
                // the registrar even when the implementation itself is accessible.
                var accessible = typeAccessible && IsAccessibleFromGeneratedCode(interfaceType, compilation);
                var experimental = new HashSet<string>(typeExperimental, StringComparer.Ordinal);
                CollectExperimentalIds(interfaceType, experimental);
                yield return new ValidatorInfo(interfaceType.ToDisplayString(FullyQualifiedFormat), implementationFqn, accessible,
                    ToSortedIds(experimental));
            }
    }
}