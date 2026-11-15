using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sharp.Validator.SourceGenerator.Generation;
using Sharp.Validator.SourceGenerator.Tests.Fixtures;

namespace Sharp.Validator.SourceGenerator.Tests.Generator;

public sealed class DiscoveryTests
{
    [Fact]
    public void Incremental_Arrays_CompareContentsForDefaultEmptyOrBoxedValues()
    {
        var empty = default(EquatableArray<string>);
        var one = new EquatableArray<string>(["a"]);
        var same = new EquatableArray<string>(["a"]);
        var other = new EquatableArray<string>(["b"]);

        Assert.Empty(empty.AsImmutableArray());
        Assert.True(empty.Equals(new EquatableArray<string>([])));
        Assert.False(empty.Equals(one));
        Assert.False(one.Equals(other));
        Assert.True(one.Equals((object)same));

        // ReSharper disable once SuspiciousTypeConversion.Global — Equals with another type is the case under test.
        Assert.False(one.Equals("a"));
        Assert.Equal(one.GetHashCode(), same.GetHashCode());
    }

    [Theory]
    [InlineData("x.Property(y => y.Text)", true)]
    [InlineData("x.ExclusiveBetween(y => y.Min, y => y.Max)", true)]
    [InlineData("x.Property()", false)]
    [InlineData("x.Property(1)", false)]
    [InlineData("x.Other(y => y.Text)", false)]
    [InlineData("Property(y => y.Text)", false)]
    public void SyntaxCandidates_WithoutAccessorRuleOrSingleLambdaParameter_AreRejected(string source, bool expected) =>
        Assert.Equal(expected, Discovery.IsCandidate(SyntaxFactory.ParseExpression(source)));

    [Theory]
    [InlineData("b.Property((System.Func<Model, string>) (x => x.Text))")]
    [InlineData("b.Property((Model x, Model y) => x.Text)")]
    [InlineData("b.Property(x => { return x.Text; })")]
    [InlineData("b.Property(x => x)")]
    [InlineData("b.Property(x => Model.StaticText)")]
    [InlineData("b.Property(x => x.Missing)")]
    [InlineData("b.Property(x => x.WriteOnly)")]
    [InlineData("b.Property(x => x.PrivateGetter)")]
    [InlineData("b.Property(x => new Model().Text)")]
    [InlineData("b.Property(x => outer.Text)")]
    [InlineData("b.InclusiveBetween(x => x.Text, \"constant\")")]
    [InlineData("b.Property(x => x.Text).Equal(x => x.Text, 1, 2, 3)")]
    public void IncompleteOrNonMemberAccessors_AreSafeToSkip(string call)
    {
        var source =
            $$"""
              using System;
              using Sharp.Validator;
              public class Model
              {
                  public string Text = "";
                  public static string StaticText = "";
                  public string WriteOnly { set { } }
                  public string PrivateGetter { private get; set; } = "";
              }
              public class Definition
              {
                  void Build(ValidatorBuilder<Model> b, Model outer) { {{call}}; }
              }
              """;

        var probe = new AccessorProbe();
        var driver = CSharpGeneratorDriver.Create(probe);
        driver.RunGeneratorsAndUpdateCompilation(GeneratorTestHost.Compile(source), out _, out var diagnostics);
        Assert.DoesNotContain(diagnostics, x => x.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    // An app extension can share a rule's name and builder return type while its lambda means something else, here a
    // length read from the model. The generator emitted an accessor that read Count into a string and didn't compile
    // (CS0029), which the app couldn't avoid without turning the generator off.
    public void AppExtensionWithARulesNameAndBuilder_IsNotOurRule()
    {
        var (output, source) = GeneratorTestHost.RunGenerator(
            """
            using System;
            using Sharp.Validator;

            public class Model { public string? Name { get; set; } public int Count { get; set; } }

            public static class AppRules
            {
                public static PropertyValidatorBuilder<TModel, string?> Equal<TModel>(
                    this PropertyValidatorBuilder<TModel, string?> builder, Func<TModel, int> length) =>
                    builder.Must((value, context) => value is null || value.Length == length(context.Model));
            }

            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(b => b.Property(x => x.Name).Equal(x => x.Count)) { }
            }
            """);

        GeneratorTestHost.AssertCompiles(output);
        Assert.Contains("\"Name\"", source);
        Assert.DoesNotContain("\"Count\"", source);
    }

    [Theory]
    [InlineData("dynamic", "Other")]
    [InlineData("int", "Sharp.Validator")]
    [InlineData("PropertyValidatorBuilder<Model, string>", "Other")]
    [InlineData("ElementValidatorBuilder<Model, string>", "Other")]
    [InlineData("ElementPropertyValidatorBuilder<Model, Model, string>", "Other")]
    [InlineData("PropertyValidatorBuilder<Model, string>", "Sharp.Validator")]
    public void SameNamedBuilders_FromOtherAssemblies_AreNotOurAccessors(string result, string ns)
    {
        var source =
            $$"""
              using System;
              using {{ns}};
              public class Model { public string Text = ""; }
              namespace {{ns}}
              {
                  public class PropertyValidatorBuilder<T, P> { }
                  public class ElementValidatorBuilder<T, P> { }
                  public class ElementPropertyValidatorBuilder<T, E, P> { }
              }
              public class Definition
              {
                  public {{result}} Property(Func<Model, string> selector) => default;
                  public void Build() => this.Property(x => x.Text);
              }
              """;

        var probe = new AccessorProbe();
        var driver = CSharpGeneratorDriver.Create(probe);
        driver.RunGeneratorsAndUpdateCompilation(GeneratorTestHost.Compile(source), out _, out var diagnostics);
        Assert.DoesNotContain(diagnostics, x => x.Severity == DiagnosticSeverity.Error);
        Assert.Empty(probe.Accessors);
    }

    [Fact]
    public void ArrayModelOrValueTypeMemberChain_GeneratesCompilableAccessors()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;
            public struct Coordinates { public int X; }
            public class Model { public Coordinates Position; public string[] Texts = []; }
            public class Rules : Validator<Model>
            {
                public Rules() : base(b =>
                {
                    b.Property(x => x.Position.X).GreaterThan(0);
                    b.Property(x => x.Texts).NotNull();
                    b.Elements(x => x.Texts).NotEmpty();
                }) { }
            }
            """);

        GeneratorTestHost.AssertCompiles(output);
        Assert.Contains("Position.X", generated);
        Assert.Contains("RegisterElements", generated);
    }

    [Fact]
    public void ValidatorShapeOrArrayAccessibility_FollowsCompilerSymbols()
    {
        var compilation = GeneratorTestHost.Compile(
            """
            public abstract class Abstract { }
            public class Generic<T> { }
            public struct Value { }
            public class Ordinary { }
            file class Hidden { }
            """);

        foreach (var name in new[] { "Abstract", "Generic`1", "Value" })
            Assert.False(Discovery.IsRegistrableValidatorShape(compilation.GetTypeByMetadataName(name)!));

        Assert.True(Discovery.IsRegistrableValidatorShape(compilation.GetTypeByMetadataName("Ordinary")!));

        var ordinary = compilation.GetTypeByMetadataName("Ordinary")!;
        Assert.True(Discovery.IsAccessibleFromGeneratedCode(compilation.CreateArrayTypeSymbol(ordinary), compilation));

        var hidden = compilation.Assembly.GlobalNamespace.GetTypeMembers().Single(x => x.IsFileLocal);
        Assert.False(Discovery.IsAccessibleFromGeneratedCode(compilation.CreateArrayTypeSymbol(hidden), compilation));

        var list = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1")!;
        Assert.False(Discovery.IsAccessibleFromGeneratedCode(list.Construct(hidden), compilation));
    }

    private sealed class AccessorProbe : IIncrementalGenerator
    {
        internal List<AccessorInfo> Accessors { get; } = [];

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var accessors = context.SyntaxProvider.CreateSyntaxProvider(
                (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax, ArgumentList.Arguments.Count: > 0 },
                Discovery.GetAccessors);

            context.RegisterSourceOutput(accessors, (_, items) => Accessors.AddRange(items.AsImmutableArray()));
        }
    }
}