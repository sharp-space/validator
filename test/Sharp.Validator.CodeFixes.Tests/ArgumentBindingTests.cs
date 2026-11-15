using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Sharp.Validator.Fixtures;

namespace Sharp.Validator.CodeFixes.Tests;

public sealed class ArgumentBindingTests
{
    [Fact]
    public void ImplicitOptionsAndConfiguration_AreNotCopiedIntoAttributeArguments()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { Rule(\"pattern\"); } void Rule(string regexString) { } }");
        var compilation = CSharpCompilation.Create("Arguments", [tree],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)));

        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var operation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(invocation)!;
        foreach (var name in new[] { "regexOptions", "configure" })
        {
            var implicitArgument = StubProxy.Of<IArgumentOperation>(("Parameter", StubProxy.Of<IParameterSymbol>(("Name", name))),
                ("Syntax", operation.Arguments[0].Syntax), ("IsImplicit", true));

            Assert.True(Read(StubProxy.Of<IInvocationOperation>(("Arguments", ImmutableArray.Create(operation.Arguments[0], implicitArgument)))));
        }

        var unbound = StubProxy.Of<IArgumentOperation>(("Syntax", operation.Arguments[0].Syntax));
        Assert.False(Read(StubProxy.Of<IInvocationOperation>(("Arguments", ImmutableArray.Create(unbound)))));
    }

    private static bool Read(IInvocationOperation operation)
    {
        object?[] args = [operation, null];
        return (bool)typeof(GeneratedRegexCodeFix).GetMethod("TryReadArguments", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
    }
}