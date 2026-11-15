using System.Reflection;
using Sharp.Validator.SourceGenerator.Generation;
using Sharp.Validator.SourceGenerator.Tests.Fixtures;

namespace Sharp.Validator.SourceGenerator.Tests.Generator;

public sealed class MemberBindingTests
{
    [Fact]
    public void MemberReadability_ChecksGetterAccessibilitySeparatelyFromTheProperty()
    {
        var compilation = GeneratorTestHost.Compile(
            """
            public class Model
            {
                public string Field = "";
                public static string Static = "";
                private string Hidden = "";
                public string Readable { get; set; }
                public string WriteOnly { set { } }
                public string PrivateGetter { private get; set; }
                public void Method() { }
            }
            """);

        var type = compilation.GetTypeByMetadataName("Model")!;
        foreach (var name in new[] { "Field", "Readable" })
            Assert.True(Discovery.CanReadMember(type.GetMembers(name).Single(), compilation));

        foreach (var name in new[] { "Static", "Hidden", "WriteOnly", "PrivateGetter", "Method" })
            Assert.False(Discovery.CanReadMember(type.GetMembers(name).Single(), compilation));

        Assert.False(Discovery.CanReadMember(null, compilation));
    }

    [Fact]
    public void MissingBoundDeclarationAndAnUnrelatedClass_HaveNoValidatorRegistrations()
    {
        var compilation = GeneratorTestHost.Compile("public class Ordinary { }");
        Assert.Null(ValidatorGenerator.GetValidators(null, compilation));
        Assert.Null(ValidatorGenerator.GetValidators(compilation.GetTypeByMetadataName("Ordinary"), compilation));
        Assert.False(Discovery.IsRegistrableValidatorShape(null!));
    }

    [Fact]
    public void DynamicTypeOrNestedChain_IsConcreteWithEachIntermediateEmittedOnce()
    {
        var compilation = GeneratorTestHost.Compile("class C { }");
        var method = typeof(Discovery).GetMethod("IsConcrete", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True((bool)method.Invoke(null, [compilation.DynamicType])!);

        var (output, source) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;
            public class Leaf { public int Value { get; set; } }
            public class Middle { public Leaf Child { get; set; } }
            public class Model { public Middle Child { get; set; } }
            public class Rules : Validator<Model>
            { public Rules() : base(b => b.Property(x => x.Child.Child.Value).GreaterThan(0)) { } }
            """);

        GeneratorTestHost.AssertCompiles(output);
        Assert.Contains("var t1 = t0.Child;", source);
    }
}