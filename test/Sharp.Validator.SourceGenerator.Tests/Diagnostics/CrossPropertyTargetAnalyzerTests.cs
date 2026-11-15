using Sharp.Validator.SourceGenerator.Analyzers;
using Sharp.Validator.SourceGenerator.Tests.Fixtures;

namespace Sharp.Validator.SourceGenerator.Tests.Diagnostics;

// SVL011: a cross-property target that is not a member chain is compiled for each validator instance, which Native AOT
// runs in the expression interpreter and trimming can break, so trimmed and AOT projects are warned.
public sealed class CrossPropertyTargetAnalyzerTests
{
    private static readonly IReadOnlyDictionary<string, string> Trimmed =
        new Dictionary<string, string> { ["build_property.EnableTrimAnalyzer"] = "true" };

    private static string Source(string rules) =>
        $$"""
          using Sharp.Validator;

          namespace App;

          public sealed class Policy { public int Min { get; set; } }

          public static class Limits { public static int Max { get; set; } }

          public sealed class Order
          {
              public int Quantity { get; set; }
              public int Minimum { get; set; }
              public Policy? Policy { get; set; }
          }

          public sealed class OrderValidator : Validator<Order>
          {
              public OrderValidator()
              {
                  {{rules}}
              }
          }
          """;

    [Theory]
    [InlineData("GreaterThan")]
    [InlineData("GreaterThanOrEqual")]
    [InlineData("LessThan")]
    [InlineData("LessThanOrEqual")]
    [InlineData("Equal")]
    [InlineData("NotEqual")]
    public async Task CrossPropertyTargetAnalyzer_FlagsAComputedTargetInEveryCrossPropertyRule(string rule)
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new CrossPropertyTargetAnalyzer(),
            Source($"Builder.Property(x => x.Quantity).{rule}(x => x.Minimum * 2);"), Trimmed);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SVL011", diagnostic.Id);
        Assert.Contains("'x => x.Minimum * 2'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    // An app extension named like a cross-property rule that returns the library's builder isn't the library's rule;
    // its expression is the app's to interpret.
    public async Task CrossPropertyTargetAnalyzer_LeavesAnAppExtensionWithARulesNameAlone()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new CrossPropertyTargetAnalyzer(),
            Source("Builder.Property(x => x.Quantity).Equal(x => x.Minimum * 2, \"twice the minimum\");") +
            """

            public static class AppRules
            {
                public static PropertyValidatorBuilder<TModel, int> Equal<TModel>(this PropertyValidatorBuilder<TModel, int> builder,
                    System.Linq.Expressions.Expression<System.Func<TModel, int>> target, string label) => builder;
            }
            """, Trimmed);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("x => Limits.Max")]
    [InlineData("x => 5")]
    [InlineData("x => x.Policy!.Min + 1")]
    [InlineData("x => x.GetHashCode()")]
    public async Task CrossPropertyTargetAnalyzer_FlagsATargetThatIsNotRootedAtTheParameterOrComputes(string target)
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new CrossPropertyTargetAnalyzer(),
            Source($"Builder.Property(x => x.Quantity).GreaterThan({target});"), Trimmed);

        Assert.Equal("SVL011", Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData("x => x.Minimum")]
    [InlineData("x => (x.Minimum)")]
    [InlineData("(x => x.Minimum)")]
    [InlineData("x => x.Policy!.Min")]
    [InlineData("(x) => x.Minimum")]
    public async Task CrossPropertyTargetAnalyzer_IgnoresAMemberChainTarget(string target)
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new CrossPropertyTargetAnalyzer(),
            Source($"Builder.Property(x => x.Quantity).GreaterThan({target});"), Trimmed);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("InclusiveBetween(x => x.Minimum, x => x.Minimum * 2)", "'x => x.Minimum * 2'")]
    [InlineData("ExclusiveBetween(x => x.Minimum * 2, x => x.Minimum)", "'x => x.Minimum * 2'")]
    public async Task CrossPropertyTargetAnalyzer_FlagsAComputedRangeBoundInEitherPosition(string rule, string target)
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new CrossPropertyTargetAnalyzer(),
            Source($"Builder.Property(x => x.Quantity).{rule};"), Trimmed);

        Assert.Contains(target, Assert.Single(diagnostics).GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossPropertyTargetAnalyzer_IgnoresRangeBoundsThatAreMemberChains()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new CrossPropertyTargetAnalyzer(),
            Source("Builder.Property(x => x.Quantity).InclusiveBetween(x => x.Minimum, x => x.Policy!.Min).ExclusiveBetween(1, 9);"),
            Trimmed);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CrossPropertyTargetAnalyzer_LiteralOrVariableTarget_IsIgnored()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new CrossPropertyTargetAnalyzer(), Source(
            """
            System.Linq.Expressions.Expression<System.Func<Order, int>> target = x => x.Minimum * 2;
                    Builder.Property(x => x.Quantity).GreaterThan(5).LessThan(target);
            """), Trimmed);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task CrossPropertyTargetAnalyzer_UnlessTheProjectIsAnalyzedForTrimming_StaysSilent(string? enableTrimAnalyzer)
    {
        var properties = enableTrimAnalyzer is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["build_property.EnableTrimAnalyzer"] = enableTrimAnalyzer };

        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new CrossPropertyTargetAnalyzer(),
            Source("Builder.Property(x => x.Quantity).GreaterThan(x => x.Minimum * 2);"), properties);

        Assert.Empty(diagnostics);
    }
}