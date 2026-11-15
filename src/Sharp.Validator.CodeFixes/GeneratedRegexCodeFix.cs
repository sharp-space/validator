using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace Sharp.Validator.CodeFixes;

/// <summary>
/// Converts a <c>Regex("&lt;literal&gt;"[, options])</c> rule (flagged by SVL010) into a hand-written <c>[GeneratedRegex]</c> partial method plus a
/// <c>Regex(&lt;method&gt;())</c> call — moving the pattern onto the .NET Regex source generator so it is AOT-fast. Offers nothing where the call can't
/// become an attribute: the <c>matchTimeout</c> overload (<c>[GeneratedRegex]</c> takes milliseconds, not a <c>TimeSpan</c>), options that aren't
/// constant at type scope, and call sites with no host type. The rule and the generated method both ignore case in the invariant culture, so the options
/// carry over as written.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp)]
[Shared]
public sealed class GeneratedRegexCodeFix : CodeFixProvider
{
    private const string GeneratedRegexFqn = "global::System.Text.RegularExpressions.GeneratedRegex";
    private const string RegexFqn = "global::System.Text.RegularExpressions.Regex";
    private const string RegexOptionsNoneFqn = "global::System.Text.RegularExpressions.RegexOptions.None";

    // Regex("…") runs under RegexValidator.DefaultMatchTimeout, while [GeneratedRegex] has no timeout unless told
    // otherwise. Dropping it would turn this fix into a silent removal of the ReDoS guard.
    private const int DefaultMatchTimeoutMilliseconds = 2000;

    public override ImmutableArray<string> FixableDiagnosticIds => ["SVL010"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        RegisterCodeFixes(context, root, semanticModel);
    }

    // Registration also receives the nullable workspace snapshot explicitly: editors may not have a root or model yet.
    internal static void RegisterCodeFixes(CodeFixContext context, SyntaxNode? root, SemanticModel? semanticModel)
    {
        var diagnostic = context.Diagnostics[0];
        var node = root?.FindNode(diagnostic.Location.SourceSpan);

        // The analyzer also flags inline `new Regex("literal")` passed to the Regex(Regex) overload; that shape has no
        // regexString argument to lift, so no automatic fix is offered (only walk up to the rule invocation — an
        // enclosing `new Validator<T>(...)` further out must not disable the fix).
        for (var current = node; current is not null and not InvocationExpressionSyntax; current = current.Parent)
            if (current is ObjectCreationExpressionSyntax)
                return;

        var invocation = node?.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        // Only offer the fix where a host type exists to hold the generated method.
        if (invocation?.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } typeDeclaration) return;

        if (semanticModel?.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation operation ||
            !TryReadArguments(operation, out var arguments))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to [GeneratedRegex] for NativeAOT",
                cancellationToken => ConvertAsync(context.Document, invocation, typeDeclaration, arguments, cancellationToken),
                "SVL010-GeneratedRegex"),
            diagnostic);
    }

    private static bool TryReadArguments(IInvocationOperation operation, out RuleArguments arguments)
    {
        arguments = default;
        ExpressionSyntax? pattern = null, options = null;
        ArgumentSyntax? configure = null;
        foreach (var argument in operation.Arguments)
        {
            if (argument.Syntax is not ArgumentSyntax syntax) continue;

            switch (argument.Parameter?.Name)
            {
                case "regexString":
                    // The analyzer only flags constant patterns, so a local const can be inlined as its value.
                    pattern = ReferencesLocal(argument.Value)
                        ? SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal((string)argument.Value.ConstantValue.Value!))
                        : syntax.Expression;

                    break;

                case "regexOptions" when !argument.IsImplicit:
                    if (!argument.Value.ConstantValue.HasValue || ReferencesLocal(argument.Value)) return false;

                    options = syntax.Expression;
                    break;

                // The timeout overload can't round-trip into [GeneratedRegex], which takes a millisecond int.
                case "matchTimeout":
                    return false;

                case "configure" when !argument.IsImplicit:
                    configure = syntax;
                    break;
            }
        }

        if (pattern is null) return false;

        arguments = new RuleArguments(pattern, options, configure);

        return true;
    }

    // Attribute arguments bind at type scope, where a method's locals and parameters don't exist.
    private static bool ReferencesLocal(IOperation operation) =>
        operation.DescendantsAndSelf().Any(static x => x is ILocalReferenceOperation or IParameterReferenceOperation);

    private static async Task<Document> ConvertAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        TypeDeclarationSyntax typeDeclaration,
        RuleArguments arguments,
        CancellationToken cancellationToken
    )
    {
        var methodName = DeterministicMethodName(invocation);
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        // 1. Replace the call's arguments with the generated method invocation (preserving any configure callback).
        var newArguments = SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.Argument(SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(methodName))));

        if (arguments.Configure is not null) newArguments = newArguments.Add(arguments.Configure.WithoutTrivia());

        editor.ReplaceNode(invocation.ArgumentList, invocation.ArgumentList.WithArguments(newArguments));

        // 2. Add the [GeneratedRegex(pattern, options, timeout)] partial method to the host type.
        var attributeArguments = SyntaxFactory.SeparatedList([
            SyntaxFactory.AttributeArgument(arguments.Pattern.WithoutTrivia()),
            SyntaxFactory.AttributeArgument(arguments.Options?.WithoutTrivia() ?? SyntaxFactory.ParseExpression(RegexOptionsNoneFqn)),
            SyntaxFactory.AttributeArgument(SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(DefaultMatchTimeoutMilliseconds)))
        ]);

        var method = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName(RegexFqn), methodName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword),
                SyntaxFactory.Token(SyntaxKind.PartialKeyword)))
            .WithAttributeLists(SyntaxFactory.SingletonList(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(SyntaxFactory.ParseName(GeneratedRegexFqn), SyntaxFactory.AttributeArgumentList(attributeArguments))))))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            .WithAdditionalAnnotations(Formatter.Annotation);

        editor.AddMember(typeDeclaration, method);

        // 3. Every containing type must be partial: a generated partial method inside a nested type requires
        // the entire containing declaration chain to participate in the same partial shape.
        foreach (var containing in ContainingTypes(typeDeclaration))
            if (!containing.Modifiers.Any(SyntaxKind.PartialKeyword))
                editor.SetModifiers(containing, editor.Generator.GetModifiers(containing).WithPartial(true));

        return editor.GetChangedDocument();
    }

    internal static IEnumerable<TypeDeclarationSyntax> ContainingTypes(TypeDeclarationSyntax declaration)
    {
        for (var containing = declaration;
             containing != null;
             containing = containing.Parent?.FirstAncestorOrSelf<TypeDeclarationSyntax>())
            yield return containing;
    }

    // Deterministic, call-site-derived name instead of a scan for the first free "GeneratedRegex{n}":
    // the batch Fix-All provider computes every fix against the original document, so two diagnostics in one type would
    // both pick the same free index and merge into duplicate members; a sequential scan also can't see sibling partial
    // declarations. Hashing file:line:column is unique per call site and stable across both.
    private static string DeterministicMethodName(InvocationExpressionSyntax invocation)
    {
        // Chained invocations share the same invocation start (`x.Regex(...).Regex(...)` both start at `x`).
        // The opening parenthesis belongs to the specific call and therefore supplies a truly unique column.
        var position = invocation.ArgumentList.OpenParenToken.GetLocation().GetLineSpan();
        return $"GeneratedRegex_{Fnv1A32(
            $"{position.Path}:{position.StartLinePosition.Line}:{position.StartLinePosition.Character}"):X8}";
    }

    private static uint Fnv1A32(string text)
    {
        var hash = 2166136261u;
        foreach (var character in text)
            hash = (hash ^ character) * 16777619u;

        return hash;
    }

    private readonly struct RuleArguments(ExpressionSyntax pattern, ExpressionSyntax? options, ArgumentSyntax? configure)
    {
        public ExpressionSyntax Pattern { get; } = pattern;

        public ExpressionSyntax? Options { get; } = options;

        public ArgumentSyntax? Configure { get; } = configure;
    }
}