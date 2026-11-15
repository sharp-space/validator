using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Sharp.Validator.Fixtures;

namespace Sharp.Validator.SourceGenerator.Tests.Fixtures;

/// <summary>In-memory compilation harness for driving the generator and analyzers against test sources.</summary>
internal static class GeneratorTestHost
{
    internal static CSharpCompilation Compile(string source, params MetadataReference[] references) =>
        CSharpCompilation.Create(
            "SourceGeneratorTests",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            [.. MetadataReferences.Trusted, .. references],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>Runs the validator generator and returns the updated compilation plus all generated sources.</summary>
    internal static (Compilation Output, string GeneratedSource) RunGenerator(string source, params MetadataReference[] references)
    {
        var driver = CSharpGeneratorDriver.Create(new ValidatorGenerator());
        driver.RunGeneratorsAndUpdateCompilation(Compile(source, references), out var output, out _);

        var generated = string.Join("\n// ---\n", output.SyntaxTrees.Skip(1).Select(static tree => tree.ToString()));
        return (output, generated);
    }

    /// <summary>Asserts the (post-generation) compilation has no errors — i.e. the generated code compiles.</summary>
    internal static void AssertCompiles(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.True(errors.Length == 0, string.Join('\n', errors.Select(static e => e.ToString())));
    }

    /// <summary>The warnings and errors located in generated files, which an app can't silence with a <c>#pragma</c>.</summary>
    internal static Diagnostic[] GeneratedDiagnostics(Compilation compilation) =>
    [
        .. compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity >= DiagnosticSeverity.Warning &&
            diagnostic.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true)
    ];

    internal static async Task<Diagnostic[]> RunAnalyzerAsync(
        DiagnosticAnalyzer analyzer,
        string source,
        IReadOnlyDictionary<string, string>? buildProperties = null
    )
    {
        var compilation = Compile(source).WithAnalyzers([analyzer], BuildProperties.Analyzer(buildProperties));
        var diagnostics = await compilation.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return [.. diagnostics];
    }
}