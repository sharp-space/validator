using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Sharp.Validator.Benchmarks.Scenarios;
using Sharp.Validator.Bindings;

namespace Sharp.Validator.Benchmarks;

/// <summary>
/// Times a scenario on both accessor paths, as two ShortRun jobs in one table. Fallback runs this project as built, without the source generator, so
/// members are read through compiled expressions. Generated rebuilds it with the generator, whose accessors package users get and the JIT can inline.
/// Fallback is the baseline, so Ratio reads as the generated path against it.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AccessorJobsAttribute : Attribute, IConfigSource
{
    /// <summary>The two jobs.</summary>
    public IConfig Config { get; } = ManualConfig.CreateEmpty()
        .AddJob(Job.ShortRun
            .WithId(AccessorPath.Fallback)
            .WithEnvironmentVariable(AccessorPath.JobVariable, AccessorPath.Fallback)
            .AsBaseline())
        .AddJob(Job.ShortRun
            .WithId(AccessorPath.Generated)
            .WithEnvironmentVariable(AccessorPath.JobVariable, AccessorPath.Generated)
            .WithMsBuildArguments($"/p:{AccessorPath.BuildProperty}={AccessorPath.Generated}"))
        // The Job column already names the path; these two would repeat it.
        .HideColumns("EnvironmentVariables", "Arguments");
}

/// <summary>Which accessors this build reads members through.</summary>
internal static class AccessorPath
{
    internal const string Fallback = nameof(Fallback);

    internal const string Generated = nameof(Generated);

    // The project file applies the source generator when this property is Generated.
    internal const string BuildProperty = "SharpBenchmarkAccessors";

    // Each accessor job sets this to the path it times, so its process can check that it got that build.
    internal const string JobVariable = "SHARP_BENCHMARK_ACCESSORS";

#if GENERATED_ACCESSORS
    internal static bool BuiltWithGenerator => true;
#else
    internal static bool BuiltWithGenerator => false;
#endif

    // The generator seeds its accessors from a module initializer, before any code here runs. A key it computes
    // differently from the runtime falls back without an error, so this looks up keys the element walks read.
    internal static bool IsGenerated =>
        AccessorRegistry.HasElements<ElementWalkBenchmarks.Document, ElementWalkBenchmarks.Line>("Lines") &&
        AccessorRegistry.HasProperty<ElementWalkBenchmarks.Line, string?>("Label");

    internal static string Current => IsGenerated ? Generated : Fallback;

    /// <summary>
    /// Throws when an accessor job runs a build other than the one it names, so a Generated row can't quietly time the fallback. Outside the two jobs, such
    /// as in <c>--check</c>, there is nothing to compare.
    /// </summary>
    internal static void VerifyJob()
    {
        var expected = Environment.GetEnvironmentVariable(JobVariable);
        if (expected is not null && expected != Current)
        {
            throw new InvalidOperationException(
                $"The {expected} accessor job ran a build that reads members through {Current} accessors.");
        }
    }
}