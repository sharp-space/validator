using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Sharp.Validator.SourceGenerator.Analysis;
using Sharp.Validator.SourceGenerator.Diagnostics;

namespace Sharp.Validator.SourceGenerator.Analyzers;

/// <summary>
/// Flags <c>Regex("&lt;literal&gt;", …)</c> rule call sites in an app published with Native AOT. There a runtime-built
/// <see cref="System.Text.RegularExpressions.Regex" /> runs interpreted (<c>RegexOptions.Compiled</c> needs Reflection.Emit and is unavailable) and
/// keeps the regex parser in the binary, whereas a <c>[GeneratedRegex]</c> method compiles to matching code. Under the JIT a literal pattern spreads
/// over one instance per core and a <c>[GeneratedRegex]</c> is one shared instance, so the conversion is not advice there. The accompanying code fix
/// performs the conversion.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RegexRuleAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SVL010";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DiagnosticDescriptors.RegexRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            // The package's buildTransitive props make PublishAot visible to analyzers.
            if (RegexRuleAnalysis.IsPublishedWithNativeAot(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
                start.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context) =>
        RegexRuleAnalysis.Analyze((IInvocationOperation)context.Operation, context.ReportDiagnostic);
}