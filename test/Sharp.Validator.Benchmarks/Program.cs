using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Sharp.Validator.Benchmarks.Scenarios;

namespace Sharp.Validator.Benchmarks;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--check") return await SmokeCheck();

        var config = args.Contains(NativeAotJobs.Option) ? NativeAotJobs.Config(BenchmarkConfig()) : BenchmarkConfig();
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
            .Run([.. args.Where(static arg => arg != NativeAotJobs.Option)], config);

        return 0;
    }

    // The Generated accessor job builds this project again with another MSBuild property. Built in parallel with the
    // default build, the two would write the same obj and bin directories at once.
    private static IConfig BenchmarkConfig() =>
        DefaultConfig.Instance.WithArtifactsPath(ArtifactsPath()).WithOptions(ConfigOptions.DisableParallelBuild);

    private static string ArtifactsPath()
    {
        var path = typeof(Program).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static x => x.Key == "BenchmarkDotNetArtifactsPath")
            ?.Value;

        return Path.GetFullPath(path ?? "BenchmarkDotNet.Artifacts");
    }

    // Exercise the same methods BenchmarkDotNet runs, with assertions for their results and no timing.
    private static Task<int> SmokeCheck()
    {
        // A build with the generator must have registered its accessors, and one without must have none, or the
        // Generated job would time the same code as Fallback.
        Check($"Accessor path ({AccessorPath.Current})", AccessorPath.IsGenerated == AccessorPath.BuiltWithGenerator);

        // Nothing here runs --native-aot, so at least check that it keeps exactly its two jobs out of a scenario's own.
        var aotConfig = NativeAotJobs.Config(BenchmarkConfig());
        var aotCases = BenchmarkConverter.TypeToBenchmarks(typeof(RuleHotPathBenchmarks), aotConfig).BenchmarksCases;
        Check("Native AOT jobs", aotCases.Select(static benchmark => benchmark.Job.Id).Distinct().Order()
            .SequenceEqual([NativeAotJobs.Jit, NativeAotJobs.NativeAot]));

        foreach (var input in Enum.GetValues<Case>())
        {
            var values = new ValueRuleBenchmarks { Input = input };
            values.Setup();
            Check($"Value rules ({input})", values.Sharp() == (input == Case.Valid ? 0 : 3));
        }

        var rules = new RuleHotPathBenchmarks();
        Check("Flags enum", rules.FlagsEnum());
        Check("Dynamic regex", rules.DynamicRegex_CacheHit());
        Check("Exact length", rules.ExactLength_FuncBound());
        Check("Element property", rules.ElementProperty_10k_AllValid());
        foreach (var count in ParamValues<ElementWalkBenchmarks>(nameof(ElementWalkBenchmarks.Count)))
        {
            var walk = new ElementWalkBenchmarks { Count = count };
            walk.Setup();
            Check($"Element walk ({count})", walk.Elements());
            Check($"Element property walk ({count})", walk.ElementProperty());
            Check($"Two element property chains ({count})", walk.ElementProperty_TwoChains());
            Check($"Element rule and property chain ({count})", walk.ElementRule_AndProperty());
            Check($"Async element walk ({count})", walk.Elements_Async());
            Check($"Async element property walk ({count})", walk.ElementProperty_Async());
        }

        var chains = new ChainCountBenchmarks();
        chains.Setup();
        Check("One chain", chains.OneChain());
        Check("Twenty-four chains", chains.TwentyFourChains());

        var partial = new PartialValidationBenchmarks();
        partial.Setup();
        Check("Whole validation", partial.Whole());
        Check("One included property", partial.OneProperty());
        Check("One included nested path", partial.NestedPath());

        var condition = new SharedConditionBenchmarks();
        condition.Setup();
        Check("Ungated chains", condition.Ungated());
        Check("Shared sync condition", condition.SyncCondition());
        Check("Shared async condition", condition.AsyncCondition());
        foreach (var count in ParamValues<ElementIncludeBenchmarks>(nameof(ElementIncludeBenchmarks.Count)))
        {
            var include = new ElementIncludeBenchmarks { Count = count };
            include.Setup();
            Check($"Element include ({count})", include.Elements());
            Check($"Element property include ({count})", include.ElementProperty());
            Check($"Async element include ({count})", include.Elements_Async());
            Check($"Async element property include ({count})", include.ElementProperty_Async());
        }

        var lifetime = new ValidatorLifetimeBenchmarks();
        lifetime.Setup();
        Check("Singleton validator in a scope", lifetime.Singleton());
        Check("Scoped validator in a scope", lifetime.Scoped());
        lifetime.Cleanup();

        var fast = new AsyncFastPathBenchmarks();
        fast.Setup();
        Check("Sync validation", fast.Validate_Sync() == 0);
        Check("Async sync-only fast path", fast.ValidateAsync_SyncOnly() == 0);

        var lazy = new LazyResultBenchmarks();
        lazy.Setup();
        Check("Valid result", lazy.ValidPath_IsValidOnly());
        Check("Empty errors", lazy.ValidPath_ReadsErrors() == 0);

        foreach (var threads in new[] { 1, 2, 4, 8 })
        {
            var parallel = new ParallelThroughputBenchmarks { Threads = threads };
            parallel.Flat();
            parallel.OwnedPattern();
            parallel.SharedGeneratedRegex();
            parallel.Email();
            Console.WriteLine($"ok: Parallel throughput ({threads} threads)");
        }

        return Task.FromResult(0);
    }

    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local — checking it is what the method is for.
    private static void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException($"Benchmark check failed: {name}.");

        Console.WriteLine($"ok: {name}");
    }

    // The values a scenario's [Params] property declares, so the check runs every size BenchmarkDotNet runs.
    private static IEnumerable<int> ParamValues<TScenario>(string property) =>
        typeof(TScenario).GetProperty(property)!.GetCustomAttribute<ParamsAttribute>()!.Values.Cast<int>();
}