using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Sharp.Validator.SourceGenerator.Analysis;
using Sharp.Validator.SourceGenerator.Analyzers;
using Sharp.Validator.SourceGenerator.Generation;
using Sharp.Validator.SourceGenerator.Tests.Fixtures;

namespace Sharp.Validator.SourceGenerator.Tests.Diagnostics;

public sealed class AnalyzerSymbolTests
{
    [Fact]
    public void MissingParameterBinding_EvenWhenTheReturnTypeIsARealBuilder_IsIgnored()
    {
        var compilation = GeneratorTestHost.Compile(
            """
            using Sharp.Validator;
            public class Model { public int Value { get; set; } }
            public class C { void M(ValidatorBuilder<Model> b) { b.Property(x => x.Value); } }
            """);

        var tree = compilation.SyntaxTrees.Single();
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var lambda = (LambdaExpressionSyntax)invocation.ArgumentList.Arguments[0].Expression;
        Assert.Null(Discovery.TryGetAccessor(invocation,
            lambda.ExpressionBody!, compilation.GetSemanticModel(tree), null, CancellationToken.None));
    }

    [Fact]
    public void SupportedAccessorName_IsACandidate()
    {
        foreach (var name in new[]
                 {
                     "Property", "Elements", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual", "Equal", "NotEqual",
                     "InclusiveBetween", "ExclusiveBetween"
                 })
        {
            Assert.True(Discovery.IsCandidate(SyntaxFactory.ParseExpression($"b.{name}(x => x.Value)")));

            for (var i = 0; i < name.Length; i++)
            {
                var different = name[..i] + "_" + name[(i + 1)..];
                Assert.False(Discovery.IsCandidate(SyntaxFactory.ParseExpression($"b.{different}(x => x.Value)")));
            }
        }
    }

    [Fact]
    public void UnknownAccessorKind_DoesNotEmitARegistration()
    {
        var builder = new StringBuilder();
        var accessor = new AccessorInfo((AccessorKind)999, "Model", "int", "Value", new([new("Value", false)]));
        typeof(Emitter).GetMethod("AppendRegistration", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [builder, accessor]);

        Assert.Equal(string.Empty, builder.ToString());
    }

    [Theory]
    [InlineData("C", "C")]
    [InlineData("Namespace.C", "C")]
    [InlineData("global::C", "C")]
    [InlineData("int[]", null)]
    public void ConstructionNameMatching_QualifiedAliasOrNonNamedSyntax_IsHandled(string type, string? expected) =>
        Assert.Equal(expected,
            typeof(ValidatorAnalysis).GetMethod("SimpleName", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                [SyntaxFactory.ParseTypeName(type)]));

    [Fact]
    public void PublicAndNestedPublicTypes_HaveNoPrivateCreationScope()
    {
        var compilation = GeneratorTestHost.Compile("public class Outer { public class Inner { } }");
        var method = typeof(ValidatorAnalysis).GetMethod("VisibleScopes", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var name in new[] { "Outer", "Outer+Inner" })
            Assert.Empty((IEnumerable<SyntaxNode>)method.Invoke(null, [compilation.GetTypeByMetadataName(name), CancellationToken.None])!);
    }

    [Fact]
    public async Task PartialCode_NonclassShapeOrMultipleValidatorInterfaces_HasNoFalseDiagnostic() =>
        Assert.Empty(await GeneratorTestHost.RunAnalyzerAsync(new InaccessibleValidatorAnalyzer(),
            """
            using Sharp.Validator;
            public struct S { }
            public abstract class Abstract { }
            public class Generic<T> { }
            public class Multiple : Validator<string>, IValidator<int> { public Multiple() : base(_ => { }) { } }
            public class Outer
            {
                protected class Hidden : Validator<string> { public Hidden() : base(_ => { }) { } }
                private protected class PrivateHidden : Validator<string> { public PrivateHidden() : base(_ => { }) { } }
                object Create() => new Hidden();
                object CreateOther() => new PrivateHidden();
            }
            """));

    [Fact]
    public void MetadataOnlyInaccessibleValidators_ReceiveALocationlessDiagnostic()
    {
        var source = GeneratorTestHost.Compile(
            "using Sharp.Validator; internal class Model { } internal class Impl : Validator<Model> { public Impl() : base(_ => { }) { } }");

        using var stream = new MemoryStream();
        Assert.True(source.Emit(stream).Success);

        var compilation = GeneratorTestHost.Compile("class C { }").AddReferences(MetadataReference.CreateFromImage(stream.ToArray()));
        var type = compilation.GetTypeByMetadataName("Impl")!;
        var diagnostics = new List<Diagnostic>();
        var context = (SymbolAnalysisContext)Activator.CreateInstance(typeof(SymbolAnalysisContext),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [
                type, compilation, new AnalyzerOptions([]), (Action<Diagnostic>)diagnostics.Add, (Func<Diagnostic, bool>)(_ => true),
                CancellationToken.None
            ], null)!;

        typeof(InaccessibleValidatorAnalyzer).GetMethod("AnalyzeNamedType", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [context]);

        Assert.True(Assert.Single(diagnostics).Location.IsInMetadata);
        Assert.Equal(Location.None, InaccessibleValidatorAnalyzer.DiagnosticLocation([]));
    }
}