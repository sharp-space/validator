using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sharp.Validator.SourceGenerator.Generation;

/// <summary>
/// Discovers <c>Property(x =&gt; …)</c> and <c>Elements(x =&gt; …)</c> accessor call sites so they can be precompiled instead of built with
/// <c>Expression.Compile()</c> at runtime (slow under Native AOT, where it runs interpreted). Identification is by the invocation's return type (
/// <c>PropertyValidatorBuilder&lt;TModel, TProperty&gt;</c>), which is robust across the instance-vs-extension-method split. A non-match, a non-pure
/// chain, or any inaccessible symbol is a silent skip — the runtime path stays correct, just unoptimized.
/// </summary>
internal static partial class Discovery
{
    /// <summary>
    /// Cheap syntactic gate: <c>Property</c>/<c>Elements</c> or a cross-property rule (<c>GreaterThan</c>/<c>…</c>/<c>Equal</c>/<c>NotEqual</c>, or a range)
    /// whose first argument is a single-parameter lambda. Requiring a lambda first argument also rejects the overloads that take a literal value (those
    /// never touch an accessor).
    /// </summary>
    internal static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Property" or "Elements"
                or "GreaterThan" or "GreaterThanOrEqual" or "LessThan" or "LessThanOrEqual" or "Equal" or "NotEqual"
                or "InclusiveBetween" or "ExclusiveBetween"
            },
            ArgumentList.Arguments.Count: > 0
        } invocation
        && GetSingleParameter(Unwrap(invocation.ArgumentList.Arguments[0].Expression)) is not null;

    /// <summary>The accessors a candidate call site needs: one for its lambda, or two for a range, whose second argument is the upper bound.</summary>
    internal static EquatableArray<AccessorInfo> GetAccessors(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var methodName = ((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.ValueText;
        var lambdas = methodName is "InclusiveBetween" or "ExclusiveBetween" ? 2 : 1;

        var accessors = ImmutableArray.CreateBuilder<AccessorInfo>(lambdas);
        for (var index = 0; index < lambdas && index < invocation.ArgumentList.Arguments.Count; index++)
            if (TryGetAccessor(context, index, cancellationToken) is { } accessor)
                accessors.Add(accessor);

        return new EquatableArray<AccessorInfo>(accessors.ToImmutable());
    }

    /// <summary>The parameter of a single-parameter lambda in any spelling: <c>x =&gt;</c>, <c>(x) =&gt;</c>, <c>(Model x) =&gt;</c>.</summary>
    internal static ParameterSyntax? GetSingleParameter(ExpressionSyntax node) => node switch
    {
        SimpleLambdaExpressionSyntax simple => simple.Parameter,
        ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0],
        _ => null
    };

    private static AccessorInfo? TryGetAccessor(GeneratorSyntaxContext context, int argumentIndex, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (Unwrap(invocation.ArgumentList.Arguments[argumentIndex].Expression) is not LambdaExpressionSyntax lambda ||
            GetSingleParameter(lambda) is null ||
            lambda.ExpressionBody is not { } body)
            return null;

        var semanticModel = context.SemanticModel;
        return TryGetAccessor(invocation, body, semanticModel,
            semanticModel.GetDeclaredSymbol(GetSingleParameter(lambda)!, cancellationToken), cancellationToken);
    }

    internal static AccessorInfo? TryGetAccessor(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax body,
        SemanticModel semanticModel,
        ISymbol? parameterSymbol,
        CancellationToken cancellationToken
    )
    {
        var methodName = ((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.ValueText;

        // 1. The invocation must return a concrete Sharp.Validator builder whose accessor can be named, and be the
        //    library's own rule: an app's extension with the same name and return type can take a lambda of another
        //    meaning. A call site inside a generic validator resolves the builder to type parameters, which can't be
        //    emitted.
        if (semanticModel.GetTypeInfo(invocation, cancellationToken).Type is not INamedTypeSymbol builderType)
            return null;

        var definition = builderType.OriginalDefinition;
        if (!IsBuilderDefinition(definition) || !IsLibraryCall(semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol))
            return null;

        var modelType = definition.MetadataName == "ElementPropertyValidatorBuilder`3"
            ? builderType.TypeArguments[1]
            : builderType.TypeArguments[0];

        var propertyType = definition.MetadataName == "ElementPropertyValidatorBuilder`3"
            ? builderType.TypeArguments[2]
            : builderType.TypeArguments[1];

        if (!IsConcrete(modelType) || !IsConcrete(propertyType))
            return null;

        if (parameterSymbol is null)
            return null;

        var compilation = semanticModel.Compilation;
        if (!IsAccessibleFromGeneratedCode(modelType, compilation) ||
            !IsAccessibleFromGeneratedCode(propertyType, compilation))
            return null;

        // 2. Walk the chain leaf->root through member accesses, stripping '!' and parentheses (both absent from the
        //    runtime expression tree). Reject anything that isn't a pure, accessible, instance member chain rooted
        //    at the lambda parameter. Nullable<T>.Value remains in the emitted accessor chain but is elided from
        //    the registry key exactly as it is by the runtime path builder.
        var steps = new List<AccessorStep>();
        var pathMembers = new List<string>();
        var experimental = new HashSet<string>(StringComparer.Ordinal);

        CollectExperimentalIds(modelType, experimental);
        CollectExperimentalIds(propertyType, experimental);

        ITypeSymbol? leafType = null;
        var leafIsValue = false;
        var node = Unwrap(body);
        while (node is MemberAccessExpressionSyntax memberAccess)
        {
            var member = semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;

            // The accessor reads the member, so a property's getter must be callable from generated code too: a public
            // property can have a private or protected getter that only code inside the model can read.
            if (!CanReadMember(member, compilation)) return null;

            CollectExperimentalIds(member, experimental);
            var memberType = member is IPropertySymbol property ? property.Type : ((IFieldSymbol)member!).Type;
            var nullable = !memberType.IsValueType || memberType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            if (leafType is null)
            {
                leafType = memberType;
                leafIsValue = IsNullableValueAccess(member);
            }

            steps.Add(new AccessorStep(member.Name, nullable));
            if (!IsNullableValueAccess(member)) pathMembers.Add(member.Name);

            node = Unwrap(memberAccess.Expression);
        }

        if (steps.Count == 0 ||
            node is not IdentifierNameSyntax rootIdentifier ||
            !SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(rootIdentifier, cancellationToken).Symbol, parameterSymbol))
            return null;

        // The path leaves out a Nullable<T>.Value hop, so a chain that converts a Nullable<T> leaf, or the Value it
        // ends at, shares its key with a plain read that treats a null otherwise: x.Age!.Value read as an int? skips
        // its chain on a null age, where x.Age reads null. The registry keeps the first accessor for a key, so the
        // conversion's could serve the plain read and let NotNull pass a null. The runtime reads such a conversion
        // through its own compiled accessor (GeneratedAccessors in AccessorCache.cs), so it isn't registered: no
        // selector that converts a Nullable<T> or its Value has a generated accessor.
        if ((leafIsValue || leafType!.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) &&
            !SymbolEqualityComparer.Default.Equals(leafType, propertyType))
            return null;

        steps.Reverse(); // root-first
        pathMembers.Reverse();

        // Only intermediates are guarded; the leaf is the produced value. The path leaves out a Nullable<T>.Value hop
        // exactly as the runtime PropertyPath does, so it is the path-faithful registry key.
        var chain = ImmutableArray.CreateRange(steps.Select((step, index) => step with { Guard = index < steps.Count - 1 && step.Guard }));
        var path = string.Join(".", pathMembers);
        var kind = methodName switch
        {
            "Property" => steps.Count >= 2 ? AccessorKind.NullSafe : AccessorKind.Property,
            "Elements" => AccessorKind.Elements,
            _ => AccessorKind.NullSafe // cross-property comparison target — always null-safe, even single-hop
        };

        return new AccessorInfo(
            kind,
            modelType.ToDisplayString(FullyQualifiedFormat),
            propertyType.ToDisplayString(FullyQualifiedFormat),
            path,
            new EquatableArray<AccessorStep>(chain),
            ToSortedIds(experimental));
    }

    internal static bool IsBuilderDefinition(INamedTypeSymbol definition) =>
        (definition.MetadataName == "PropertyValidatorBuilder`2" ||
            definition.MetadataName == "ElementValidatorBuilder`2" ||
            definition.MetadataName == "ElementPropertyValidatorBuilder`3") &&
        definition.ContainingNamespace?.ToDisplayString() == "Sharp.Validator" &&
        definition.ContainingAssembly?.Name == "Sharp.Validator";

    // Kept separate from walking syntax: a write-only or hidden getter can still bind as a property symbol.
    internal static bool CanReadMember(ISymbol? member, Compilation compilation) =>
        member is IPropertySymbol or IFieldSymbol &&
        !member.IsStatic &&
        IsAccessibleFromGeneratedCode(member, compilation) &&
        (member is not IPropertySymbol property ||
            (property.GetMethod is { } getter && IsAccessibleFromGeneratedCode(getter, compilation)));

    /// <summary>Strips the null-forgiving <c>!</c> and redundant parentheses — neither appears in the runtime expression tree.</summary>
    internal static ExpressionSyntax Unwrap(ExpressionSyntax node)
    {
        while (true)
            switch (node)
            {
                case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    node = postfix.Operand;
                    break;
                case ParenthesizedExpressionSyntax parenthesized:
                    node = parenthesized.Expression;
                    break;
                default:
                    return node;
            }
    }

    /// <summary>True for <c>Nullable&lt;T&gt;.Value</c>, whose <c>.Value</c> hop is elided from the dotted path.</summary>
    private static bool IsNullableValueAccess(ISymbol member) =>
        member is { Name: "Value", ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    /// <summary>True when the type can be written as a literal generic argument — no open type parameters anywhere.</summary>
    private static bool IsConcrete(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => false,
        IArrayTypeSymbol array => IsConcrete(array.ElementType),
        // Outer<T>.Inner has no type arguments of its own, yet it still names the container's T.
        INamedTypeSymbol named => named.TypeArguments.All(IsConcrete) &&
            (named.ContainingType is null || IsConcrete(named.ContainingType)),
        _ => true
    };
}