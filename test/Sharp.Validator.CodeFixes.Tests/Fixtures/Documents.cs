using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Sharp.Validator.Fixtures;

namespace Sharp.Validator.CodeFixes.Tests.Fixtures;

internal static class Documents
{
    // A document of one C# source in a project of its own, compiled against the test process's assemblies.
    public static Document Create(AdhocWorkspace workspace, string source) =>
        workspace.AddProject("CodeFixTests", LanguageNames.CSharp)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(MetadataReferences.Trusted)
            .AddDocument("Input.cs", SourceText.From(source));
}