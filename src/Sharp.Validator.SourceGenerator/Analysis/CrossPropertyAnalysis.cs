using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Sharp.Validator.SourceGenerator.Diagnostics;
using Sharp.Validator.SourceGenerator.Generation;

namespace Sharp.Validator.SourceGenerator.Analysis;

internal static class CrossPropertyAnalysis
{
    private static readonly ImmutableHashSet<string> RuleNames = ImmutableHashSet.CreateRange(StringComparer.Ordinal, [
        "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual", "Equal", "NotEqual",
        "InclusiveBetween", "ExclusiveBetween"
    ]);

    internal static void Analyze(IInvocationOperation invocation, Action<Diagnostic> report)
    {
        var method = invocation.TargetMethod;

        // An app extension can share a rule's name and builder return type, so the library must declare the method too.
        if (!RuleNames.Contains(method.Name) ||
            !Discovery.IsLibraryMethod(method) ||
            method.ReturnType.OriginalDefinition is not INamedTypeSymbol { MetadataName: "PropertyValidatorBuilder`2" } builder ||
            builder.ContainingNamespace?.ToDisplayString() != "Sharp.Validator" ||
            builder.ContainingAssembly?.Name != "Sharp.Validator")
            return;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type is not INamedTypeSymbol { MetadataName: "Expression`1" } parameterType ||
                parameterType.ContainingNamespace?.ToDisplayString() != "System.Linq.Expressions")
                continue;

            // A target held in a variable has no lambda to read; it is left alone. A range has two targets.
            var syntax = argument.Syntax is ArgumentSyntax { Expression: var expression } ? expression : argument.Value.Syntax;
            if (syntax is ExpressionSyntax target &&
                Discovery.Unwrap(target) is LambdaExpressionSyntax lambda &&
                !IsMemberChainOfParameter(lambda))
                report(Diagnostic.Create(DiagnosticDescriptors.CrossPropertyTarget, lambda.GetLocation(), lambda.ToString()));
        }
    }

    // The shape the generator precompiles: one or more member accesses down to the lambda's only parameter, with '!'
    // and parentheses ignored as the expression tree ignores them.
    private static bool IsMemberChainOfParameter(LambdaExpressionSyntax lambda)
    {
        if (Discovery.GetSingleParameter(lambda) is not { } parameter || lambda.ExpressionBody is not { } body) return false;

        var hops = 0;
        var node = Discovery.Unwrap(body);
        while (node is MemberAccessExpressionSyntax memberAccess && memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression))
        {
            hops++;
            node = Discovery.Unwrap(memberAccess.Expression);
        }

        return hops > 0 && node is IdentifierNameSyntax root && root.Identifier.ValueText == parameter.Identifier.ValueText;
    }
}