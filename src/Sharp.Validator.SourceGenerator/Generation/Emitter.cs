using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Sharp.Validator.SourceGenerator.Generation;

/// <summary>
/// Emits the AOT-safe <c>[ModuleInitializer]</c> registrars into the consumer assembly. The class is partial and split by concern:
/// <c>Emitter.Validators.cs</c> emits the DI registrations (SVL001 for skipped inaccessible validators is reported by
/// <c>InaccessibleValidatorAnalyzer</c>) and <c>Emitter.Accessors.cs</c> emits the precompiled property accessors. Each writes its own <c>.g.cs</c> so
/// the two pipelines never entangle.
/// </summary>
internal static partial class Emitter
{
    private const string ToolName = "Sharp.Validator.SourceGenerator";

    // [GeneratedCode]'s version comes from <Version> in Directory.Build.props — the SDK bakes it into this generator
    // assembly's AssemblyInformationalVersion, which we read here instead of hardcoding a literal. Resolved once
    // (constant per build); we trim the "+<sha>" build-metadata suffix and fall back to the assembly version.
    private static readonly string ToolVersion = ResolveToolVersion(typeof(Emitter).Assembly);

    internal static string ResolveToolVersion(Assembly assembly) => NormalizeToolVersion(
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        assembly.GetName().Version);

    // Separate metadata lookup from normalization so assemblies without optional version attributes are testable.
    internal static string NormalizeToolVersion(string? informational, Version? assemblyVersion)
    {
        if (informational is { Length: > 0 })
        {
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational.Substring(0, plus);
        }

        return assemblyVersion?.ToString() ?? "1.0.0.0";
    }

    // Stamps the standard attributes on a generated type: [GeneratedCode] attributes it to the tool,
    // [ExcludeFromCodeCoverage] keeps the module-initializer scaffolding out of coverage (test/testconfig.json excludes
    // by this attribute), and [DebuggerNonUserCode] makes the debugger step over it. [Obsolete] makes the type an
    // obsolete context, where the compiler reports no use of an obsolete member or type: the app may keep validating a
    // member it marked obsolete, or registering a validator it did, and silenced the warning in its own code, but it
    // can't reach into this file. That covers CS0612, CS0618, CS0619 (error: true) and a custom DiagnosticId alike. The
    // registrar is file-local and only its module initializer runs it, so nothing reports a use of the type itself.
    // Written by both registrar emitters for identical output.
    private static void AppendGeneratedCodeAttributes(StringBuilder builder, string indent)
    {
        builder.AppendLine($"{indent}[global::System.CodeDom.Compiler.GeneratedCode(\"{ToolName}\", \"{ToolVersion}\")]");
        builder.AppendLine($"{indent}[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        builder.AppendLine($"{indent}[global::System.Diagnostics.DebuggerNonUserCode]");
        builder.AppendLine($"{indent}[global::System.Obsolete(\"Generated registration code; it reads what the app chose to keep.\")]");
    }

    // [Experimental] isn't silenced by an obsolete context, only by a #pragma for its ID, so each generated file turns
    // off the IDs its own references would raise.
    private static void AppendExperimentalPragma(StringBuilder builder, IEnumerable<EquatableArray<string>> ids)
    {
        var all = ids.SelectMany(static set => set.AsImmutableArray()).Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal).ToList();

        if (all.Count > 0) builder.AppendLine($"#pragma warning disable {string.Join(", ", all)}");
    }
}