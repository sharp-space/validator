using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Sharp.Validator.Fixtures;

// Stands in for the .globalconfig MSBuild writes from CompilerVisibleProperty items: the build properties an analyzer
// reads. No syntax tree or additional file has any of its own.
public sealed class BuildProperties(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
{
    private static readonly Options None = new(new Dictionary<string, string>());

    /// <summary>The build property the package's buildTransitive props expose for an app published with Native AOT.</summary>
    public static readonly IReadOnlyDictionary<string, string> PublishAot =
        new Dictionary<string, string> { ["build_property.PublishAot"] = "true" };

    public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(values);

    public static AnalyzerOptions Analyzer(IReadOnlyDictionary<string, string>? values = null) =>
        new([], new BuildProperties(values ?? new Dictionary<string, string>()));

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => None;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => None;

    private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}