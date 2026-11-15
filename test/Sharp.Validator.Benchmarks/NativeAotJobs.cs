using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;

namespace Sharp.Validator.Benchmarks;

/// <summary>
/// The jobs <c>--native-aot</c> runs in place of each scenario's own: the JIT as the baseline, and Native AOT. Both build with the source generator,
/// because a Native AOT app always does: without its accessors, Native AOT would interpret every selector's compiled expression. Ratio then reads as
/// what Native AOT changes for the same code, and shows costs that only Native AOT pays, which a JIT run can't.
/// </summary>
internal static class NativeAotJobs
{
    internal const string Option = "--native-aot";

    internal const string Jit = nameof(Jit);

    internal const string NativeAot = nameof(NativeAot);

    internal static IConfig Config(IConfig baseConfig) => ManualConfig.Create(baseConfig)
        .AddJob(Generated(Job.ShortRun.WithId(Jit)).AsBaseline())
        .AddJob(Generated(Job.ShortRun.WithId(NativeAot).WithRuntime(NativeAotRuntime.Net10_0)))
        // A scenario's attributes add their own jobs, and BenchmarkDotNet merges them by the attribute's rule, not this
        // config's. A filter, which runs before anything is built, keeps these two.
        .AddFilter(new SimpleFilter(static benchmark => benchmark.Job.Id is Jit or NativeAot))
        .HideColumns("EnvironmentVariables", "Arguments");

    private static Job Generated(Job job) => job
        .WithEnvironmentVariable(AccessorPath.JobVariable, AccessorPath.Generated)
        .WithMsBuildArguments($"/p:{AccessorPath.BuildProperty}={AccessorPath.Generated}");
}