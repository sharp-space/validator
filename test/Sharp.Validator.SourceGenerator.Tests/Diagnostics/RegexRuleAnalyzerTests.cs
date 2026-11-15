using Sharp.Validator.Fixtures;
using Sharp.Validator.SourceGenerator.Analyzers;
using Sharp.Validator.SourceGenerator.Tests.Fixtures;

namespace Sharp.Validator.SourceGenerator.Tests.Diagnostics;

public sealed class RegexRuleAnalyzerTests
{
    private const string ModelPrelude =
        """
        using System.Text.RegularExpressions;
        using Sharp.Validator;

        namespace App;

        public sealed class Model { public string? Name { get; set; } }

        """;

    [Fact]
    public async Task RegexRuleAnalyzer_FlagsALiteralStringPattern()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new RegexRuleAnalyzer(), ModelPrelude +
            """
            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.Name).Regex("^[a-z]+$")) { }
            }
            """, BuildProperties.PublishAot);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SVL010", diagnostic.Id);
    }

    [Fact]
    // An app extension named Regex that returns the library's builder isn't the library's rule, and its literal string
    // need not be a pattern at all.
    public async Task RegexRuleAnalyzer_LeavesAnAppExtensionNamedRegexAlone()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new RegexRuleAnalyzer(), ModelPrelude +
            """
            public static class AppRules
            {
                public static PropertyValidatorBuilder<TModel, string?> Regex<TModel>(
                    this PropertyValidatorBuilder<TModel, string?> builder, string label, int flags) => builder;
            }

            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.Name).Regex("label", 1)) { }
            }
            """, BuildProperties.PublishAot);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task RegexRuleAnalyzer_FlagsAnInlineNewRegexWithALiteralPattern()
    {
        // The Regex(Regex) overload with an inline literal construction is still an interpreted regex under Native AOT
        // — previously this shape escaped detection entirely.
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new RegexRuleAnalyzer(), ModelPrelude +
            """
            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.Name).Regex(new Regex("^[a-z]+$"))) { }
            }
            """, BuildProperties.PublishAot);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SVL010", diagnostic.Id);
    }

    [Fact]
    public async Task RegexRuleAnalyzer_IgnoresAPrecompiledRegexPassedByReference()
    {
        // The [GeneratedRegex] shape the code fix produces: a Regex resolved from a method — no literal to lift.
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new RegexRuleAnalyzer(), ModelPrelude +
            """
            public sealed partial class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.Name).Regex(NamePattern())) { }

                [GeneratedRegex("^[a-z]+$")]
                private static partial Regex NamePattern();
            }
            """, BuildProperties.PublishAot);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("")]
    // Under the JIT a literal pattern spreads over one instance per core, where a [GeneratedRegex] is one shared
    // instance that tops out near a single core's throughput, so the conversion is advice for Native AOT apps only.
    public async Task RegexRuleAnalyzer_UnlessTheAppIsPublishedWithNativeAOT_StaysSilent(string? publishAot)
    {
        var properties = publishAot is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["build_property.PublishAot"] = publishAot };

        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new RegexRuleAnalyzer(), ModelPrelude +
            """
            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.Name).Regex("^[a-z]+$")) { }
            }
            """, properties);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task RegexRuleAnalyzer_ReadsPublishAotCaseInsensitively()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new RegexRuleAnalyzer(), ModelPrelude +
            """
            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.Name).Regex("^[a-z]+$")) { }
            }
            """, new Dictionary<string, string> { ["build_property.PublishAot"] = "True" });

        Assert.Equal("SVL010", Assert.Single(diagnostics).Id);
    }
}