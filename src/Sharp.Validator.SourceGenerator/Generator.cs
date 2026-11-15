using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sharp.Validator.SourceGenerator.Generation;

namespace Sharp.Validator.SourceGenerator;

/// <summary>
/// Incremental generator that replaces Sharp.Validator's runtime assembly-scan reflection: it discovers <c>IValidator&lt;T&gt;</c> implementations at
/// compile time and emits closed-generic, AOT-safe DI registrations wired through a <c>[ModuleInitializer]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ValidatorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var validators = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) =>
                    node is ClassDeclarationSyntax or RecordDeclarationSyntax
                    && ((TypeDeclarationSyntax)node).BaseList is not null,
                static (syntaxContext, cancellationToken) => GetValidators(syntaxContext, cancellationToken))
            .Where(static x => x is not null)
            .Select(static (infos, _) => infos!.Value);

        context.RegisterSourceOutput(
            validators.Collect(),
            static (productionContext, sets) => Emitter.EmitValidators(productionContext, [.. sets.SelectMany(static x => x.AsImmutableArray())]));

        // A second, independent pipeline that precompiles flat Property(x => x.Member) accessors. AccessorInfo is a
        // string-only record struct, so the Collect() boundary compares element-wise and unrelated edits don't re-emit.
        // Kept separate from the validator pipeline (its own .g.cs) so the two never entangle.
        var accessors = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => Discovery.IsCandidate(node),
                static (syntaxContext, cancellationToken) => Discovery.GetAccessors(syntaxContext, cancellationToken))
            .SelectMany(static (infos, _) => infos.AsImmutableArray());

        context.RegisterSourceOutput(accessors.Collect(), static (productionContext, items) => Emitter.EmitAccessors(productionContext, items));
    }

    private static EquatableArray<ValidatorInfo>? GetValidators(GeneratorSyntaxContext context, CancellationToken cancellationToken) =>
        GetValidators(context.SemanticModel.GetDeclaredSymbol((TypeDeclarationSyntax)context.Node, cancellationToken) as INamedTypeSymbol,
            context.SemanticModel.Compilation);

    internal static EquatableArray<ValidatorInfo>? GetValidators(INamedTypeSymbol? symbol, Compilation compilation)
    {
        if (symbol is null) return null;

        // Only concrete, non-generic classes register. Matches the reflection scan's contract (validators are classes)
        // and skips the open-generic Validator<T> base.
        if (!Discovery.IsRegistrableValidatorShape(symbol))
            return null;

        var infos = Discovery.GetValidators(symbol, compilation).ToImmutableArray();

        // Wrap in EquatableArray so this transform's output compares by content — otherwise the bare ImmutableArray
        // compares by reference and the downstream emission re-runs on every edit.
        return infos.Length > 0 ? new EquatableArray<ValidatorInfo>(infos) : null;
    }
}