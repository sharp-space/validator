using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Sharp.Validator.Fixtures;
using Sharp.Validator.SourceGenerator.Analyzers;
using Sharp.Validator.SourceGenerator.Generation;
using Sharp.Validator.SourceGenerator.Tests.Fixtures;

namespace Sharp.Validator.SourceGenerator.Tests.Diagnostics;

public sealed class AnalyzerOperationTests
{
    private static INamedTypeSymbol Builder(
        string? ns = "Sharp.Validator",
        string? assembly = "Sharp.Validator",
        string metadata = "PropertyValidatorBuilder`2"
    ) => StubProxy.Of<INamedTypeSymbol>(("MetadataName", metadata),
        ("ContainingNamespace", ns is null
            ? null : StubProxy.Of<INamespaceSymbol>(("ToDisplayString", ns))),
        ("ContainingAssembly", assembly is null
            ? null : StubProxy.Of<IAssemblySymbol>(("Name", assembly))));

    private static List<Diagnostic> Analyze(Type analyzer, string name, ITypeSymbol result, params IArgumentOperation[] arguments) =>
        AnalyzeDeclaredIn("Sharp.Validator", analyzer, name, result, arguments);

    // The rule method's declaring assembly: the library's own, another assembly's same-named extension, or none.
    private static List<Diagnostic> AnalyzeDeclaredIn(
        string? assembly,
        Type analyzer,
        string name,
        ITypeSymbol result,
        params IArgumentOperation[] arguments
    )
    {
        var compilation = GeneratorTestHost.Compile("class C { }")
            .AddSyntaxTrees(arguments.SelectMany(x => new[] { x.Syntax, x.Value.Syntax })
                // A stub operation can leave Syntax null.
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                .Where(x => x is not null).Select(x => x.SyntaxTree).Distinct());

        var method = StubProxy.Of<IMethodSymbol>(("Name", name), ("ReturnType", result),
            ("ContainingAssembly", assembly is null
                ? null : StubProxy.Of<IAssemblySymbol>(("Name", assembly))));

        var invocation = StubProxy.Of<IInvocationOperation>(
            ("TargetMethod", method), ("Arguments", ImmutableArray.Create(arguments)),
            ("Syntax", compilation.SyntaxTrees.First().GetRoot()), ("SemanticModel", compilation.GetSemanticModel(compilation.SyntaxTrees.First())));

        var diagnostics = new List<Diagnostic>();
        var context = (OperationAnalysisContext)Activator.CreateInstance(typeof(OperationAnalysisContext),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [
                invocation, compilation.Assembly, compilation, new AnalyzerOptions([]), (Action<Diagnostic>)diagnostics.Add,
                (Func<Diagnostic, bool>)(_ => true), CancellationToken.None
            ], null)!;

        analyzer.GetMethod("AnalyzeInvocation", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [context]);

        return diagnostics;
    }

    private static IArgumentOperation Argument(
        ITypeSymbol? type,
        string text,
        object? constant = null,
        bool hasConstant = false,
        bool argumentSyntax = true,
        SyntaxNode? syntax = null
    )
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { Call(" + text + "); } }");
        var argument = tree.GetRoot().DescendantNodes().OfType<ArgumentSyntax>().First();
        var expression = argument.Expression;
        var actualSyntax = syntax is BlockSyntax
            ? tree.GetRoot().DescendantNodes().OfType<BlockSyntax>().First() : (SyntaxNode)expression;

        var value = StubProxy.Of<IOperation>(
            ("Syntax", actualSyntax),
            ("ConstantValue", hasConstant ? new Optional<object?>(constant) : default));

        return StubProxy.Of<IArgumentOperation>(
            ("Parameter", type is null ? null : StubProxy.Of<IParameterSymbol>(("Type", type))),
            ("Value", value), ("Syntax", argumentSyntax ? argument : actualSyntax));
    }

    [Fact]
    public void SameNamedForeignOrIncompleteRuleSymbols_DoNotProduceDiagnostics()
    {
        foreach (var analyzer in new[] { typeof(RegexRuleAnalyzer), typeof(CrossPropertyTargetAnalyzer) })
        {
            var name = analyzer == typeof(RegexRuleAnalyzer) ? "Regex" : "Equal";
            foreach (var type in new[]
                     {
                         Builder(metadata: "Other`2"), Builder("Other"), Builder(null), Builder(assembly: "Other"), Builder(assembly: null),
                         StubProxy.Of<ITypeSymbol>()
                     })
                Assert.Empty(Analyze(analyzer, name, type));
        }
    }

    [Fact]
    // A method another assembly declares, or one without an assembly, isn't the library's rule even when its name and
    // builder return type match and its argument is one the library's rule would flag.
    public void RuleNamedMethodsOfOtherAssemblies_DoNotProduceDiagnostics()
    {
        var expressionType = StubProxy.Of<INamedTypeSymbol>(("MetadataName", "Expression`1"),
            ("ContainingNamespace", StubProxy.Of<INamespaceSymbol>(("ToDisplayString", "System.Linq.Expressions"))));

        var stringType = StubProxy.Of<ITypeSymbol>(("SpecialType", SpecialType.System_String));
        foreach (var assembly in new[] { "Other", null })
        {
            Assert.Empty(AnalyzeDeclaredIn(assembly, typeof(CrossPropertyTargetAnalyzer),
                "Equal", Builder(), Argument(expressionType, "x => { return x.Value; }")));

            Assert.Empty(AnalyzeDeclaredIn(assembly, typeof(RegexRuleAnalyzer),
                "Regex", Builder(), Argument(stringType, "\"^a$\"", "^a$", true)));
        }

        Assert.Single(Analyze(typeof(CrossPropertyTargetAnalyzer),
            "Equal", Builder(), Argument(expressionType, "x => { return x.Value; }")));

        Assert.Single(Analyze(typeof(RegexRuleAnalyzer),
            "Regex", Builder(), Argument(stringType, "\"^a$\"", "^a$", true)));
    }

    [Fact]
    public void IncompleteBuilderMetadata_IsNotAGeneratorContract()
    {
        Assert.False(Discovery.IsBuilderDefinition(Builder(null)));
        Assert.False(Discovery.IsBuilderDefinition(Builder(assembly: null)));
    }

    [Fact]
    public void CrossPropertyAnalysis_NonExpressionOrNonLambdaArgument_IsIgnored()
    {
        var expressionType = StubProxy.Of<INamedTypeSymbol>(("MetadataName", "Expression`1"),
            ("ContainingNamespace", StubProxy.Of<INamespaceSymbol>(("ToDisplayString", "System.Linq.Expressions"))));

        var foreignExpression = StubProxy.Of<INamedTypeSymbol>(("MetadataName", "Expression`1"),
            ("ContainingNamespace", StubProxy.Of<INamespaceSymbol>(("ToDisplayString", "Other"))));

        var unknownExpression = StubProxy.Of<INamedTypeSymbol>(("MetadataName", "Expression`1"));
        Assert.Empty(Analyze(typeof(CrossPropertyTargetAnalyzer), "Equal", Builder(),
            Argument(null, "1"), Argument(foreignExpression, "1"),
            Argument(unknownExpression, "1"), Argument(expressionType, "target")));

        Assert.Empty(Analyze(typeof(CrossPropertyTargetAnalyzer), "Equal", Builder(),
            Argument(expressionType, "1", argumentSyntax: false, syntax: SyntaxFactory.Block())));

        Assert.Single(Analyze(typeof(CrossPropertyTargetAnalyzer), "Equal", Builder(),
            Argument(expressionType, "(x, y) => x.Value", argumentSyntax: false)));

        Assert.Single(Analyze(typeof(CrossPropertyTargetAnalyzer), "Equal", Builder(),
            Argument(expressionType, "x => { return x.Value; }")));
    }

    [Fact]
    public void RegexAnalysis_MissingParameterOrNonconstantPattern_IsIgnored()
    {
        Assert.Empty(Analyze(typeof(RegexRuleAnalyzer), "Regex", Builder(), Argument(null, "1")));

        var foreign = StubProxy.Of<INamedTypeSymbol>(("Name", "Regex"),
            ("ContainingNamespace", StubProxy.Of<INamespaceSymbol>(("ToDisplayString", "Other"))));

        var creation = StubProxy.Of<IObjectCreationOperation>(("Type", foreign));
        var arg = StubProxy.Of<IArgumentOperation>(("Value", creation));
        Assert.Empty(Analyze(typeof(RegexRuleAnalyzer), "Regex", Builder(), arg));

        var type = StubProxy.Of<INamedTypeSymbol>(("Name", "Regex"),
            ("ContainingNamespace", StubProxy.Of<INamespaceSymbol>(("ToDisplayString", "System.Text.RegularExpressions"))));

        var noNamespace = StubProxy.Of<IObjectCreationOperation>(("Type", StubProxy.Of<INamedTypeSymbol>(("Name", "Regex"))));
        Assert.Empty(Analyze(typeof(RegexRuleAnalyzer), "Regex", Builder(),
            StubProxy.Of<IArgumentOperation>(("Value", noNamespace))));

        var empty = StubProxy.Of<IObjectCreationOperation>(("Type", type), ("Arguments", ImmutableArray<IArgumentOperation>.Empty));
        Assert.Empty(Analyze(typeof(RegexRuleAnalyzer), "Regex", Builder(),
            StubProxy.Of<IArgumentOperation>(("Value", empty))));

        var constructor = StubProxy.Of<IObjectCreationOperation>(("Type", type), ("Arguments", ImmutableArray.Create(Argument(null, "1"))));
        Assert.Empty(Analyze(typeof(RegexRuleAnalyzer), "Regex", Builder(),
            StubProxy.Of<IArgumentOperation>(("Value", constructor))));
    }
}