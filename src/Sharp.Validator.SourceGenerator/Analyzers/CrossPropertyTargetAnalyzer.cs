using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Sharp.Validator.SourceGenerator.Analysis;
using Sharp.Validator.SourceGenerator.Diagnostics;
using Sharp.Validator.SourceGenerator.Generation;

namespace Sharp.Validator.SourceGenerator.Analyzers;

/// <summary>
/// Flags a cross-property rule (<c>GreaterThan</c>, <c>GreaterThanOrEqual</c>, <c>LessThan</c>, <c>LessThanOrEqual</c>, <c>Equal</c>, <c>NotEqual</c>,
/// and either bound of <c>InclusiveBetween</c> or <c>ExclusiveBetween</c>) whose target lambda is not a member chain rooted at its parameter, in a
/// project analyzed for trimming. A member chain reads through an accessor the generator precompiles (<see cref="Discovery" />); any other target is an
/// expression tree compiled for each validator instance, which Native AOT runs in the expression interpreter and trimming can break by removing an
/// operator the runtime looks up by reflection.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CrossPropertyTargetAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The SVL011 diagnostic ID.</summary>
    public const string DiagnosticId = "SVL011";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DiagnosticDescriptors.CrossPropertyTarget];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            // The SDK's ILLink analyzer props expose EnableTrimAnalyzer whenever it can be true.
            if (start.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.EnableTrimAnalyzer", out var value) &&
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                start.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context) =>
        CrossPropertyAnalysis.Analyze((IInvocationOperation)context.Operation, context.ReportDiagnostic);
}