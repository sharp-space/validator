using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sharp.Validator.CodeFixes.Tests.Fixtures;

namespace Sharp.Validator.CodeFixes.Tests;

public sealed class GeneratedRegexCodeFixGuardTests
{
    [Fact]
    public void FixAllAndDiagnosticContract_AreAvailable()
    {
        var fixer = new GeneratedRegexCodeFix();
        Assert.Equal(["SVL010"], fixer.FixableDiagnosticIds);
        Assert.Same(WellKnownFixAllProviders.BatchFixer, fixer.GetFixAllProvider());
    }

    [Theory]
    [InlineData("class C { string Text = \"marker\"; }", "marker")]
    [InlineData("new System.Text.RegularExpressions.Regex(\"marker\");", "marker")]
    [InlineData("class C { void M() { Unknown(\"marker\"); } }", "marker")]
    [InlineData("class C { void M() { System.Console.WriteLine(\"marker\"); } }", "marker")]
    [InlineData("System.Console.WriteLine(\"marker\");", "marker")]
    public async Task UnrelatedOrIncompleteDiagnostics_DoNotOfferAnInvalidFix(string source, string marker)
    {
        using var workspace = new AdhocWorkspace();
        var document = Documents.Create(workspace, source);
        var actions = await Actions(document, marker);
        Assert.Empty(actions);
    }

    [Fact]
    public async Task Fix_ExplicitConfigureCallbackOrPartialContainer_IsPreserved()
    {
        using var workspace = new AdhocWorkspace();
        var document = Documents.Create(workspace,
            """
            using Sharp.Validator;
            public class Model { public string Text { get; set; } = ""; }
            public partial class Rules : Validator<Model>
            {
                public Rules() : base(b => b.Property(x => x.Text).Regex("marker", configure: options => options.ErrorCode = "Custom")) { }
            }
            """);

        var actions = await Actions(document, "marker");
        var operations = await Assert.Single(actions).GetOperationsAsync(TestContext.Current.CancellationToken);

        var updated = Assert.Single(operations.OfType<ApplyChangesOperation>()).ChangedSolution.GetDocument(document.Id)!;
        var source = (await updated.GetTextAsync(TestContext.Current.CancellationToken)).ToString();
        Assert.Contains("configure: options => options.ErrorCode = \"Custom\"", source);
        Assert.Contains("GeneratedRegex(\"marker\"", source);
    }

    [Fact]
    public async Task MissingWorkspaceSnapshotOrDetachedType_OffersNoFixWithOneContainer()
    {
        using var workspace = new AdhocWorkspace();
        var document = Documents.Create(workspace, "class C { void M() { System.Console.WriteLine(\"marker\"); } }");
        var root = (await document.GetSyntaxRootAsync(TestContext.Current.CancellationToken))!;
        var literal = root.DescendantNodes().OfType<LiteralExpressionSyntax>().Single();
        var descriptor = new DiagnosticDescriptor("SVL010", "Regex", "Regex", "Test", DiagnosticSeverity.Info, true);
        var diagnostic = Diagnostic.Create(descriptor, literal.GetLocation());
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), TestContext.Current.CancellationToken);

        GeneratedRegexCodeFix.RegisterCodeFixes(context, null, null);
        GeneratedRegexCodeFix.RegisterCodeFixes(context, root, null);

        Assert.Empty(actions);
        Assert.Single(GeneratedRegexCodeFix.ContainingTypes(SyntaxFactory.ClassDeclaration("Detached")));
    }

    private static async Task<List<CodeAction>> Actions(Document document, string marker)
    {
        var root = (await document.GetSyntaxRootAsync(TestContext.Current.CancellationToken))!;
        var literal = root.DescendantNodes().OfType<LiteralExpressionSyntax>().First(x => x.Token.ValueText == marker);
        var descriptor = new DiagnosticDescriptor("SVL010", "Regex", "Regex", "Test", DiagnosticSeverity.Info, true);
        var diagnostic = Diagnostic.Create(descriptor, literal.GetLocation());
        var actions = new List<CodeAction>();

        await new GeneratedRegexCodeFix().RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action),
                TestContext.Current.CancellationToken));

        return actions;
    }
}