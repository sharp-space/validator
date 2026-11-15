using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sharp.Validator.SourceGenerator.Generation;

/// <summary>
/// Symbol-level inspection that mirrors, at compile time, the reflection the library does at runtime. The class is partial and split by concern:
/// <c>Discovery.Validators.cs</c> finds <c>IValidator&lt;T&gt;</c> implementations and <c>Discovery.Accessors.cs</c> finds <c>Property</c>/
/// <c>Elements</c> accessor call sites. This file holds the symbol helpers shared by both.
/// </summary>
internal static partial class Discovery
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>
    /// True when a call binds to a method Sharp.Validator declares. A rule is recognized by its name and builder return type first, and an app's own
    /// extension can share both while taking a lambda of another meaning, so the declaring assembly decides. It holds for instance methods, classic
    /// extension methods, and extension members alike.
    /// </summary>
    internal static bool IsLibraryMethod(IMethodSymbol method) =>
        (method.ReducedFrom ?? method).ContainingAssembly?.Name == "Sharp.Validator";

    /// <summary>True when a call site's bound symbol is a method Sharp.Validator declares; an unresolved call is not.</summary>
    internal static bool IsLibraryCall(ISymbol? symbol) => symbol is IMethodSymbol method && IsLibraryMethod(method);

    /// <summary>True for <c>System.Diagnostics.CodeAnalysis.ExperimentalAttribute</c>.</summary>
    internal static bool IsExperimentalAttribute(INamedTypeSymbol? attributeClass) =>
        attributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";

    /// <summary>
    /// Adds the diagnostic IDs of the <c>[Experimental]</c> attributes that naming <paramref name="symbol" /> in generated code would raise. The compiler
    /// reports one for the symbol, a containing type, a type argument, or the module or assembly it comes from, and only a <c>#pragma</c> for that ID
    /// silences it. The app already silenced each in its own code, so the generated file does the same.
    /// </summary>
    internal static void CollectExperimentalIds(ISymbol? symbol, ISet<string> ids)
    {
        while (symbol is IArrayTypeSymbol array) symbol = array.ElementType;
        if (symbol is null) return;

        for (var current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
        {
            ReadExperimentalIds(current, ids);
            if (current is INamedTypeSymbol named)
            {
                foreach (var argument in named.TypeArguments)
                    CollectExperimentalIds(argument, ids);
            }
        }

        ReadExperimentalIds(symbol.ContainingModule, ids);
        ReadExperimentalIds(symbol.ContainingAssembly, ids);
    }

    /// <summary>The collected IDs in a stable order, so equal call sites stay equal for incremental caching.</summary>
    internal static EquatableArray<string> ToSortedIds(IEnumerable<string> ids) =>
        new(ids.Distinct(StringComparer.Ordinal).OrderBy(static id => id, StringComparer.Ordinal).ToImmutableArray());

    // An ID that isn't an identifier can't appear in a #pragma, the app can't silence it there either, and writing it
    // would itself be a warning, so it is left out.
    private static void ReadExperimentalIds(ISymbol? symbol, ISet<string> ids)
    {
        if (symbol is null) return;

        foreach (var attribute in symbol.GetAttributes())
            if (IsExperimentalAttribute(attribute.AttributeClass) &&
                // ReSharper disable once MergeIntoPattern — a list pattern needs System.Index,
                // which netstandard2.0 lacks.
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string id &&
                SyntaxFacts.IsValidIdentifier(id))
                ids.Add(id);
    }
}