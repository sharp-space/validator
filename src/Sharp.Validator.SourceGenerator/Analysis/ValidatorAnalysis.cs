using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sharp.Validator.SourceGenerator.Diagnostics;
using Sharp.Validator.SourceGenerator.Generation;

namespace Sharp.Validator.SourceGenerator.Analysis;

internal static class ValidatorAnalysis
{
    internal static void Analyze(INamedTypeSymbol type, Compilation compilation, CancellationToken cancellationToken, Action<Diagnostic> report)
    {
        if (!Discovery.IsRegistrableValidatorShape(type)) return;

        // Most types implement no IValidator<T>; scan interfaces first and compute accessibility lazily, only when a
        // validator interface is actually found.
        var typeAccessible = (bool?)null;
        foreach (var interfaceType in type.AllInterfaces)
        {
            if (!Discovery.IsValidatorInterface(interfaceType.OriginalDefinition)) continue;

            typeAccessible ??= Discovery.IsAccessibleFromGeneratedCode(type, compilation);
            if (typeAccessible.Value && Discovery.IsAccessibleFromGeneratedCode(interfaceType, compilation))
                continue;

            // Such a validator is visible only in the type or file around it. When the code there creates it, as in
            // Include(new AddressValidator()), it was never meant for DI, so there is nothing to warn about.
            if (!typeAccessible.Value && IsCreatedWhereVisible(type, compilation, cancellationToken))
                return;

            var location = DiagnosticLocation(type.Locations);
            report(Diagnostic.Create(DiagnosticDescriptors.InaccessibleValidator, location, type.ToDisplayString()));

            return;
        }
    }

    internal static Location DiagnosticLocation(ImmutableArray<Location> locations) =>
        locations.IsEmpty ? Location.None : locations[0];

    // Searches only the declarations that can see the validator: its file when it is file-local, or else the body of
    // the type around the innermost private or protected type in its nesting. A protected validator created in a
    // derived type elsewhere is not found, and still reported. Syntax is matched by name first, so only a candidate
    // creation is bound.
    private static bool IsCreatedWhereVisible(INamedTypeSymbol type, Compilation compilation, CancellationToken cancellationToken)
    {
        foreach (var scope in VisibleScopes(type, cancellationToken))
        {
            SemanticModel? model = null;
            foreach (var node in scope.DescendantNodes())
            {
                if (node is ObjectCreationExpressionSyntax creation)
                {
                    if (SimpleName(creation.Type) != type.Name) continue;
                }
                else if (node is not ImplicitObjectCreationExpressionSyntax) continue;

#pragma warning disable RS1030 // Binds only the declarations around a validator that would otherwise be reported.
                model ??= compilation.GetSemanticModel(scope.SyntaxTree);
#pragma warning restore RS1030
                if (SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(node, cancellationToken).Type, type))
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<SyntaxNode> VisibleScopes(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        for (var current = type; current != null; current = current.ContainingType)
            if (current.IsFileLocal)
            {
                foreach (var reference in type.DeclaringSyntaxReferences)
                    yield return reference.SyntaxTree.GetRoot(cancellationToken);

                yield break;
            }

        for (var current = type; current.ContainingType is { } container; current = container)
            if (current.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                foreach (var reference in container.DeclaringSyntaxReferences)
                    yield return reference.GetSyntax(cancellationToken);

                yield break;
            }
    }

    private static string? SimpleName(TypeSyntax syntax) => syntax switch
    {
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax aliased => aliased.Name.Identifier.ValueText,
        _ => null
    };
}