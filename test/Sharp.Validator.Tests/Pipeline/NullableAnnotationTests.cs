using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sharp.Validator.Fixtures;

namespace Sharp.Validator.Tests.Pipeline;

// The warnings-as-errors build pins the declarations that must compile clean. These compile declarations with nullable
// analysis on and pin the other half: a chain keeps its property's annotation through the rules, so a predicate on a
// nullable value warns, a rule that can't take null rejects a nullable chain, and a fixed comparison value that the
// rule would throw on is flagged where it is written. A value type takes no nullable target at all.
public sealed class NullableAnnotationTests
{
    private const string Source =
        """
        #nullable enable
        using System.Collections.Generic;
        using Sharp.Validator;

        public sealed class Address { public string City { get; set; } = ""; }

        public sealed class Model
        {
            public string Name { get; set; } = "";
            public string? Nick { get; set; }
            public Address? Work { get; set; }
            public List<string?> LooseTags { get; set; } = new();
            public int Age { get; set; }
            public int? MaybeAge { get; set; }
        }

        public sealed class LongRule<T> : PropertyValidator<T, string>
        {
            public override bool Validate(string value, ValidationContext<T> context) => value.Length > 3;
        }

        public static class Declarations
        {
            public static void Declare(ValidatorBuilder<Model> b, int? limit, string? low)
            {
                STATEMENT
            }
        }
        """;

    [Theory]
    [InlineData("b.Property(x => x.Nick).Must(v => v.Length > 0);", "CS8602")]
    [InlineData("b.Property(x => x.Nick).MinimumLength(1).Must(v => v.Length > 0);", "CS8602")]
    [InlineData("b.Property(x => x.Work).Must(v => v.City.Length > 0);", "CS8602")]
    [InlineData("b.Elements(x => x.LooseTags).Must(v => v.Length > 0);", "CS8602")]
    [InlineData("b.Property(x => x.Nick).Add(new LongRule<Model>());", "CS8620")]
    [InlineData("b.Property(x => x.MaybeAge).GreaterThan(limit);", "CS8607")]
    [InlineData("b.Property(x => x.Nick).InclusiveBetween(low, \"z\");", "CS8604")]
    [InlineData("b.Property(x => x.Name).GreaterThan(low);", "CS8604")]
    public void Declaration_ThatCanMeetNull_Warns(string statement, string id) =>
        Assert.Contains(id, Diagnostics(statement).Where(static d => d.Severity == DiagnosticSeverity.Warning).Select(static d => d.Id));

    [Theory]
    [InlineData("b.Property(x => x.Name).MinimumLength(1).Must(v => v.Length > 0);")]
    [InlineData("b.Property(x => x.Name).Add(new LongRule<Model>()).Equal(x => x.Nick).NotEqual(low);")]
    [InlineData("b.Property(x => x.MaybeAge).GreaterThan(0).InclusiveBetween(1, 9).GreaterThan(x => x.Age);")]
    public void Declaration_ThatCantMeetNull_IsClean(string statement) => Assert.Empty(Diagnostics(statement));

    [Theory]
    [InlineData("b.Property(x => x.Age).LessThan(x => x.MaybeAge);")]
    [InlineData("b.Property(x => x.Age).Equal(x => x.MaybeAge);")]
    [InlineData("b.Property(x => x.Age).Equal(limit);")]
    public void ValueTypeChain_WithANullableTarget_DoesNotCompile(string statement) =>
        Assert.Contains(Diagnostics(statement), static d => d.Severity == DiagnosticSeverity.Error);

    private static Diagnostic[] Diagnostics(string statement)
    {
        var compilation = CSharpCompilation.Create(
            "NullableAnnotations",
            [CSharpSyntaxTree.ParseText(Source.Replace("STATEMENT", statement), new CSharpParseOptions(LanguageVersion.Latest))],
            MetadataReferences.Trusted,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return [.. compilation.GetDiagnostics().Where(static d => d.Severity >= DiagnosticSeverity.Warning)];
    }
}