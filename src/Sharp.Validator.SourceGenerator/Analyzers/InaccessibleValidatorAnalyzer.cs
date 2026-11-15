using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Sharp.Validator.SourceGenerator.Analysis;
using Sharp.Validator.SourceGenerator.Diagnostics;

namespace Sharp.Validator.SourceGenerator.Analyzers;

/// <summary>
/// Reports SVL001 on validator types the source-generated registrar cannot reference — private, protected, or file-local types (or ones nested in them),
/// and types closing <c>IValidator&lt;T&gt;</c> over such a type. The generator silently skips them, and <c>AddValidators</c> uses an assembly's
/// generated registrar whenever it has one, under the JIT as well as Native AOT. The reflection scan for an assembly without one skips the same types, so
/// they are never registered by a scan. A validator the code around it creates with <c>new</c> is used directly and isn't reported. Lives as a
/// standalone analyzer (not inside the generator) so the diagnostic lands on the type declaration, is navigable in the IDE, and can be suppressed with
/// <c>#pragma</c> — diagnostic locations stay independent of the incremental source-emission cache.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InaccessibleValidatorAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The SVL001 diagnostic ID.</summary>
    public const string DiagnosticId = "SVL001";

    // Sharp-family diagnostic allocation: SVLxxx belongs to Validator; 001-009 is source generation/registration and
    // 010-019 is validation-rule analysis and code fixes. IDs are a public contract — users pin them in
    // #pragma/.editorconfig — so keep new IDs in their feature band and never renumber or reuse one after release. When
    // a feature band is full, the feature continues in the product's next unassigned ten-number band, and that band is
    // added to the band list.

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DiagnosticDescriptors.InaccessibleValidator];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context) =>
        ValidatorAnalysis.Analyze((INamedTypeSymbol)context.Symbol, context.Compilation,
            context.CancellationToken, context.ReportDiagnostic);

    internal static Location DiagnosticLocation(ImmutableArray<Location> locations) =>
        ValidatorAnalysis.DiagnosticLocation(locations);
}