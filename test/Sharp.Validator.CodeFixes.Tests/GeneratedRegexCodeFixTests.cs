using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Sharp.Validator.CodeFixes.Tests.Fixtures;
using Sharp.Validator.Fixtures;
using Sharp.Validator.SourceGenerator.Analyzers;
using Sharp.Validator.Validators;

namespace Sharp.Validator.CodeFixes.Tests;

public sealed class GeneratedRegexCodeFixTests
{
    private static int DefaultTimeoutMilliseconds => (int)RegexValidator.DefaultMatchTimeout.TotalMilliseconds;

    [Fact]
    public async Task ParameterNamePatterns_AreInlinedInTheGeneratedAttribute()
    {
        var fixedSource = await ApplyFixAsync(
            """
            using Sharp.Validator;
            public sealed class Model { public string? Name { get; set; } }
            public class ModelValidator : Validator<Model>
            {
                public ModelValidator(string pattern)
                {
                    Builder.Property(m => m.Name).Regex(nameof(pattern));
                }
            }
            """);

        Assert.Contains("GeneratedRegex(\"pattern\"", fixedSource);
    }

    [Fact]
    public void SameLineFixAllCallSites_ReceiveUniqueMethodNames()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { void M() { x.Regex(\"^a\").Regex(\"b$\"); } }");

        var invocations = tree.GetRoot().DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(x => x.Expression.ToString().EndsWith(".Regex", StringComparison.Ordinal))
            .ToArray();

        var method = typeof(GeneratedRegexCodeFix).GetMethod(
            "DeterministicMethodName", BindingFlags.Static | BindingFlags.NonPublic)!;

        var names = invocations.Select(x => (string)method.Invoke(null, [x])!).ToArray();
        Assert.Equal(2, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task NestedFix_MarksEveryContainingTypePartial()
    {
        const string source =
            """
            using Sharp.Validator;

            public sealed class Model { public string? Name { get; set; } }
            public class Outer
            {
                public class Inner : Validator<Model>
                {
                    public Inner() : base(x => x.Property(m => m.Name).Regex("^[a-z]+$")) { }
                }
            }
            """;

        using var workspace = new AdhocWorkspace();
        var document = Documents.Create(workspace, source);
        var compilation = await document.Project.GetCompilationAsync(TestContext.Current.CancellationToken);
        var errors = compilation!.GetDiagnostics()
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(errors.Length == 0, string.Join('\n', errors.Select(x => x.ToString())));

        var diagnostics = await compilation.WithAnalyzers([new RegexRuleAnalyzer()], BuildProperties.Analyzer(BuildProperties.PublishAot))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);

        var actions = new List<CodeAction>();
        var fixer = new GeneratedRegexCodeFix();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            TestContext.Current.CancellationToken);

        await fixer.RegisterCodeFixesAsync(context);

        var action = Assert.Single(actions);

        var operations = await action.GetOperationsAsync(TestContext.Current.CancellationToken);
        var changedSolution = Assert.Single(operations.OfType<ApplyChangesOperation>()).ChangedSolution;
        var changedDocument = changedSolution.GetDocument(document.Id)!;
        var root = await changedDocument.GetSyntaxRootAsync(TestContext.Current.CancellationToken);
        var declarations = root!.DescendantNodes().OfType<TypeDeclarationSyntax>().ToArray();
        Assert.Contains(SyntaxKind.PartialKeyword, declarations.Single(x => x.Identifier.ValueText == "Outer").Modifiers.Select(x => x.Kind()));
        Assert.Contains(SyntaxKind.PartialKeyword, declarations.Single(x => x.Identifier.ValueText == "Inner").Modifiers.Select(x => x.Kind()));

        Assert.Contains(root.DescendantNodes().OfType<MethodDeclarationSyntax>(),
            method => method.Identifier.ValueText.StartsWith("GeneratedRegex_", StringComparison.Ordinal));
    }

    [Fact]
    // Regression: the fix replaced Regex("…"), which carries the 2-second ReDoS guard, with a [GeneratedRegex] that has
    // no timeout, so accepting the suggestion silently removed the guard.
    public async Task Fix_KeepsTheDefaultMatchTimeout()
    {
        var fixedSource = await ApplyFixAsync(
            """
            using Sharp.Validator;

            public sealed class Model { public string? Name { get; set; } }
            public class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.Name).Regex("^[a-z]+$")) { }
            }
            """);

        Assert.Contains($"RegexOptions.None, {DefaultTimeoutMilliseconds}", fixedSource);
    }

    [Fact]
    // Regex(pattern, configure) is an overload of its own, without options; the fix reads its pattern by the same
    // parameter name and keeps its configure.
    public async Task Fix_KeepsTheConfigureOfThePatternOnlyOverload()
    {
        var fixedSource = await ApplyFixAsync(
            """
            using Sharp.Validator;

            public sealed class Model { public string? Name { get; set; } }
            public class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.Name).Regex("^[a-z]+$", o => o.ErrorMessage = "letters")) { }
            }
            """);

        Assert.Contains($"RegexOptions.None, {DefaultTimeoutMilliseconds}", fixedSource);
        Assert.Contains("o => o.ErrorMessage = \"letters\"", fixedSource);
    }

    [Fact]
    public async Task Fix_KeepsExplicitOptionsAlongsideTheTimeout()
    {
        var fixedSource = await ApplyFixAsync(
            """
            using System.Text.RegularExpressions;
            using Sharp.Validator;

            public sealed class Model { public string? Name { get; set; } }
            public class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.Name).Regex("^[a-z]+$", RegexOptions.Multiline)) { }
            }
            """);

        Assert.Contains($"RegexOptions.Multiline, {DefaultTimeoutMilliseconds}", fixedSource);
    }

    [Theory]
    [InlineData("\"^[a-z]+$\", RegexOptions.IgnoreCase",
        "RegexOptions.IgnoreCase")]
    [InlineData("\"(?i)^[a-z]+$\"",
        "\"(?i)^[a-z]+$\", global::System.Text.RegularExpressions.RegexOptions.None")]
    // The rule and a [GeneratedRegex] both ignore case in the invariant culture, so a case-insensitive rule's options
    // carry over as written, whether IgnoreCase comes from the option or from (?i).
    public async Task Fix_KeepsTheOptionsOfARuleThatIgnoresCase(string arguments, string expected)
    {
        var fixedSource = await ApplyFixAsync(
            $$"""
              using System.Text.RegularExpressions;
              using Sharp.Validator;

              public sealed class Model { public string? Name { get; set; } }
              public class ModelValidator : Validator<Model>
              {
                  public ModelValidator() : base(x => x.Property(m => m.Name).Regex({{arguments}})) { }
              }
              """);

        Assert.Contains($"{expected}, {DefaultTimeoutMilliseconds}", fixedSource);
    }

    [Fact]
    public async Task Fix_KeepsOptionsThatAlreadyIgnoreCaseInTheInvariantCulture()
    {
        var fixedSource = await ApplyFixAsync(
            """
            using System.Text.RegularExpressions;
            using Sharp.Validator;

            public sealed class Model { public string? Name { get; set; } }
            public class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.Name)
                    .Regex("^[a-z]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) { }
            }
            """);

        Assert.Contains($"RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, {DefaultTimeoutMilliseconds}", fixedSource);
    }

    [Fact]
    // Regression: attribute arguments bind at type scope, so a copied local produced CS0103 after the fix, and the
    // timeout overload was offered a fix that changed nothing.
    public async Task NonConstantOptions_OfferNoFix() => Assert.Empty(await OfferedFixesAsync(
        """
        using System.Text.RegularExpressions;
        using Sharp.Validator;

        public sealed class Model { public string? Name { get; set; } }
        public class ModelValidator : Validator<Model>
        {
            public ModelValidator()
            {
                var options = RegexOptions.IgnoreCase;
                Builder.Property(m => m.Name).Regex("^[a-z]+$", options);
            }
        }
        """));

    [Fact]
    public async Task LocalConstantOptions_OfferNoFix() => Assert.Empty(await OfferedFixesAsync(
        """
        using System.Text.RegularExpressions;
        using Sharp.Validator;

        public sealed class Model { public string? Name { get; set; } }
        public class ModelValidator : Validator<Model>
        {
            public ModelValidator()
            {
                const RegexOptions options = RegexOptions.IgnoreCase;
                Builder.Property(m => m.Name).Regex("^[a-z]+$", options);
            }
        }
        """));

    [Fact]
    public async Task TimeoutOverload_OffersNoFix() => Assert.Empty(await OfferedFixesAsync(
        """
        using System;
        using System.Text.RegularExpressions;
        using Sharp.Validator;

        public sealed class Model { public string? Name { get; set; } }
        public class ModelValidator : Validator<Model>
        {
            public ModelValidator() : base(x => x.Property(m => m.Name)
                .Regex("^[a-z]+$", RegexOptions.None, TimeSpan.FromSeconds(1))) { }
        }
        """));

    [Fact]
    public async Task LocalConstantPattern_IsInlined()
    {
        var fixedSource = await ApplyFixAsync(
            """
            using Sharp.Validator;

            public sealed class Model { public string? Name { get; set; } }
            public class ModelValidator : Validator<Model>
            {
                public ModelValidator()
                {
                    const string pattern = "^[a-z]+$";
                    Builder.Property(m => m.Name).Regex(pattern);
                }
            }
            """);

        Assert.Contains("GeneratedRegex(\"^[a-z]+$\"", fixedSource);
    }

    private static async Task<string> ApplyFixAsync(string source)
    {
        using var workspace = new AdhocWorkspace();
        var document = Documents.Create(workspace, source);
        var operations = await Assert.Single(await OfferedFixesAsync(document))
            .GetOperationsAsync(TestContext.Current.CancellationToken);

        var changed = Assert.Single(operations.OfType<ApplyChangesOperation>()).ChangedSolution.GetDocument(document.Id)!;
        return (await changed.GetTextAsync(TestContext.Current.CancellationToken)).ToString();
    }

    private static async Task<List<CodeAction>> OfferedFixesAsync(string source)
    {
        using var workspace = new AdhocWorkspace();
        return await OfferedFixesAsync(Documents.Create(workspace, source));
    }

    private static async Task<List<CodeAction>> OfferedFixesAsync(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync(TestContext.Current.CancellationToken);
        var diagnostics = await compilation!.WithAnalyzers([new RegexRuleAnalyzer()], BuildProperties.Analyzer(BuildProperties.PublishAot))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        var actions = new List<CodeAction>();
        await new GeneratedRegexCodeFix().RegisterCodeFixesAsync(new CodeFixContext(
            document, Assert.Single(diagnostics), (action, _) => actions.Add(action), TestContext.Current.CancellationToken));

        return actions;
    }
}