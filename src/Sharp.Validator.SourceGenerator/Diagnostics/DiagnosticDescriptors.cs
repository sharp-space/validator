using Microsoft.CodeAnalysis;
using Sharp.Validator.SourceGenerator.Analyzers;

namespace Sharp.Validator.SourceGenerator.Diagnostics;

internal static class DiagnosticDescriptors
{
    internal static readonly DiagnosticDescriptor RegexRule = new(
        RegexRuleAnalyzer.DiagnosticId,
        "Use [GeneratedRegex] for a literal Regex pattern",
        "Literal Regex pattern runs interpreted under NativeAOT; convert it to a [GeneratedRegex] method",
        "Sharp.Validator",
        DiagnosticSeverity.Info,
        true,
        "Reported when PublishAot is true. Under NativeAOT a runtime-built Regex runs interpreted, because " +
        "RegexOptions.Compiled is unavailable, and keeps the regex parser in the binary. A [GeneratedRegex] " +
        "method compiles to matching code and is the fastest option on one thread, but it is one shared " +
        "instance, where a literal pattern spreads over one instance per core, so it tops out near a single " +
        "core's throughput under sustained parallel load.");

    internal static readonly DiagnosticDescriptor CrossPropertyTarget = new(
        CrossPropertyTargetAnalyzer.DiagnosticId,
        "Cross-property target is compiled at runtime",
        "Cross-property target '{0}' is not a member chain, so it is compiled when the validator is built: " +
        "NativeAOT runs it interpreted, and trimming can remove an operator it needs; compare against a " +
        "property, or check the value with Must((value, context) => ...)",
        "Sharp.Validator",
        DiagnosticSeverity.Warning,
        true,
        "Reported when EnableTrimAnalyzer is true, as PublishTrimmed, PublishAot, IsTrimmable, and IsAotCompatible " +
        "make it. A target that is a member chain rooted at the lambda parameter, such as x => x.Policy.Min, reads " +
        "through an accessor the source generator precompiles. Any other target, such as x => x.Minimum * 2, is an " +
        "expression tree compiled for each validator instance. NativeAOT runs it in the expression interpreter, at " +
        "about four times the time and some 200 more bytes per validation, and trimming can remove an operator " +
        "the runtime looks up by reflection, such as * on decimal, so building the validator throws.");

    internal static readonly DiagnosticDescriptor InaccessibleValidator = new(
        InaccessibleValidatorAnalyzer.DiagnosticId,
        "Validator is not registered by assembly scanning",
        "Validator '{0}' or its model is private, protected, or file-local, or nested in such a type, so " +
        "AddValidators skips it; make them public or internal, or register it with AddValidator<T>()",
        "Sharp.Validator",
        DiagnosticSeverity.Warning,
        true,
        "AddValidators registers an assembly's validators through its generated registrar whenever the " +
        "assembly has one, under the JIT as well as NativeAOT, and generated code can't name these types. " +
        "The reflection scan for an assembly with no registrar, which runs only under the JIT, skips the " +
        "same types.");
}