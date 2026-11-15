using BenchmarkDotNet.Attributes;

namespace Sharp.Validator.Benchmarks.Scenarios;

/// <summary>
/// Checks how the all-valid path's result is consumed. A validation that records nothing returns the shared <c>ValidationResult.Empty</c>, whose
/// <c>Errors</c> is an empty array, so reading <c>Errors</c> allocates no more than checking <c>IsValid</c>. Default job for tight ns numbers; the
/// meaningful, deterministic signal is Allocated.
/// </summary>
[MemoryDiagnoser]
public class LazyResultBenchmarks
{
    private readonly CustomerValidator _validator = new();

    private Customer _valid = null!;

    [GlobalSetup]
    public void Setup() => _valid = Data.Customer(Case.Valid);

    [Benchmark(Baseline = true)]
    // Realistic common case: pass/fail decision only.
    public bool ValidPath_IsValidOnly() => _validator.Validate(_valid).IsValid;

    [Benchmark]
    // Reads Errors: the empty result's list is shared too, so this allocates the same as the baseline.
    public int ValidPath_ReadsErrors() => _validator.Validate(_valid).Errors.Count;
}