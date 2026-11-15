using System.Reflection;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Sharp.Validator.ComparisonBenchmarks.Scenarios;

namespace Sharp.Validator.ComparisonBenchmarks;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--check") return await SmokeCheck();

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, BenchmarkConfig());

        return 0;
    }

    private static IConfig BenchmarkConfig() =>
        DefaultConfig.Instance.WithArtifactsPath(ArtifactsPath());

    private static string ArtifactsPath()
    {
        var path = typeof(Program).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static x => x.Key == "BenchmarkDotNetArtifactsPath")
            ?.Value;

        return Path.GetFullPath(path ?? "BenchmarkDotNet.Artifacts");
    }

    /// <summary>
    /// Runs each scenario's setup and benchmark methods once per input and fails, with exit code 1, when the libraries disagree on the error count: a
    /// benchmark whose libraries report different errors no longer compares the same work. Going through the scenario classes also fails the check when a
    /// setup or a benchmark method breaks. CI runs it on every pull request.
    /// </summary>
    private static async Task<int> SmokeCheck()
    {
        var mismatches = 0;
        foreach (var @case in (Case[])[Case.Valid, Case.Invalid])
        {
            var flat = new FlatModelBenchmarks { Input = @case };
            flat.Setup();
            mismatches += Report("Flat", @case, flat.Sharp(), flat.FluentValidation(), flat.DataAnnotations());

            var collection = new CollectionBenchmarks { Input = @case };
            collection.Setup();
            mismatches += Report("Collection", @case, collection.Sharp(), collection.FluentValidation());

            var nested = new NestedBenchmarks { Input = @case };
            nested.Setup();
            mismatches += Report("Nested", @case, nested.Sharp(), nested.FluentValidation());

            var account = new AsyncBenchmarks { Input = @case };
            account.Setup();
            mismatches += Report("Async", @case, await account.Sharp(), await account.FluentValidation());
        }

        if (mismatches == 0) return 0;

        await Console.Error.WriteLineAsync($"Parity check failed: {mismatches} scenario case(s) report different error counts.");

        return 1;
    }

    // Prints one scenario case and returns 1 when the libraries' error counts differ.
    private static int Report(string scenario, Case @case, int sharp, int fluent, int? dataAnnotations = null)
    {
        var agree = sharp == fluent && (dataAnnotations is not { } annotated || annotated == sharp);
        var suffix = dataAnnotations is { } count ? $" | DataAnnotations: {count}" : "";
        Console.WriteLine($"{scenario,-11} {@case,-8} | Sharp: {sharp} | FluentValidation: {fluent}{suffix}{(agree ? "" : " | MISMATCH")}");
        return agree ? 0 : 1;
    }
}