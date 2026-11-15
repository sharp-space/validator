using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Sharp.Validator.SourceGenerator.Diagnostics;
using Sharp.Validator.SourceGenerator.Generation;

namespace Sharp.Validator.SourceGenerator.Analysis;

internal static class RegexRuleAnalysis
{
    internal static bool IsPublishedWithNativeAot(AnalyzerConfigOptions options) =>
        options.TryGetValue("build_property.PublishAot", out var value) &&
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    internal static void Analyze(IInvocationOperation invocation, Action<Diagnostic> report)
    {
        var method = invocation.TargetMethod;

        // Cheap gate, then confirm it is Sharp.Validator's own Regex(string, …) rule: declared by the library, not an
        // app extension that shares the name and builder return type (the same test Discovery applies to accessors).
        if (method.Name != "Regex" ||
            !Discovery.IsLibraryMethod(method) ||
            method.ReturnType.OriginalDefinition is not INamedTypeSymbol { MetadataName: "PropertyValidatorBuilder`2" } builder ||
            builder.ContainingNamespace?.ToDisplayString() != "Sharp.Validator" ||
            builder.ContainingAssembly?.Name != "Sharp.Validator")
            return;

        // The only string parameter across the Regex overloads is the pattern; the Regex(Regex)/Func overloads have no
        // string parameter, so a constant string argument uniquely identifies an inlinable literal-pattern call.
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type.SpecialType == SpecialType.System_String &&
                argument.Value.ConstantValue is { HasValue: true, Value: string })
            {
                report(Diagnostic.Create(DiagnosticDescriptors.RegexRule, argument.Value.Syntax.GetLocation()));
                return;
            }

            // An inline `new Regex("literal")` handed to the Regex(Regex) overload is still an interpreted regex under
            // Native AOT — flag it too (the code fix skips this shape; the diagnostic is the value).
            if (argument.Value is IObjectCreationOperation { Type: INamedTypeSymbol { Name: "Regex" } regexType } creation &&
                regexType.ContainingNamespace?.ToDisplayString() == "System.Text.RegularExpressions" &&
                HasConstantStringArgument(creation))
            {
                report(Diagnostic.Create(DiagnosticDescriptors.RegexRule, creation.Syntax.GetLocation()));
                return;
            }
        }
    }

    private static bool HasConstantStringArgument(IObjectCreationOperation creation)
    {
        foreach (var argument in creation.Arguments)
            if (argument.Parameter?.Type.SpecialType == SpecialType.System_String &&
                argument.Value.ConstantValue is { HasValue: true, Value: string })
                return true;

        return false;
    }
}