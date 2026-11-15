using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sharp.Validator.Fixtures;
using Sharp.Validator.SourceGenerator.Generation;
using Sharp.Validator.SourceGenerator.Tests.Fixtures;

namespace Sharp.Validator.SourceGenerator.Tests.Generator;

// Generated code names the members, models and validators an app picked, and the app can't put a #pragma into a
// generated file. An app that kept validating a member it marked [Obsolete] or [Experimental], or kept registering a
// validator it marked obsolete, and silenced that in its own code, still had its build broken from the generated files:
// CS0612, CS0618, CS0619 or a custom DiagnosticId for an obsolete symbol, and the attribute's ID, as an error, for an
// experimental one. These pin that the generated files raise none of them and still register and read every symbol.
public sealed class GeneratedCodeWarningTests
{
    [Fact]
    public void ObsoleteMembersAndValidators_RaiseNothingInGeneratedCode()
    {
        var (output, source) = GeneratorTestHost.RunGenerator(
            """
            using System;
            using Sharp.Validator;

            public class Model
            {
                [Obsolete] public string? Plain { get; set; }
                [Obsolete("use Name")] public string? WithMessage { get; set; }
                [Obsolete("use Name", DiagnosticId = "LIB0001")] public string? WithId { get; set; }
                public string? Name { get; set; }
            }

            #pragma warning disable CS0612, CS0618, LIB0001
            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(b =>
                {
                    b.Property(x => x.Plain).NotEmpty();
                    b.Property(x => x.WithMessage).NotEmpty();
                    b.Property(x => x.WithId).NotEmpty();
                }) { }
            }
            #pragma warning restore CS0612, CS0618, LIB0001

            [Obsolete("replaced by ModelValidator", true)]
            public sealed class LegacyValidator : Validator<Model>
            {
                public LegacyValidator() : base(b => b.Property(x => x.Name).NotEmpty()) { }
            }
            """);

        Assert.Empty(GeneratorTestHost.GeneratedDiagnostics(output));
        Assert.Contains("typeof(global::LegacyValidator)", source);
        Assert.Contains("\"WithId\"", source);
    }

    [Fact]
    public void ExperimentalMembersContainingTypesAndModels_RaiseNothingInGeneratedCode()
    {
        var (output, source) = GeneratorTestHost.RunGenerator(
            """
            using System.Collections.Generic;
            using System.Diagnostics.CodeAnalysis;
            using Sharp.Validator;

            [Experimental("APP002")] public class BetaChild { public string? Name { get; set; } }
            [Experimental("APP003")] public class BetaModel { public string? Name { get; set; } }

            #pragma warning disable APP001, APP002, APP003
            public class Model
            {
                [Experimental("APP001")] public string? Beta { get; set; }
                public BetaChild Child { get; set; } = new();
                public BetaChild[]? Children { get; set; }
                public List<BetaChild>? Items { get; set; }
            }

            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(b =>
                {
                    b.Property(x => x.Beta).NotEmpty();
                    b.Property(x => x.Child.Name).NotEmpty();
                    b.Property(x => x.Children).NotNull();
                    b.Property(x => x.Items).NotNull();
                }) { }
            }

            public sealed class BetaValidator : Validator<BetaModel>
            {
                public BetaValidator() : base(b => b.Property(x => x.Name).NotEmpty()) { }
            }
            #pragma warning restore APP001, APP002, APP003
            """);

        Assert.Empty(GeneratorTestHost.GeneratedDiagnostics(output));
        Assert.Contains("#pragma warning disable APP001, APP002, APP003", source);
        Assert.Contains("typeof(global::BetaValidator)", source);
    }

    [Fact]
    public void ModelFromAnExperimentalAssembly_RaisesNothingInGeneratedCode()
    {
        var library = CSharpCompilation.Create(
                "ExperimentalLibrary",
                [
                    CSharpSyntaxTree.ParseText(
                        """
                        [assembly: System.Diagnostics.CodeAnalysis.Experimental("ASM001")]
                        public class LibraryModel { public string? Name { get; set; } }
                        """)
                ],
                MetadataReferences.Trusted,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .ToMetadataReference();

        var (output, source) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;

            #pragma warning disable ASM001
            public sealed class LibraryModelValidator : Validator<LibraryModel>
            {
                public LibraryModelValidator() : base(b => b.Property(x => x.Name).NotEmpty()) { }
            }
            #pragma warning restore ASM001
            """, library);

        Assert.Empty(GeneratorTestHost.GeneratedDiagnostics(output));
        Assert.Contains("#pragma warning disable ASM001", source);
    }

    [Fact]
    // An ID that isn't an identifier can't be written in a #pragma, by the app or here, so it is left out rather than
    // written into a line that would itself warn. A malformed attribute contributes nothing either.
    public void ExperimentalIdsThatCannotAppearInAPragma_AreLeftOut()
    {
        var compilation = GeneratorTestHost.Compile(
            """
            using System.Diagnostics.CodeAnalysis;
            public class Model
            {
                [Experimental("NOT-AN-IDENTIFIER")] public string? Hyphen { get; set; }
                [Experimental(null!)] public string? Missing { get; set; }
                [Experimental] public string? Malformed { get; set; }
            }
            """);

        var model = compilation.GetTypeByMetadataName("Model")!;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in model.GetMembers())
            Discovery.CollectExperimentalIds(member, ids);

        Discovery.CollectExperimentalIds(compilation.DynamicType, ids);
        Discovery.CollectExperimentalIds(null, ids);

        Assert.Empty(ids);
        Assert.False(Discovery.IsExperimentalAttribute(null));
        Assert.False(Discovery.IsLibraryCall(null));
    }
}